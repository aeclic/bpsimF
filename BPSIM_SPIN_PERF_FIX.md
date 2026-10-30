# 载具转得越快越卡：Unity.Physics 速度膨胀 AABB 导致同车部件互撞

## 问题

导入蓝图「不败荣光」（1648 个部件 / 5250 根焊接关节）后，载具旋转越快卡顿越严重：
静止时 220-270 fps，整块高速自旋（ω=32 rad/s）时掉到 **4 fps、单帧 243ms**。

## 量化（`Bpsim.QA.SpinPerfProbe`）

探针本身就是复现器：加载 CSV → 开始模拟 → 按 ω 刚体自旋 → 采样 `Time.unscaledDeltaTime`。

```
// Play Mode 里通过 DSH 桥接调用（也可在任意 MonoBehaviour 里调用）
Bpsim.QA.SpinPerfProbe.Run("file=不败荣光;bisect=0;gravity=0;sweep=0;mode=rigid;inf=1;fix=0;spin=32");
```

报告写 `Logs/spinperf.log`。关键 spec：`fix=0/1`（旧行为 / 新过滤）、`inf=1`（焊接强度无限，载具保持整块）、
`factor`（焊接强度倍率）、`trans`（整体平移）、`nocollide`、`spin`、`dur`、`bisect`。

| 场景（ω=32 整块自旋，gravity=0） | 修复前 | 修复后 |
| --- | --- | --- |
| `inf=1`（不拉断，整块刚性旋转） | **242.9ms**（4.1 fps，p99 576ms） | **6.4-7.0ms**（143-155 fps，p99 18ms） |
| 默认焊接强度（会拉断成 140 个连通块） | 6.4ms | 7.2ms |
| 整体平移 400 格/秒（关节不断，纯速度） | 7.45ms | 5.66ms |
| 静置（warmup） | 4.0-4.9ms | 3.8-4.3ms |

静置与断裂后两者相当（碎片本身很小，没有接触爆炸），**差距全在「整块刚性旋转」这种状态上** ——
那正是用户说的「载具转得越快越卡」。

## 根因

Unity.Physics 的宽相位不是按真实包围盒建树，而是按**速度膨胀后的 AABB** 建（推测接触 / speculative contact）：

```
Library/PackageCache/com.unity.physics@1.0.0-pre.44/Unity.Physics/Dynamics/Motion/Motion.cs:150-186
MotionExpansion MotionVelocity.CalculateExpansion(float timeStep) {
    Linear  = LinearVelocity * timeStep;                       // 平移
    Uniform = min(|AngularVelocity| * timeStep * angleExpansion, angleExpansion);  // 转动
}
Library/PackageCache/com.unity.physics@1.0.0-pre.44/Unity.Physics/Collision/World/Broadphase.cs:772
expansion.ExpandAabb(body.CalculateAabb())
```

整块以 ω 自旋时，半径 20 格处的部件线速度 = ω×r：

- ω=8 → 160 格/秒 → 每个固定步（0.02s）AABB **沿速度方向膨胀 3.2 格**；
- ω=16 → 320 格/秒 → 膨胀 6.4 格；
- ω=32 → 640 格/秒 → 膨胀 12.8 格。

1648 个 1×1 部件的膨胀体两两重叠，宽相位对数和窄相位推测接触一起爆炸，
`PhysicsWorldExporter` / `Broadphase` / `Narrowphase` 全部被拖死。实测（同一份蓝图、同一 ω）：

- 把每个部件的 `CollidesWith` 清零（`nocollide=1`）：20.8ms → **5.1ms**；
- 无限焊接强度 + 关碰撞：73.7ms → **4.7ms**（保持刚性不断链反而最慢，因为整块不会散开）；
- 纯平移 400 格/秒（关节不断、部件之间没有相对运动）：3.06 → 8.41ms，只涨不跌 ——
  证明开销来自「速度 → AABB 膨胀 → 部件间接触」，而不是关节求解。

## 修复

核心事实：**同一焊接连通块内的部件之间的接触没有任何意义**。它们已经被 fixed joint 焊死，
`AddFixedJoint(..., enableCollision: false)` 也让相邻部件不互撞；高速旋转时唯一「碰」到彼此的是
膨胀出来的推测接触。

所以给每个连通块分配一个 `CollisionFilter` 类别位（位 0 留给地形/关卡/道具，位 1..30 给连通块，
按 `componentIndex % 30` 取模），**同一连通块内部互不碰撞，不同连通块照常互撞**。

> 连接强度（`ConnectionStrengthFactor`、`AddFixedJoint` 的 `maxImpulse`、`RemoveJointsJob` 的断链判定）
> **没有任何改动**。但结果上会不一样：改动前 ω=32 整块自旋会把 5250 根关节拉断到 1993 根（353 块），
> 改动后只断到 3169 根（140 块）—— 因为「假接触」本身会产生巨大冲量、触发关节的 impulse event。
> 想要完全旧行为，见下方 `PartSelfCollision`。

### 关键障碍：碰撞体 blob 是共享的

第一版只改过滤，结果 `Apply` 报「1648 个部件只改动了 25 个」、断链后所有部件又退回同一个类别。
原因：`Instantiate` 出来的部件共用 prefab 的 `BlobAssetReference<Collider>`，而 `CollisionFilter`
存在 **blob 里**（`Collider.GetCollisionFilter()`），改一个等于改一片。1648 个部件实际只有 25 份 blob。

所以修复拆成两步（`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartCollisionFilter.cs`）：

1. `IsolateColliders(entityManager, simulator)`：统计每份 blob 被多少部件引用；
   共享的用 `PartPhysics.CreateScaled(blob, (1,1))` 克隆一份独占 blob（几何/材质/过滤原样保留），
   替换 `PhysicsCollider` 并挂 `PartIsolatedCollider` 标记；已经独占的只打标记。
   克隆出来的 blob 交给 `PartSimulatorUnmanaged.TrackScaledCollider`，随 `Dispose` 释放。
2. `Apply` / `ApplyInJob`：只写带 `PartIsolatedCollider` 标记的部件，按 `PartConnectedComponent.Index`
   写 `FilterFor(index)`。

接线：

- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartSimulator.cs`：
  `commandBuffer.Playback()` 之后 `IsolateColliders(...)` 再 `Apply(...)`（每次开始模拟各一次）。
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartSimulationSystem.cs`：
  关节被拉断（`RemoveJointsJob` 置 `Dirty`）后连通块重算，`UpdateComponentsJob` 里紧接着
  `PartCollisionFilter.ApplyInJob(parts, componentIndexes, ref colliderLookup, ref isolatedLookup)`，
  用同一份刚算出来的索引，避免在同一个 job 里再从 `ComponentLookup` 读回刚写的值。

### 为什么不用别的做法

- **只靠 `PhysicsJoint(enableCollision: false)`**：只覆盖「相邻」的焊接对；真正爆的是不相邻但 AABB 重叠的部件。
- **整体关掉部件间碰撞**（`CollidesWith=0` 或把所有部件设成同一类别且不互碰）：性能同样好，但多载具/碎片之间
  也会互穿，属于玩法回归。按连通块分类别只在「已经焊死」的关系上去掉碰撞。
- **改 `PhysicsStep`**：`PhysicsStep` 只有 SimulationType / Gravity / SolverIterationCount /
  SolverStabilizationHeuristicSettings / MultiThreaded / SynchronizeCollisionWorld，没有限制膨胀的开关。

## 取舍与已知限制

- **可以一键恢复旧行为**：`SimulationSettings.PartSelfCollision = true` 时跳过隔离与分类，
  完全回到改动前的原生行为（实测 ω=32 整块自旋 233ms / 4.3fps，`isolated=0`、过滤保持 Default）。
  默认 `false`（修复生效）。探针对应 `selfcollide=1|0`。
- **30 个类别**：Unity.Physics 的 `CollisionFilter` 只有 32 位，位 0 归地形，位 31 不用。载具被拉断成
  >30 个连通块时按 `index % 30` 复用类别 —— 「同一连通块永不互碰」始终成立，代价是「不同连通块但落到
  同一类别」的碎片之间会互穿。对高速碎片观感影响很小，换来的是断链后不会退回旧行为。
- **每个部件一份碰撞体 blob**：只在 blob 被共享时克隆。本蓝图 1648 个部件 → 1648 份，
  `StartSimulation` 实测 88.5ms（含建关节），一次性开销。prefab 带复合碰撞体的部件会深拷贝子体，
  内存相应增加。
- **不支持重建的形状**（例如将来出现 `MeshCollider`）不克隆、不打标记、也不写过滤：保持旧行为，
  绝不会因为「改一份 blob 连累一片」而把别的部件过滤改坏。

## 验证：相邻焊接件本来就不碰撞

修复只去掉「同一连通块里**没**被焊住、但几何重叠/贴面」的部件之间的接触，相邻焊接件原本就不碰 ——
这一点有源码和运行时两重证据：

- 建关节的每一处都传 `enableCollision: false`：
  `PartSimulator.BuildJointsAndComponentsJob` 的三条路径（容器 `PartSimulator.cs:221`、四邻域 `:241`、
  下层 `:274`）、`PartSimulatorUnmanaged.AddFixedJoint`（写进 `PhysicsConstrainedBodyPair`，
  `PartSimulatorUnmanaged.cs:95-119`）、`FrameJointSystem.AddFrameJoints`（`FrameJointSystem.cs:165`）。
  运行时 5250 根关节的 `PhysicsConstrainedBodyPair.EnableCollision` 全部为 0。
- Unity.Physics 自己也认这个位：`PhysicsWorldBuilder.cs:945` 把组件值拷进 world 的 joint 数据，
  `Scheduler.cs:891-892` 里「这一对只要有一根 joint 不允许碰撞，就不再为它生成接触对」
  （不允许碰撞时该对的 contact dispatch pair 被标成 `invalidPhaseId` 跳过）。
- 对照实验（`SpinPerfProbe` 新增 `jointcollide=0|1`，运行时改这 5250 个位）：
  同一蓝图、`selfcollide=1`（关掉本次修复的过滤，只剩关节位在起作用）、`inf=1`、ω=8：
  `jointcollide=0` → 15.30ms / 65fps；`jointcollide=1` → 32.59ms / 30fps，且静置热身阶段
  `avgW=0.26 maxW=22.87`（关节被接触顶着抖），说明接触确实生成了、并且在跟固定关节打架。

反过来说，**相邻但没焊住**（`CanConnectTo` 不成立、斜向、或同格非容器件）以及同连通块里隔着一格
却因 AABB 膨胀而重叠的部件，改动前是真的在互相碰撞 —— 那才是 243ms 的来源。

## 相关文件

- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartCollisionFilter.cs`
  —— `PartIsolatedCollider`、`FilterFor`、`IsolateColliders`、`Apply`、`ApplyInJob`、
  诊断用的 `Describe` / `CountMismatches` / `Reset`。
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartSimulator.cs`
  —— `Run()` 里 `IsolateColliders` + `Apply` 的接线；`ApplyFreeScaleJob` 仍按自由缩放重建几何。
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartSimulationSystem.cs`
  —— 断链重算连通块后刷新过滤（`PhysicsCollider` / `PartIsolatedCollider` 两个 lookup 必须在
  `__AssignHandlesImpl` 里注册，否则每帧 `NullReferenceException`，见下文）。
- `Assets/Scripts/Assembly-CSharp/Bpsim/QA/SpinPerfProbe.cs` —— 复现/回归用的探针（`fix=` 切换新旧行为）。

## 踩过的坑

- **`ComponentLookup<T>` 字段必须在 `__AssignHandlesImpl` 里 `state.GetComponentLookup<T>()` 注册**。
  只声明字段再 `Update(ref CheckedStateRef)` 会在
  `Unity.Entities.ComponentSafetyHandles.GetTypeArrayIndex` 里 NRE，而且这个系统每帧从头抛异常，
  表现为「`PartSimulationSystem` 好像没跑」：连通块永远 1、断链后过滤不刷新。
- **Burst job 里抛的异常不会重抛**：`Assets/Scripts/BpsimCompat/BpsimPhysicsCompat.cs` 在
  `[RuntimeInitializeOnLoadMethod]` 里关了 `JobsUtility.JobDebuggerEnabled`（否则 player 的 DOTS DLL 在
  `PhysicsWorldExporter+ExportDynamicBodiesJob` 上会 `IndexOutOfRangeException` 然后原生崩溃），
  代价是 job 内异常被吞。排这种问题时在 job 里 `try/catch` 把异常写进静态字段再读。
- **新载入的场景实体这一帧没有 `WorldTransform`**：`PartEntityFactory` 只加 `LocalTransform` +
  `LocalToWorld`，`WorldTransform` 由 `LocalToWorldSystem` 在下一个 `TransformSystemGroup` 才补。
  `PartSimulatorUnmanaged.AddFixedJoint` 需要它，所以「载入场景 → 同帧开始模拟」会抛
  `A component with type:Unity.Transforms.WorldTransform has not been added to the entity`，
  job 静默中断、`Joints=0`、车根本没焊起来。探针里在 `LoadSceneFile` 后等两帧再 `StartSimulation`。

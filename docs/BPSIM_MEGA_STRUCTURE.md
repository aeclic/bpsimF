# 巨构优化：整车刚体化（MegaStructure）

> 目标：大型存档（2 万部件的「天使座」）启动模拟要十几秒、运行时每秒 2~4 帧。
> 本功能把**整辆车压成一个刚体**参与物理，部件退化成"挂在刚体上的贴图"，只在**受击超过阈值**时才掉下来、重新变成独立刚体。
> 参考：Robocraft 2.1 的整车刚体化（单 Rigidbody + 复合碰撞体 + 部件纯视觉子物体 + 零关节）与 BPLE 的「可破坏南瓜」（受击阈值 → 破坏 → 分裂）。

## 1. 怎么用

设置里两个开关（`SimulationSettings` 的属性，设置面板会**自动**为它们各生成一行，见 `UserSettingsPanel.Bind` 的 `AddSettingItems`）：

| 属性 | 默认 | 含义 |
| --- | --- | --- |
| `MegaStructure` | `false` | 开启整车刚体化 |
| `MegaStructureBreakSpeed` | `8.0` | 部件掉落阈值：接触点的**法向接近速度**（m/s）。`0` = 永不掉落 |

探针对应 spec：`mega=1` / `breakspeed=6`。

## 2. 效果（实测，编辑器 2022.2.23f1c1，`maximumDeltaTime` 压到 `0.02` 保证「一帧 = 一固定步」）

存档「天使座」= 20405 部件，开启前 180832 根关节、`components=1`（整个存档是一个巨型求解岛）：

| | StartSimulation | 静置每步 | ω=32 自旋每步 |
| --- | --- | --- | --- |
| mega=0（原样） | 771–898 ms | 238.4 ms | 358.1 ms |
| mega=1 | **458–483 ms（−40%）** | **33.4 ms（−86%）** | **31.8 ms（−91%）** |

小存档「不败荣光」= 1648 部件（5250 关节）：

| | StartSimulation | ω=32 每步 |
| --- | --- | --- |
| mega=0 | 86.0 ms | 6.30 ms |
| mega=1 | **65.8 ms（−24%）** | **3.80 ms（−40%）** |

两个存档都是「部件数越多赚得越多」——因为省掉的是**关节求解**（每部件 8.9 根关节）与**巨型岛迭代**，这两项随部件数超线性增长。
小存档本来就不卡，收益自然有限；20k 存档才是这个功能的靶子。

## 3. 架构

新增 3 个文件（都在 `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/`）：

| 文件 | 内容 |
| --- | --- |
| `MegaStructureComponents.cs` | 4 个组件：`MegaPartOffset`（部件相对整车重心的偏移+朝向+质量）、`MegaPartDamage`（累计伤害，预留）、`MegaVehicle`（整车标记）、`MegaHit`（伤害系统内部的命中记录） |
| `MegaStructure.cs` | 状态机与所有重活：`Build`（建整车实体+复合体+给部件挂偏移）、`MirrorToParts`（把刚体位姿/速度写到部件）、`FlushDetachQueue`（处理掉件）、`RebuildCompound`（重建复合体）、`Dispose` |
| `MegaStructureSystems.cs` | 3 个 SystemBase：`MegaStructureDriveSystem`（玩法施力 → 整车冲量）、`MegaStructureMirrorSystem`（刚体 → 部件）、`MegaStructureDamageSystem`（碰撞事件 → 摘件） |

改动的既有文件：

| 文件 | 改动 |
| --- | --- |
| `PartSimulator.cs` | mega 时**整段跳过** `BuildRigidbodiesJob`（部件不加 `PhysicsWorldIndex` = 不进物理世界 = 不产生 body），只跑连通块计算（`BuildJointsAndComponentsJob.CreateJoints = false` 不建关节），之后 `MegaStructure.Build(...)` |
| `WheelSystem.cs` / `AutoGunProjectileSystem.cs` | `BelongsToPart` 认整车刚体（否则射线/扫掠会打到自己） |
| `SpinPerfProbe.cs` | `mega=` / `breakspeed=` / `megadrop=` / `megadetach=` / `writev=` 等 spec 与诊断输出 |
| `SimulationSettings.cs` | 两个属性 + 默认值 + `Update` 复制 |

### 关键设计：部件为什么还能"被施力"

全部托管玩法施力都走唯一入口 `PartGameplayUtil.AddVelocity(Entity, float3 delta)`（9 处调用），它只做 `PhysicsVelocity.Linear += delta`。
巨构模式下部件**保留 `PhysicsVelocity` 但不进物理世界**，于是：

```
玩法代码（一行没改）→ 改部件速度 → Drive 每步读偏差 → 汇总成整车冲量 → 清空部件速度
```

`MegaStructureDriveSystem` 挂在 `PhysicsInitializeGroup`（**物理步进之前**），单遍遍历部件：

```
expected = MirrorLinear + ω × r            // r = 部件相对重心的偏移
impulse  = (partVelocity - expected) * partMass
整车速度 += Σimpulse / 总质量；整车角速度 += (Σ(r × impulse) 转到惯性系) / 惯性张量
然后把部件速度复位成 expected（防止同一次施力被重复计入）
```

### 三个必须遵守的不变量（踩过的坑，别改回去）

1. **Drive 的期望速度基准必须是「上次镜像时的整车状态」，不是当前整车状态。**
   部件没有物理体，速度是上次镜像写进去的、天然落后一步。若拿当前整车速度当基准，
   重力每步加的 Δv 会被当成玩家施力、生成**反向**冲量，正好抵消重力
   —— 实测整车下落速度一直卡在 −1.5 m/s，永远不会加速。
   基准存在 `MegaStructure.MirrorLinear/MirrorAngularWorld/MirrorRotation/MirrorCenterOfMass`。
   同理：**外部直接改整车速度后必须调 `MegaStructure.SyncMirror(em)`**（探针的 `megadrop` 就是这样）。
2. **整车刚体原点放在复合体质心。** 子碰撞体的 `CompoundFromChild` 用「部件位置 − 质心」，
   `LocalTransform.Position = 质心`。否则质心偏移几百单位 → 惯性张量按平行轴定理虚高、
   `Constraint2D`（作用于质心速度）与实体原点语义错位 → 整车无外力也自己漂移。
   质心按**体积加权**累加（与 `CompoundCollider.BuildMassProperties` 同口径）。
3. **复合体必须显式打开碰撞事件。** `Material.Default` 的响应是 `Collide`，**不抛事件**，
   `ICollisionEventsJob` 什么也收不到 → 「受击掉件」永远不触发。
   `MegaStructure.EnableCollisionEvents` 对复合体调 `SetCollisionResponse(CollideRaiseCollisionEvents)`
   （响应是两侧按位或，所以只改整车这边就够）。改的是复合体自己的 blob 副本，不污染部件共享的 collider。

## 4. 掉件链路（已验证）

`MegaStructureDamageSystem`（`AfterPhysicsSystemGroup`，`UpdateBefore(MegaStructureMirrorSystem)`）用 `ICollisionEventsJob` 消费本步碰撞事件：

- 事件里 `ColliderKey` 的高 `ChildKeyBits` 位 = 子件下标（`CompoundCollider` 的 `NumColliderKeyBits`，1648 部件 = 11 位，20405 部件 = 15 位），借此还原"是哪个部件被撞了"。
- 强度 = `|dot(部件在接触点的速度 − 对方速度, 法向)|`，其中**部件速度用步进前的整车速度**（`LastBodyLinear + ω × r`）。
  ⚠️ **不能用 `AfterPhysicsSystemGroup` 里读到的整车速度** —— 那时已被求解器压扁，实测 20 m/s 撞地只读到 0.04，永远不达标。
- 超过 `BreakSpeed` → 记 `MegaHit`，主线程按速度排序，每步最多摘 `MaxDetachPerStep = 24` 个。
- 摘件在下一步**物理步之前**（`FlushDetachQueue`）做：从 children 移除、`RebuildCompound` 重建复合体、给该部件加 `PhysicsWorldIndex` → 它立刻成为独立刚体，自己掉、自己滚。

实测（`perf/buhai.csv`，`mega=1;breakspeed=6;gravity=-9.81;megadrop=20;writev=0`，向下 20 m/s 砸地面）：

```
mega end: parts=1641 detached=7 mass=1229.70 ...
          evtsTotal=2708 peakImpact=20.47 detachedTotal=8
partsWithPhysics=7/1648
```

`peakImpact=20.47` 正是坠地速度、`detachedTotal=8`、`partsWithPhysics=7` ⇒ **受击 → 掉件 → 变独立刚体**整条链路成立。
机械部分另有确定性验证：`megadetach=40` → `parts=1608 detached=40 partsWithPhysics=40/1648`。

## 5. 第二轮优化：每步 94 ms → 33 ms（−65%）

第一版能跑之后逐层二分（`groupbisect=1` / `sysbisect=1`，`gravity=0;writev=0` 保证不漂移），
发现「mega 模式物理本体几乎免费」——关掉 `PhysicsSimulationGroup` 完全不省时间——
剩下的全在 bpsim 自己的代码里。三处修完之后单步从 **94 ms 降到 33 ms**：

| 优化 | 改动 | 效果 |
| --- | --- | --- |
| 镜像改 `IJobChunk` | `MegaStructureMirrorSystem` 原来是两万个部件逐个 `EntityManager.GetComponentData/SetComponentData`（每部件 3 次托管调用），改成按 `MegaPartOffset+LocalTransform+PhysicsVelocity` 查询的并行 chunk job | 94.0 → 71.1 ms |
| 驱动改 `IJobChunk` | `MegaStructureDriveSystem` 的 `DriveJob` 从串行 `IJob`（自己遍历 chunk 数组）改成并行 `IJobChunk`，每个 chunk 累加到 `LinearSums/AngularSums` 独立槽位，主线程再归约一次 | 71.1 → 62.0 ms |
| **轮子射线按类别位剪枝** | 整车复合体在 `Build` 里用 `ApplyVehicleFilter` 打上独占类别位 `MegaStructure.VehicleCategory = 1u<<31`（`CollisionFilter{BelongsTo=该位, CollidesWith=~该位}`）；`WheelSystem.RaycastGround` 在 mega 模式下用 `CollidesWith=~VehicleCategory` 的射线过滤 | 62.0 → **33.3 ms** |

第三条是最大头（−28 ms）：存档里 **1161 个轮子**，每个轮子每步打一条 `AllHitsCollector` 接地射线，
而巨构模式下车是**一个带 20405 个子件的复合碰撞体**，射线起点落在复合体 BVH 深处、还要在半径内收一串自命中再逐个过滤。
给了独占类别位之后整棵子树在宽相位就被剪掉，射线只看得到真正的地面。

第二轮之后的分布（20k，mega=1，`groupbisect`）：

```
group base 33.37ms
  no-FixedStepGroup        11.41ms   ← 固定步以外的渲染/表现只有 ~11ms
  no-PartSimulationGroup   22.37ms   ← 玩法系统（Wheel/Circuit/... 13 个）约 12ms
  no-PhysicsSimulationGroup 37.26ms  ← 关掉 Unity.Physics 反而不省 ⇒ 物理本体已经是零头
```

`StartSimulation` 里 `MegaStructure.Build` 的实测拆分（`[MegaStructure] build:` 日志）：

```
parts=20405 gather=80ms compound=211ms vehicle+mirror=22ms total=313ms
```

- `gather` = 两万次 `HasComponent/GetComponentData` + 给每个部件 `AddComponentData`（PhysicsMass/Velocity/Damping/Offset/Damage）。
- `compound` = `CompoundCollider.Create` 建 20k 子件的 BVH，是接下来唯一的大头（见下）。

## 6. 已知限制 / 可选的下一步

1. **子碰撞体数 = 部件数**（2 万个 box），`CompoundCollider.Create` 建 BVH 实测 211 ms，是 `StartSimulation` 里最大的一块。
   Robocraft 的做法是把部件按空间树节点**合并成少量大盒子**（误差阈值 1.5），盒数只随几何复杂度增长。
   收益：启动更快（211 ms → 几十 ms）、宽相位更省。
   **代价比预想大**：复合体的 `ColliderKey` 子件下标就是「掉件」时反查部件的唯一凭据（`MegaStructureDamageSystem` 用
   `key.Value >> (32-KeyBits)` 还原部件），合并之后一个子件覆盖多个部件，一次命中只能整块矩形一起掉，
   还得额外维护「子件下标 → 部件列表」的映射并在掉件后重建。所以这一项要连同掉件语义一起设计。
2. **没有连通性分裂**：真断裂会分成几个岛各自飞，这里掉下来的部件是单体（Robocraft 也没有岛分裂）。要做就得在摘件后对剩余部件跑一次 flood fill 再拆多个复合体。
3. **`MegaPartDamage` 目前没用**：现在的判定是"单次冲击强度 > 阈值"，不是"累计伤害 > 阈值"。要手感更接近"打几下才掉"，可以在这个组件上累加、按部件等级改阈值。
4. **Drive/Mirror 仍是每步全量遍历**（各 2 万实体）。改成 job 之后已经很便宜（合计约 5 ms），再往上压可以只处理"本步被施力的部件"（dirty 列表）。
5. **`MegaStructure` 与「部件自碰撞过滤」互斥**：mega 模式没有关节也没有逐部件 body，`PartCollisionFilter` 整段跳过（`coverage isolated=0 multiBit=20402` 是探针报的复合体状态，不参与过滤）。
6. **`gather` 的 80 ms** 里有一半是给 2 万部件逐个 `AddComponentData`（结构性变更）。要用 `EntityCommandBuffer` 批处理或把这些组件做进 `PartEntityFactory` 的原型里才能再压。

## 7. 复现命令

```bash
# 在 bpsim-main 目录下（编辑器需先进 Play Mode 到 MainScene）
perf/run.sh "file=perf/angel.csv;mega=0;spin=32;sweep=0;bisect=0;dur=2.0" 0.02   # 基线
perf/run.sh "file=perf/angel.csv;mega=1;spin=32;sweep=0;bisect=0;dur=2.0" 0.02   # 巨构

# 受击掉件
perf/run.sh "file=perf/buhai.csv;mega=1;breakspeed=6;gravity=-9.81;megadrop=20;writev=0;forcebisect=1;dur=1.0" 0.02
```

- `perf/run.sh "<spec>" [maxDelta]`：进 Play → 设 `maximumDeltaTime` → 跑探针 → 等 `=== done ===` → 打印本次 `[SpinPerf]` 行 → 退 Play → 恢复。
- `perf/build.sh`：退 Play → `CompilationPipeline.RequestScriptCompilation` → 轮询 DLL mtime → 打印 `error CS`。**本工程 `AssetDatabase.Refresh` 经常不触发重编译；且绝不能在 Play 模式下 Refresh。**
- `perf/angel.csv` = 「天使座」20405 部件（ASCII 副本，桥接传中文会乱码）；`perf/buhai.csv` = 「不败荣光」1648 部件。
- `writev=0` 关掉探针的"速度场写入"（那等于假装玩家每步在施力）；做真实动力学测试必须关。

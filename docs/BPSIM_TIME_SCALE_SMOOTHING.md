# 时间倍速低倍速卡顿修复（参考 Besiege）

## 问题

时间倍速滑条（`Assets/Scripts/Assembly-CSharp/Bpsim/UI/HudSlider.cs`）把 0..100% 直接映射成
`Time.timeScale = m_timeValue * 2f`。倍速调低后，模拟看起来"一卡一卡的"，而 Besiege 在同样的慢放倍速下
是连续流畅的（只是慢）。

## 根因

DOTS 物理跑在 `FixedStepSimulationSystemGroup` 里，用的是 `RateUtils.FixedRateCatchUpManager`：

```
Library/PackageCache/com.unity.entities@1.0.0-pre.44/Unity.Entities/RateUtils.cs:173-217
finalElapsedTime = min(m_MaxFinalElapsedTime, World.Time.ElapsedTime)
if (finalElapsedTime - m_LastFixedUpdateTime >= m_FixedTimestep) { m_LastFixedUpdateTime += m_FixedTimestep; ... }
```

`World.Time` 是按 `Time.deltaTime`（已经被 `Time.timeScale` 缩放）推进的，所以倍速越低，
"哪一帧轮到物理步"就越稀疏：0.25x 时大约每 4 个渲染帧才有一个物理固定步。

而物理步是唯一会写 `LocalTransform` 的时机（`PhysicsWorldExporter`），渲染侧

- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/SceneRenderSystem.cs:341` 每帧 `PopulateBuffer()`，
- `CopyPropertiesJob` 把每个实例的 `LocalToWorld` 直接 `PackMatrix` 传进 BatchRendererGroup 的 GraphicsBuffer，

中间**没有任何插值**。于是渲染只在物理步那一帧跳一次位置——这就是"一卡一卡"。

Besiege（`D:\Program Files (x86)\Steam\steamapps\common\Besiege\Besiege_Data\Managed\Assembly-CSharp.dll`）
的做法与之对比：它的 `Assembly-CSharp.dll` 里 `set_timeScale`/`get_timeScale` 各命中 1 次，而
`set_fixedDeltaTime`/`set_maximumDeltaTime`/`set_captureDeltaTime` 全部 0 次——它只动 `Time.timeScale`，
平滑靠的是 Unity 刚体的 `RigidbodyInterpolation.Interpolate`（在渲染前把 transform 插到两个物理 tick 之间）。

## 改法

等价物在 Unity.Physics 里现成有：`PhysicsGraphicalSmoothing` + `PhysicsGraphicalInterpolationBuffer`
+ `SmoothRigidBodiesGraphicalMotion`（`Unity.Physics/ECS/GraphicsIntegration/`）。这正是 Unity.Physics 官方
baking 给开了 `BodySmoothing.Interpolation` 的刚体加的那套组件。

| 文件 | 改动 |
| --- | --- |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartPhysics.cs` | 新增 `EnableGraphicalInterpolation(EntityManager/EntityCommandBuffer, Entity, RigidTransform)`：加 `PhysicsGraphicalSmoothing`、`PhysicsGraphicalInterpolationBuffer`（`PreviousTransform` = 生成时的姿态，避免第一帧从原点飞入）、`PropagateLocalToWorld`。 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartSimulatorUnmanaged.cs` | `AddPhysicsComponents(...)` 末尾调用上面的 ECB 版——模拟开始时的**所有**部件刚体都走这里（`PartSimulator.BuildRigidbodiesJob`）。 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartGameplayUtil.cs` | `SpawnPart(...)` 里运行中生成部件的那条刚体路径调用 EntityManager 版。 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartWorldTransformSyncSystem.cs`（新增） | `[UpdateInGroup(TransformSystemGroup)]`、`[UpdateAfter(SmoothRigidBodiesGraphicalMotion)]`、`[UpdateBefore(LocalToWorldSystem)]`：把 `WorldTransform` 刷成 `LocalTransform`。 |

### 为什么需要那个新系统

`PhysicsGraphicalSmoothing` 标了 `[WriteGroup(typeof(LocalToWorld))]`，而
`LocalToWorldSystem._rootsQuery` 用了 `EntityQueryOptions.FilterWriteGroup`
（`LocalToWorldSystem.cs:281-287`）。`EntityQueryManager` 会把 RW 组件的写组成员塞进 none
（`EntityQueryManager.cs:186-205, 294-326`），于是**带 `PhysicsGraphicalSmoothing` 的根实体会被根作业整体跳过**
——这正是我们要的（否则每帧都会被未插值的矩阵覆盖）。但同一个作业也负责刷 `WorldTransform`，所以这些实体
的 `WorldTransform` 会永久停在旧值。项目里还有非渲染路径在读 `WorldTransform`
（`FrameJointSystem` 焊接帧、`PartSimulator`、`PartPropertyPanel` 的角度显示），因此补一个系统把
`WorldTransform = (WorldTransform)LocalTransform` 写回去。部件根实体没有 `Parent`，两个组件本就该相等，
这一步是幂等的。

### 子渲染实体

一个部件根实体下面挂着渲染子实体（`PartEntityFactory.CreateChild` 加 `Parent`）。`LocalToWorldSystem`
的 child job 只有在父实体带 `PropagateLocalToWorld` 时才用父的（插值后）`LocalToWorld` 矩阵去乘
（`ChildLocalToWorldFromTransformMatrix`），否则用父的原始 `WorldTransform`
（`ChildLocalToWorldFromTransformData`）——后者会把插值丢掉，子四边形继续一卡一卡。所以三个组件是一套。

## 为什么不动 `Time.timeScale`

和 Besiege 保持一致：滑条继续只写 `Time.timeScale`，物理步长保持不变。备选方案（按倍速缩放
`FixedStepSimulationSystemGroup.Timestep`，或换 `FixedRateSimpleManager` 每帧一步）会改变物理步长，
求解器行为和稳定性都会跟着变，属于"改物理"而不是"改观感"。

## 验证

`Assets/Editor/TimeScaleSmoothingQA.cs`（批处理入口）：

```
Tools\unity-batch-bpsim.bat -executeMethod Bpsim.Editor.TimeScaleSmoothingQA.Run
```

它断言（而不是"读包源码相信它"）：

1. `SmoothRigidBodiesGraphicalMotion` / `BufferInterpolatedRigidBodiesMotion` / `RecordMostRecentFixedTime` 都被自动创建；
2. `PartWorldTransformSyncSystem` 已注册；
3. 与 `LocalToWorldSystem._rootsQuery` 同形状的查询**匹配普通根实体、排除带 `PhysicsGraphicalSmoothing` 的根实体**；
4. 补写系统的查询命中集合正好等于被根作业跳过的集合；
5. `PhysicsGraphicalInterpolationBuffer` 没有 `WriteGroup`（物理侧的 buffer 系统仍能匹配同一实体）。

报告写到 `Logs/timescale-smoothing-qa.txt`，`FAILURES: 0` 为通过。实测结果：

```
PASS  SmoothRigidBodiesGraphicalMotion is auto-created
PASS  BufferInterpolatedRigidBodiesMotion is auto-created
PASS  RecordMostRecentFixedTime is auto-created
PASS  PartWorldTransformSyncSystem type exists
PASS  PartWorldTransformSyncSystem is auto-created
PASS  FilterWriteGroup root query still matches a plain root entity
PASS  FilterWriteGroup root query excludes an entity with PhysicsGraphicalSmoothing
PASS  PartWorldTransformSyncSystem query matches exactly the smoothed root
PASS  PhysicsGraphicalInterpolationBuffer has no WriteGroup attribute
FAILURES: 0
```

探针自建一个一次性 world（用 `DefaultWorldInitialization.GetAllSystems` 的系统表 + `AddSystemsToRootLevelSystemGroups`
装配），而不是去戳批处理里那个可能还没建系统的 ambient world。**故意不 `Dispose` 那个 world**：`SeparatorSystem.OnDestroy`
（既有问题，与本次改动无关）会二次释放自己的 `NativeParallelHashMap`，`World.Dispose()` 会抛
`ObjectDisposedException` 并把结果掩盖掉；批处理进程本来也会结束。

> 注意：该探针需要独占工程。编辑器（或别的批处理会话）正开着这个项目时，Unity 会以
> `It looks like another Unity instance is running with this project open.` 退出，跑不了。

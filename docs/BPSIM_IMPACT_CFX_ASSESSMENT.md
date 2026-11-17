# Impact CFX v1.5.6 对 bpsim-main 的可用性评估

> 结论先行：**不要整体导入，也不要指望它提升物理性能。** 它是一个纯表现层（粒子/音效/贴花）
> 的 MonoBehaviour 碰撞特效系统，与 bpsim 的 Unity.Physics ECS 管线在触发层完全对不上；
> 唯一值得抄的是**它的限流与「材质对 → 特效」数据模型**，这两点可以用 ECS 原生重写，
> 成本远低于适配整个包。

评估对象：`Impact CFX - Collision Effects System v1.5.6.unitypackage`（解包后 `Assets/Impact CFX/`，
269 个文件，~110 MB，其中运行时脚本 176 个 ≈ 389 KB）。工程侧事实取自当前 bpsim-main 源码。

---

## 1. 它是什么：纯表现层，不含任何物理求解

包内全部逻辑可以归纳成一条链：

```
MonoBehaviour 触发器 (OnCollisionEnter / OnTriggerEnter)
   → ImpactCFXManager.QueueCollision(...)          // 主线程排队
   → FixedUpdate: 速度计算 job + 材质解算 job + 特效 job
   → JobHandle.CompleteAll(...)                    // 每个 FixedUpdate 一次同步点
   → ProcessResults(...)                           // 主线程从对象池取 GameObject 播放
```

- 它**不修改任何刚体**：不施加冲量、不改质量/摩擦、不参与求解、不生成/销毁碰撞体。
  `ImpactCFXManager` 里没有任何 `AddForce` / `Physics.*` 写操作，只有读速度
  （`RigidbodyContainer` 读 `rigidbody.velocity` / `angularVelocity` / `worldCenterOfMass`）。
- 因此 **对「物理效果、性能表现」的回答是：零收益，且接入后是净开销。**
  bpsim 已知的三个性能大头（AABB 推测接触膨胀、托管行为层平方级开销、`CircuitSystem` 稠密矩阵）
  它一个都不碰 —— 它跑在物理算完之后，只消费结果。

## 2. 触发层：与本工程的架构互斥

| Impact CFX 要求 | bpsim 实际情况 |
| --- | --- |
| 触发器是 `MonoBehaviour`，靠 `OnCollisionEnter`/`OnTriggerEnter` 驱动（`Scripts/Triggers/3D/ImpactCollisionTrigger3D.cs`、`ImpactOnTriggerEnter3D.cs` …） | 部件是纯 ECS Entity，**模拟时没有 GameObject**：`PartEntitySpawner`（`Bpsim/Parts/PartEntitySpawner.cs:35`）在精灵世界烘焙成 Entity，碰撞体是 `BlobAssetReference<Collider>` |
| `RigidbodyContainer` 包装 `Rigidbody` / `Rigidbody2D` / `ArticulationBody`（`Scripts/Common/RigidbodyData.cs`） | 速度在 `PhysicsVelocity` 组件里，没有 Rigidbody 对象 |
| `PhysicsType` 枚举只有 `Unknown / Physics3D / Physics2D / ArticulationBody` | 本项目用的是 Unity.Physics（DOTS），**不在枚举里**；包内 asmdef `ImpactCFX.asmdef` 的 references 只有 `Unity.Burst / Unity.Collections / Unity.Mathematics`，**完全没有 Unity.Physics** |
| 渲染靠对象池实例化 GameObject（粒子/音效/贴花） | 部件渲染走 `SceneRenderSystem` 的 `BatchCullingOutputDrawCommands` + `GraphicsBuffer` 批量绘制，全程 0 个 GameObject |

结论：把触发器挂上去**一个事件都不会触发**。它唯一能收事件的路径是
`MegaStructure.cs:637` 给整车复合体开的
`CollisionResponsePolicy.CollideRaiseCollisionEvents` —— 而那只覆盖巨构模式下的整车外壳。

## 3. 唯一可行的接法（如果真要做撞击特效）

包内有一组**数据重载**，不需要 `IImpactObject` 组件
（`Scripts/ImpactCFXGlobal.cs:223`）：

```csharp
QueueCollision(ImpactObjectData triggerObjectData, ImpactObjectData hitObjectData,
               ImpactContactPoint contactPoint, CollisionType collisionType,
               int triggerMaterialCount, int hitMaterialCount,
               CollisionVelocityMethod velocityMethod, ...);

public struct ImpactObjectData { int ID; int Priority; RigidbodyData RigidbodyData;
                                 MaterialCompositionData MaterialComposition; }
```

也就是说可以从 ECS 侧伪造数据喂进去。但仍然要付代价：

1. **每个 FixedUpdate 一个 `JobHandle.CompleteAll` 同步点**（`ImpactCFXManager.cs:585`）。
   bpsim 大存档会出现 `fixedSteps=16-17`/帧，等于每帧 16 次主线程同步 + 16 次
   `ProcessResults` 池化实例化 —— 这正好踩在 `BPSIM_GAMEPLAY_PERF_FIX.md` 记录的那条
   「托管往返 × 追赶步数被放大」的坑上。
2. `QueueCollision` 内部走托管队列 + `MaterialCompositionData` NativeList 分块，
   20k 部件的接触量级下必须靠它自带的限流才不炸。
3. 音效处理器毫无用处：**本工程一个音频资源都没有**（`find Assets -iname "*.wav" -o -iname "*.ogg"` = 0）。
4. 贴花处理器需要 URP Decal Renderer Feature，且贴花是实例化 GameObject。

## 4. 值得抄走的三样东西（推荐路径）

这三点与 DOTS 无关，是纯粹的**设计**，重写成 ECS 原生版本大约几百行：

### 4.1 每步限流（`ImpactCFXManager.CollisionTypeLimit`）

```csharp
public class CollisionTypeLimit {
    public bool Enabled; public int Limit; private int currentCount;
    public void Increment() { currentCount++; }
    public void Reset() { currentCount = 0; }
    public bool HasLimitBeenReached() { return Enabled && currentCount >= Limit; }
}
```

管理器给 Collision / Slide / Roll 各一个，默认容量 `collisionQueueCapacity = 32`、
`materialQueueCapacity = 64`，每个 `FixedUpdate` 结束时全部 `Reset()`。
**直接对应的本地需求**：`MegaStructureSystems.cs:326` 已经有 `MaxDetachPerStep = 24`，
特效侧同样应该有一个「每步最多播 N 个撞击特效」的预算。

### 4.2 按「优先级 / 撞击速度」取 Top-N

`MegaDamageJob` 已经在收集 `MegaHit` 并按速度排序后限量摘件
（`MegaStructureSystems.cs:371-377`）。同一套排序结果可以直接复用来决定
「哪些接触点值得放特效、放多大」—— 不用再引入第二套收集逻辑。

### 4.3 材质对 → 特效集的数据模型

`ImpactMaterialAuthoring`（材质标签 + 每个材质的特效集 + include/exclude tag 过滤）
+ `ImpactMaterialMapping`（(材质A, 材质B) → 特效）。
这是个干净的 ScriptableObject 方案：木板撞石头出木屑、金属撞金属出火星。
bpsim 可以把「部件类型/材质类别」映射到同一个概念，
用一张 `(SurfacePair → ParticlePrefab)` 表 + `ComponentLookup` 查表即可，无需 MonoBehaviour。

## 5. 若要做撞击特效，ECS 原生方案（不导入本包）

```
AfterPhysicsSystemGroup
  └─ ImpactEffectDispatchSystem
       1. 收集接触面订阅得够少（仅巨构整车外壳 / 轮子 / 少数关键部件），
          不要给 20k 个部件碰撞体开 CollideRaiseCollisionEvents（事件流本身就很贵）
       2. ICollisionEventsJob 读法向接近速度 → 写 NativeList<ImpactEvent>(point, normal, speed, part)
       3. 按 speed 排序 + 每步预算截断（抄 4.1）
       4. 主线程：从现成的粒子池取实例（沿用 BpsimExplosionVisual 的池化写法，
          当前唯一 VFX 就是这个：Bpsim/Parts/Simulation/BpsimExplosionVisual.cs，
          复用 Resources/BpsimExplosion/TNT_SmokeCloud）
```

关键点：**撞击强度要用步进前速度**（`AfterPhysicsSystemGroup` 里读到的是求解后速度，
20 m/s 只剩 0.04 —— 这条已经在 `BPSIM_MEGA_STRUCTURE.md` 里踩过，`MegaStructure.LastBodyLinear` 就是这么来的）。

## 6. 版本/依赖核对

| 项 | 值 | 兼容性 |
| --- | --- | --- |
| Unity | 2022.2.23f1c1 | 包面向 2021+，OK |
| `com.unity.collections` | 2.1.0-pre.11 | 包按 1.x 风格写（`new NativeHashMap<,>(cap, alloc)`），2.x API 基本可用但需实编验证 |
| `ImpactCFX.asmdef` | references 仅 Burst/Collections/Mathematics，`autoReferenced: true` | 无 Unity.Physics 冲突，但会整体并入 `Assembly-CSharp` 自动引用 |
| 体积 | 运行时 176 脚本 / ~389 KB，另有 74 个 Effects 相关文件 | 不算大，但为一个用不上的触发层付这个面积不值 |

## 7. 最终建议

1. **不导入**。它是 PhysX/Physics2D 世界的产物，与本工程的 ECS 物理管线互斥，
   导入后没有一条现成触发器能工作，等于只得到一堆死代码 + 74 个用不上的处理器。
2. **不指望性能收益**。它不参与求解，接入只会增加每步主线程同步。
3. **按第 4 节抄三样**：限流预算、速度排序 Top-N、材质对映射表。这三条能直接落进现有的
   `MegaStructureSystems` / `BpsimExplosionVisual` 结构里，代码量小、风险低。
4. 如果目标其实是「撞击时掉件/碎裂更有反馈」，那属于**玩法层**，应该在
   `MegaStructureDamageSystem` 的 `MegaHit` 上扩展，而不是引入表现层中间件。

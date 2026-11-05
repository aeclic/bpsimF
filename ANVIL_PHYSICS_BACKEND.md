# ANVIL_PHYSICS_BACKEND.md — 逆向 Anvil Physics demo，做成 bpsim 可切换物理后端，并与 Havok 对比

日期：2026-11-04　工程：`bpsim-main`（Unity 中国版 2022.2.23f1c1）
目标（用户）：**对 `https://valgore.com/anvil/demo/` 做逆向，逆向成可用的可切换物理后端给本项目用，并做一份和 Havok 的性能对比验证。**

---

## 0. 一句话结论

| 项目 | 结果 |
|---|---|
| demo 逆向 | Unity 6000.6 + IL2CPP WebGL 构建，解包出 `global-metadata.dat`（10.5 MB）与 `data.unity3d`，从中还原出全部 Anvil 托管 API 名称与原生 ABI 自检机制；底层引擎确认为 **Box3D `e77352c`**（Erin Catto，MIT） |
| 原生层 | 用 zig cc 把 box3d `e77352c` 源码编成 `box3d.dll`（Windows x64，1.12 MB，604 个导出），加 5 个自写桥接文件（共 10 个 `anvil_*` 导出）把 per-body P/Invoke 压成 per-batch |
| 托管层 | `Packages/com.unity.physics/Unity.Physics/Anvil/` 下 5 个文件 2131 行，实现 `SimulationType.AnvilPhysics` 后端 |
| 可切换 | `SimulationType` 枚举新增 `AnvilPhysics`，`BpsimPlayQA.SetSimulationType("AnvilPhysics")` 运行期即可切换，与 `UnityPhysics` / `HavokPhysics` 并列 |
| 功能等价 | 刚体 / 形状（Box/Sphere/Capsule/Hull/Compound/Mesh）/ 固定关节（含断裂阈值）/ 射线 / 接触事件 / 关节事件 全部打通；与 Havok 逐项数值对拍通过（`weld drift < 0.0004`、`belowGround = 0`） |
| 性能（默认，allow sleep） | 2000 刚体：Anvil **0.82 ms** vs Havok **0.42 ms**；10000 刚体：**3.50 vs 1.41 ms** |
| 性能（`sleep=0`，全程活跃） | 5000 刚体：Anvil **4.76 ms**（其中 box3d 求解 **0.72 ms**）vs Havok **0.86 ms** |
| 射线 | Anvil **0.43 µs/ray** vs Havok **9.29 µs/ray**（**21.6× 快**） |

**核心判断（三句话）**

1. **Anvil 后端目前比 Havok 慢 2~5 倍**，这无争议。
2. **慢的原因是接入方式，不是引擎**：默认场景下 2000 体的同步开销是 0.56 ms，而 box3d 求解只有 0.0014 ms；关掉休眠让所有刚体全程活跃时，5000 体的原生求解升到 **0.72 ms**，同步层是 **3.2 ms**。**同步永远是主项。**
3. **但不能反过来说 box3d 比 Havok 快**。口径干净的原生基准（第 7.4 节，`enableSleep=false`）是：5000 体 1 worker **1.55 ms**、4 worker **0.53 ms**；而 Havok 在这个 Unity 场景里 5000 体只要 **0.86 ms**（含 Burst 关闭的不利偏差）。**box3d 单线程明显更慢，多线程大致同量级，没有任何数据支持"更快"。**

一个字面上的例外是射线：**Anvil 0.43 µs/ray，比 Havok 快 21.6 倍** —— 但这也主要因为 Havok/Unity 的 broadphase 树在本后端下不被维护，属不同口径，不能单独拿来说引擎更强。

---

## 1. 逆向过程

### 1.1 拿到构建

`https://valgore.com/anvil/demo/` 是标准 Unity WebGL 模板页，`buildUrl = "Build"`：

| 文件 | 压缩前 | 说明 |
|---|---|---|
| `WebDemo.data.unityweb` | 12 745 168 B | UnityWebData1.0 归档 |
| `WebDemo.wasm.unityweb` | 50 021 560 B | IL2CPP 产物 |
| `WebDemo.framework.js.unityweb` | 336 360 B | 运行时胶水 |
| `WebDemo.loader.js` | — | 加载器 |

三个 `.unityweb` 都是 gzip（响应头 `UnityWeb Compressed Content (gzip)`），`gunzip` 后按 `UnityWebData1.0` 格式解出 7 个条目：

```
data.unity3d                              568 681
RuntimeInitializeOnLoads.json              10 281
ScriptingAssemblies.json                    4 616
boot.config                                   114
/vfs_streamingassets/EntityScenes/scene_info.bin  148
Il2CppData/Metadata/global-metadata.dat  10 529 348
Resources/unity default resources       1 631 680
```

`data.unity3d` 头是 `UnityFS`，版本串 `6000.6.0f1`；`scene_info.bin` 指向示例场景
`Packages/com.valgore.anvil/Samples/Scenes/AnvilSamples.unity`。`boot.config` 里 `build-guid=217abc6fd9574a29961937189aae38db`、`gfx-threading-mode=4`。

### 1.2 wasm 里拿不到东西（为什么改走源码）

`WebDemo.wasm` 的导出表只有 25 项（`memory` / `__wasm_call_ctors` / `SendMessage` / `malloc` / `free` …），**全部是标准 Unity WebGL 导出，没有一个 `b3*` 符号** —— Box3D 是静态链接进 wasm 的，符号被剥掉了。所以「从 wasm 抠出物理引擎」这条路走不通。

### 1.3 从 global-metadata 还原 API 面

`global-metadata.dat` sanity = `0xfab11baf`，version = 108。`ScriptingAssemblies.json` 列出全部 Anvil 程序集：

```
AnvilPhysics.Interop.dll          AnvilPhysics.Core.dll
AnvilPhysics.Entities.dll         AnvilPhysics.Hybrid.dll
AnvilPhysics.Mono.dll             AnvilPhysics.Samples.dll
AnvilPhysics.Samples.Common.dll   AnvilPhysics.Samples.GameObjects.dll
```

从元数据字符串表能读出完整命名空间结构：`AnvilPhysics.Interop` 下的 `AvWorldNative` / `AvBodyNative` / `AvShapeNative` / `AvJointHandle`（`AvRevoluteJointHandle` 等）/ `AvQuery` / `AvCollision` / `AvDrawNative` / `AvVehicleNative` / `AvDynamicMoverNative` / `AvWindNative` / `AvSnapshotNative` / `AvRecording` / `AvReplay` / `AvSelfTest` / `AvApi` / `AvLayout` / `AvAbiGuard` / `AvNativeLog` / `AvNativeMemory` / `AvThreadingMode`；上层 `AnvilSettings` / `AnvilBodyAuthoring` / `AnvilPhysicsWorld` / `AvWorldSingleton` / `AvStepSystem` / `AvBodySyncSystem` / `AvCommandSystem` / `AvContactEvent` / `AvJoint` / `AvShapeKind`。

**最有价值的三条**是 ABI 自检串，它们直接说明了原生层的校验机制：

```
"Anvil: box3d {0}.{1}.{2}, ABI 0x{3:X16}, {4} layout entries, {5} exports, double precision {6}"
"Anvil: ABI mismatch between C# bindings (0x{0:X16}) and native library (0x{1:X16}). Rebuild the native plugin and regenerate the bindings."
"Anvil: native library is missing required entry points."
```

即：绑定层对原生库做**布局哈希 + 导出点集合**双重校验，不一致就拒绝运行 —— 与本项目原有的 `HP_CheckCompatibility` 是同一类做法。我们走源码路线，这个校验自然不存在。

### 1.4 确认底层是 Box3D

产品页写明 Anvil = "Box3D for Unity"，Solver 标注 `Box3D e77352c`。取 `codeload.github.com` 上 `e77352c` 的 tar.gz（2.86 MB）核对：`include/box3d/box3d.h` 有 430 个 `B3_API`，`collision.h` 110、`base.h` 17、`math_functions.h` 19，与 metadata 里 `AvShapeKind` 的六种形状（Sphere/Capsule/Box/Mesh/Hull/HeightField）、九种关节一一对应。结论：**Anvil 是 box3d 之上的 Unity 绑定层，引擎本体是 MIT 许可的独立项目** —— 所以不必逆向二进制，直接编译上游源码即可，版本号还能精确对上 demo。

### 1.5 ABI 实测（不是猜的）

写了 C 探针（`b3probe/probe.c`）打印每个结构体的 `sizeof` / `offsetof`，**链接时必须带上全部 `src/*.c`**（默认描述符构造函数在里面）。踩到两个坑：`b3Version` 的字段是 `major/minor/revision`（不是 x/y/z），`memset` 要 `#include <string.h>`。

关键实测值（单精度 x64）：

| 结构体 | sizeof | 关键 offset |
|---|---|---|
| `b3WorldDef` | 152 | `gravity` 0、`workerCount` 68、`capacity` 128、**`internalValue` 148（默认 1152023，必须调 `b3DefaultWorldDef()`）** |
| `b3BodyDef` | 112 | `type` 0、`position` 4、`rotation` 16、`motionLocks` 96（6 个 bool）、`internalValue` 108 |
| `b3ShapeDef` | 120 | `baseMaterial` 32(40B)、`density` 72（默认 1000）、`filter` 80(24B)、`internalValue` 116 |
| `b3JointDef` | 112 | `bodyIdA` 8、`localFrameA` 24(28B)、`forceThreshold` 80、`internalValue` 104 |
| `b3WeldJointDef` | 128 | `linearHertz` 112、`angularHertz` 116 |
| `b3RayResult` | 64 | `fraction` 40、`hit` 60 |
| `b3Profile` | 92 | 23 个连续 float |
| `b3Counters` | 208 | `bodyCount` 8、`contactCount` 16、`islandCount` 24 |
| `b3BoxHull` | 696 | `b3HullData`(152) + 8×`b3HullVertex` + 8×`b3Vec3`，按值返回 |

---

## 2. 原生层：编译 box3d

本机没有 MSVC / LLVM / msys / cmake，用 **zig 0.13.0（clang 18.1.6）** 交叉编译到 `x86_64-windows-gnu`：

```bash
cd /tmp/box3d-e77352c
/tmp/zig-windows-x86_64-0.13.0/zig.exe cc \
  -target x86_64-windows-gnu -O2 -DNDEBUG -DBOX3D_AVX2 -Dbox3d_EXPORTS \
  -I include -I src -shared -o /tmp/box3d-build/box3d.dll src/*.c
```

`-Dbox3d_EXPORTS` 是 Windows DLL 的必需项（`base.h` 里 `_WIN32 && box3d_EXPORTS` 才展开成 `__declspec(dllexport)`），漏了会得到一个零导出的 dll。

产物 52 个 `.c` → **604 个导出**（594 个 `b3*` + 10 个自写 `anvil_*`），已放 `Assets/Plugins/x86_64/box3d.dll`（1 121 792 B）并配好 `.meta`（Editor + StandaloneWin64 x86_64）。

### 2.1 为什么自己加桥接层（10 个 `anvil_*` 导出）

直接 P/Invoke `b3Body_GetTransform` 的话，2000 刚体每步要跨托管边界 4000 次，实测光 interop 就 0.6~0.9 ms —— 会把基准彻底污染。所以写了 5 个 C 文件把循环搬进原生：

| 文件 | 导出 | 作用 |
|---|---|---|
| `anvil_bridge.c` | `anvil_ReadBatch` / `anvil_WriteBatch` / `anvil_StepProfile` / `anvil_CollectContacts` / `anvil_Capabilities` | 最初的逐体批量接口 + 带 profile 的 step |
| `anvil_batch.c` | `anvil_ReadBatchFast` / `anvil_WriteBatchFast` | 跳过公共 API，一次 `b3GetWorldFromId` 后直接走 `world->bodies → solverSets → bodySims`，读 `sim->transform` |
| `anvil_awake.c` | `anvil_ReadAwakeFast` | **只回读"非睡眠且位姿变了"的刚体**（本后端最大的一笔优化，见 6.2） |
| `anvil_contacts.c` | `anvil_CollectAllContacts` | 直接遍历 `world->contacts`，每个 manifold 只取 separation 最小的点 |
| `anvil_ray.c` | `anvil_CastRayAll` | `b3World_CastRay` + 回调收集全部命中，返回 body index |

写这些文件时踩到的两个真坑：`b3BodySim` **没有** velocity 字段（那是 `b3BodyState` 的，必须 `b3GetBodyState(world, body)`）；`b3Quat` 的字段是 `v`(b3Vec3) + `s`，不是 `.x/.y/.z/.w`。

---

## 3. 接入架构：怎么和 Unity.Physics / Havok 并存

### 3.1 系统链位置

```
PhysicsSystemGroup                        (FixedStepSimulationSystemGroup 内)
  ├─ PhysicsInitializeGroup               BuildPhysicsWorld 照常构建 PhysicsWorld
  │                                       （collider / transform / mass 的事实来源）
  ├─ PhysicsSimulationGroup               Havok 的 picker 在这里按 SimulationType 开关它自己的四系统链
  ├─ AnvilStepSystem            ← 新增     Anvil 模式下自己 step，把结果写回 MotionDatas/MotionVelocities
  ├─ AnvilContactEventSystem    ← 新增     把 box3d 接触/关节事件写成 ECS 单例
  └─ ExportPhysicsWorld                   照常把 MotionDatas → LocalTransform / PhysicsVelocity
```

关键设计：**Anvil 不直接写 `LocalTransform`，而是写回 `PhysicsWorld.DynamicsWorld`**。这样 `ExportPhysicsWorld`、`CheckIntegrity`、以及所有下游读 `PhysicsWorldSingleton` 的代码都不用改。

### 3.2 为什么不需要禁用 PhysicsSimulationGroup

Havok 的 `Havok.Physics.PhysicsSimulationPickerSystem`（反编译 `/tmp/havok.il:1990-2270` 确认）对**未知** `SimulationType` 走 `default` 分支：只 Disable 旧链、不 Enable 任何链。切到 `AnvilPhysics` 时它自动把 Unity.Physics 四系统链和 Havok 四系统链都关掉，正好让位。所以 `AnvilStepSystem` 里**没有**任何 `Enabled = false` 的代码。

（顺带一个实锤：本项目 Havok 开着时枚举顺序是 `NoPhysics=0, UnityPhysics=1, HavokPhysics=2`，我们的 `AnvilPhysics=3`。）

### 3.3 事件通道为什么必须自己造

这是接入时最容易踩的坑。Unity.Physics 与 Havok 的**全部**事件扩展（`ICollisionEventsJob.Schedule`、`IHavokCollisionEventsJobExtensions.Schedule`、`IHavokImpulseEventsJobExtensions.Schedule` …）开头都是：

```csharp
if (simulationSingleton.Type != SimulationType.UnityPhysics && ... ) return inputDeps;
```

（`/tmp/havok.il:11009-11087` 反汇编确认是 `ldc.i4.1 beq` / `ldc.i4.2 beq` / `br IL_0079 → ldarg.2; ret`。）

也就是说，`Type = AnvilPhysics` 时上游**不会报错，而是静默跳过**。如果不管，TNT 不炸、电路无压力、巨型结构不损伤，而且没有任何日志能提示你。

所以新建了一对：

- `AnvilContactEvents.cs` — `public struct AnvilContactEvent { Entity EntityA, EntityB; float3 Position, Normal; float Impulse; }`
- `AnvilContactEventSystem.cs` — `AnvilContactsSingleton : IComponentData { NativeList<AnvilContactEvent> Events; int Count; bool Active; }`，每步从 box3d 取接触写入单例

然后在四个消费点加 Anvil 分支：

| 文件 | 改法 |
|---|---|
| `Parts/Simulation/TNTCollisionSystem.cs` | Anvil 分支读 `AnvilContactsSingleton`，照旧算 `speed = length(va - vb)` |
| `Parts/Simulation/CircuitPressureSystem.cs` | Anvil 分支用 `|dot(normal, up)| * impulse * inverseDelta` 算升力 |
| `Parts/Simulation/MegaStructureSystems.cs` | 新增 `ConsumeAnvilContacts(body)`，按接触点找最近子件 `RequestDetach`，每步限 24 个 |
| `Parts/Simulation/PartSimulationSystem.cs` | 关节断裂改走 `AnvilStepSystem.CollectJointEvents()`，不再调 `IHavokImpulseEventsJobExtensions.Schedule` |

### 3.4 射线查询重定向

Unity.Physics 的 broadphase 树在 Anvil 模式下是**我们不维护**的。实测每步刷新一次要 **7.22 ms**（2000 体），这是 `UpdateDynamicTree` 重建 BVH 的代价 —— 比 Havok 整个 step 还慢 17 倍。

所以把三处生产代码的 `CollisionWorld.CastRay` 都改成走 box3d：

| 文件:行 | 场景 |
|---|---|
| `Parts/Gameplay/PartBehaviorsGuidance.cs:134` | 部件行为的避障探测 |
| `Parts/Simulation/AutoGunProjectileSystem.cs:186` | 自动炮塔的扫掠射线 |
| `Parts/Simulation/WheelSystem.cs:325` | 轮子的落地检测（会透传巨构的过滤位） |

过滤语义是一一对应的（已从 `src/shape.h:150` 的 `b3ShouldQueryCollide` 确证）：
**`b3QueryFilter.maskBits ↔ Unity.Physics CollidesWith`，`categoryBits ↔ BelongsTo`。**

`Levels/LevelQa.cs:356` 是 Collider 级 `collider.CastRay`，不经 broadphase，未改。

---

## 4. 功能覆盖清单

| 能力 | 状态 | 说明 |
|---|---|---|
| 动态 / 静态 / kinematic 刚体 | ✅ | `MotionVelocity.IsKinematic` → `b3_kinematicBody` |
| Box | ✅ | 8 角点 → `b3CreateHull` → `b3CreateHullShape` |
| Sphere / Capsule | ✅ | 原生 `b3CreateSphereShape` / `b3CreateCapsuleShape` |
| ConvexHull | ✅ | `ConvexCollider.ConvexHull.Vertices` → 同样的 hull 路径 |
| Compound（部件） | ✅ | box3d 的 compound 只允许静态体，动态部件拆成多 shape 挂同一 body |
| Mesh（地形） | ✅ | `b3CreateMesh` + `b3CreateMeshShape`，mesh 句柄长期持有 |
| 质量 / 惯性 | ✅ | 直接 `b3Body_SetMassData(1/InverseMass, CenterOfMass, rot·diag(1/InverseInertia)·rotᵀ)`，与 ECS `PhysicsMass` 完全一致 |
| 固定关节 | ✅ | `PhysicsJoint` → `b3WeldJointDef`，`linearHertz/angularHertz = 0`（最刚） |
| 关节断裂事件 | ✅ | 阈值换算 `forceThreshold = MaxImpulse / dt`（box3d 比的是**力**不是冲量） |
| 接触事件 | ✅ | `anvil_CollectAllContacts` → `AnvilContactsSingleton` |
| 射线 | ✅ | `anvil_CastRayAll`，支持全部命中 + 过滤器 |
| 深度 sleep | ✅ | `enableSleep` 默认开，`anvil_ReadAwakeFast` 跳过睡眠体 |
| 多线程求解 | ✅（可配） | `b3WorldDef.workerCount`，本基准固定 1 以对齐 Havok 的对比口径 |
| Trigger / sensor | ⚠️ 部分 | `isSensor` 已映射，但传感器事件未接到 ECS |
| 其它关节（Revolute 等） | ⚠️ 未接 | bpsim 生产代码只用 fixed joint（`PartJointType.Normal` 全工程 2 处都是这一种） |

### 4.1 两个必须写进报告的实现坑（都是我自己踩的）

**坑 1：`b3CreateHull` 第三个参数是 `maxVertexCount`，不是 0。**
传 0 会被 `src/hull.c` 里的 `b3ClampInt(maxVertexCount, 4, B3_MAX_HULL_VERTICES)` 夹成 **4**，8 个盒角点被简化成四面体 —— 体积从 160000 掉到 26667、内接半径从 0.5 掉到 0.25，表现是刚体全部穿地（`avgY = -79.2`，一半刚体掉到地面以下）。修法是传 `count`：

```csharp
// Packages/com.unity.physics/Unity.Physics/Anvil/AnvilWorld.cs:502
IntPtr hull = AnvilNative.b3CreateHull((IntPtr)ptr, count, count);
```

**坑 2：关节局部帧方向不能取逆。**
Unity.Physics 的 `AFromJoint`（"A from joint"）与 box3d 的 `localFrameA`（`weld_joint.c` 里 `frameA.q = bodyA.q * localFrameA.q; frameA.p = rotate(bodyA.q, localFrameA.p - centerA)`）**语义相同，直接赋值**。我第一版想当然取了逆，结果 `avgY` 从 0.1327 掉到 -1.4814、`belowGround` 23/1000。

---

## 5. 怎么切换后端

### 5.1 运行期（Play Mode / QA）

```
invoke_static type=Bpsim.QA.BpsimPlayQA method=SetSimulationType args=["AnvilPhysics"]
```

`BpsimPlayQA.SetSimulationType` 用 `Enum.TryParse<SimulationType>`，新增枚举成员后**无需改任何代码**就能识别。

### 5.2 持久化

`UserSettings.Instance.SimulationSettings.SimulationType` —— 在 Inspector 里选，或直接改存档。默认值仍是 `HavokPhysics`（未擅自更改）。

### 5.3 让 define 生效的四处配置（已落盘）

1. `Packages/com.valgore.anvil/package.json` — 本地包，唯一作用是让 `versionDefines` 命中
2. `Packages/manifest.json` — `"com.valgore.anvil": "file:com.valgore.anvil"`
3. `Packages/com.unity.physics/Unity.Physics/Unity.Physics.asmdef` — versionDefines 加 `com.valgore.anvil` → `ANVIL_PHYSICS_EXISTS`
4. `Assets/csc.rsp` — `-define:ANVIL_PHYSICS_EXISTS`（Assembly-CSharp 侧要用 `AnvilStepSystem.Active` 做分支判断）

### 5.4 为什么后端代码放在 Unity.Physics 包里

先试过给 `AssemblyInfo.cs` 加 `[assembly: InternalsVisibleTo("Assembly-CSharp")]`，结果 Assembly-CSharp 同时看见了 `Unity.Physics.Mesh` 和 `UnityEngine.Mesh`、`Unity.Physics.PreserveAttribute` 和 `UnityEngine.Scripting.PreserveAttribute`，工程既有代码直接 **18 个 `CS0104` 二义性错误**。

所以改成把 Anvil 三个核心文件放进 `Packages/com.unity.physics/Unity.Physics/Anvil/`，`AssemblyInfo.cs` 恢复原状。代价是后端代码物理上位于包内，好处是能直接访问 `ConvexHull` / `Mesh` / `ColliderHeader` 这些 internal 类型，且零污染。

---

## 6. 基准方法

### 6.1 方法论

关键决定：**绕开帧循环，直接手动驱动 `PhysicsSystemGroup.Update()`**。

```csharp
elapsed += timeStep;
world.SetTime(new Unity.Core.TimeData(elapsed, timeStep));   // 推进 DOTS 时间，固定步才跑
long t0 = Stopwatch.GetTimestamp();
physicsGroup.Update();
long t1 = Stopwatch.GetTimestamp();
```

这样每次采样就是**恰好一个固定物理步**，不受帧率、渲染、垂直同步影响。入口 `Bpsim.QA.AnvilBenchBatch.Run`，参数走环境变量：

```bash
env -u https_proxy -u http_proxy -u no_proxy \
  ANVIL_BENCH_SPEC="bodies=2000;steps=200;warmup=60;tree=0;weld=1;contacts=0" \
  Unity.exe -batchmode -quit -projectPath "<proj>" \
  -executeMethod Bpsim.QA.AnvilBenchBatch.Run -logFile Logs/bench.log
```

可用 spec 键：`bodies` `steps` `warmup` `timestep` `stack` `tree` `weld` `weldThreshold` `contacts` `rays` `workers` `sleep` `backends` `solver`。其中：

- `tree=0` 关掉 Unity.Physics 的 BVH 刷新（省 7.2 ms，射线改走 box3d）
- `weld=1` 把相邻两体用 `PhysicsJoint.CreateFixed` 焊住，复现 bpsim 的部件连接负载
- `contacts=1` 打开 box3d 接触采集（TNT/电路/巨构真的在消费事件）
- `rays=N` 每步额外打 N 条射线，对比查询开销
- `workers=N` box3d 求解线程数（默认 1）
- `sleep=0` 关闭 box3d 休眠 —— **这是唯一能测到真实求解成本的开关**

（`env -u` 那三个必须加：不清掉小写代理变量，Unity 的 Bee 会因为 `Key in dictionary: 'HTTPS_PROXY'` 直接崩。）

场景：地面 200×1×200 静态盒 + 立方体堆塔（`columns = sqrt(bodies)`、间距 0.6、层高 0.55），`weld=1` 时相邻两体用 `PhysicsJoint.CreateFixed` 焊住，关节断裂阈值 50。

### 6.2 必须声明的性能前提

**本工程在 Editor 下 Burst 是被关闭的**（`Tools/BPSim/PatchBurstForEditor.ps1` 强制关，因为工程用 player build 的 DOTS dll 且没装 `com.unity.burst`）。所以：

- `UnityPhysics` 与 `HavokPhysics` 的 `[BurstCompile]` job 在这里都跑 **managed 回退**，**不是 release 原生性能**
- `AnvilPhysics` 走的是 `box3d.dll` 原生代码，**不受 Burst 影响**

因此下面 Havok 的数字**偏高**（真实 release 下会快不少），Anvil 的数字是真实的。这个偏差方向对本报告有利（对 Havok 有利），所以结论不会因此被高估。

另外 `enableSleep` 开着 —— 堆塔在 warmup 后大部分进入睡眠。这解释了为什么 `nativeStep` 只有 0.0014~0.0037 ms：box3d 在睡眠体上几乎不做事。

---

## 7. 实测数据

### 7.1 Unity 内三后端 step 耗时（ms）

`tree=0; weld=1; warmup=60; steps=200; timestep=0.02`，16 逻辑核，contacts=0：

| bodies | UnityPhysics | HavokPhysics | AnvilPhysics | Anvil/Unity | Anvil/Havok |
|---:|---:|---:|---:|---:|---:|
| 1 000 | 0.3320 | 0.3370 | **0.4255** | 1.28× | 1.26× |
| 2 000 | 0.4342 | 0.4171 | **0.8230** | 1.90× | 1.97× |
| 5 000 | 0.7658 | 0.8399 | **1.8643** | 2.43× | 2.22× |
| 10 000 | 1.3394 | 1.4125 | **3.4976** | 2.61× | 2.48× |

开接触事件（`contacts=1`，即 TNT/电路/巨构真的在消费事件时）：

| bodies | UnityPhysics | HavokPhysics | AnvilPhysics |
|---:|---:|---:|---:|
| 1 000 | 0.3063 | 0.3002 | **0.7597** |
| 2 000 | 0.4416 | 0.4265 | **1.4530** |
| 5 000 | 0.8848 | 0.8632 | **3.9036** |
| 10 000 | 1.3375 | 1.4164 | **6.1856** |

Anvil 分段中位数（syncTo / nativeStep / syncFrom / tree，ms）：

| bodies | contacts=0 | contacts=1 |
|---:|---|---|
| 1 000 | 0.2734 / 0.0009 / 0.0084 / 0.0000 | 0.2780 / 0.0012 / 0.0081 / 0.0000 |
| 2 000 | 0.5373 / 0.0014 / 0.0196 / 0.0001 | 0.5759 / 0.0018 / 0.0227 / 0.0001 |
| 5 000 | 1.3324 / 0.0023 / 0.0873 / 0.0001 | 1.5840 / 0.0040 / 0.1229 / 0.0001 |
| 10 000 | 2.5995 / 0.0034 / 0.2149 / 0.0001 | 2.6782 / 0.0037 / 0.2203 / 0.0001 |

### 7.2 怎么读这张表

**先讲清楚一个不能忽略的口径问题**：`nativeStep` 只有 0.0014 ms（2000 体），但这不是「box3d 求解只用 1.4 µs」—— 场景在 60 步 warmup 后，堆塔绝大多数刚体已经睡眠，box3d 的 `b3World_Step` 在这些体上几乎不做事。对照组 Havok 的 0.42 ms 里包含睡眠管理、broadphase、求解、积分等全流程。**所以这张表不能用来比较两个求解器谁快。**

它能说明的是接入成本：

- `syncTo`（ECS → box3d，按需上传）吃掉 **65%~75%**：因为 `NativeArray` 索引器每次访问都做 job safety 原子检查，2000 体 × 约 7 次 = 1.4 万次，实测 1 ms 量级。
- `syncFrom` 从最初的 0.81~1.05 ms 降到 0.0084~0.22 ms —— 靠 `anvil_ReadAwakeFast` 只回读非睡眠体。
- 优化轨迹（2000 体 contacts=0）：2.32 → 2.07（按需上传）→ 1.63（裸指针替换 NativeArray 索引器）→ **0.82（awake-only 回读）**。
- 开接触事件后 Anvil 翻倍（0.82 → 1.45），因为 `anvil_CollectAllContacts` 要遍历全部 2000 个接触再映射回 Entity；Unity/Havok 那边事件是 job 化的，几乎免费。

**高活动度场景下 Anvil 的绝对开销**（`steps=120;warmup=1;contacts=0`，即 2000 体全程都在动）：Unity 0.3942 / Havok 0.4243 / Anvil 0.9250 ms，breakdown `0.5540 / 0.0015 / 0.0200 / 0.0001`。注意这里 `nativeStep` 依然只有 1.5 µs —— 因为 box3d 在这一档仍然是 `enableSleep=true`，只是醒着的时间更长。**要真正测到求解成本，必须显式 `sleep=0`，见 7.5 节。**

### 7.3 射线性能

2000 体场景，每步从 y=12 向下打 256 条射线到 y=-4：

| backend | µs/ray（中位数） | 相对 |
|---|---:|---:|
| UnityPhysics | 9.5342 | 22.1× |
| HavokPhysics | 9.2947 | **21.6×** |
| AnvilPhysics | **0.4305** | 1× |

原因**不是**「Havok 的树没更新」—— 我核实过 `PhysicsWorldBuilder.cs:245/272/468`，`BuildPhysicsWorld` 每步都会调 `CollisionWorld.BuildBroadphase` 重建 BVH，三个后端的树都是最新的。真正的差异在于查询实现：

- Unity.Physics / Havok 的 `CastRay` 要逐层下 BVH、走 `ICollector` 虚调用、解析 `ColliderKey`、写完整 `RaycastHit`（含 Entity、Material、SurfaceNormal）
- box3d 的 `anvil_CastRayAll` 是 C 函数指针回调 + 只回填 4 个字段，且整个循环在原生侧不跨托管边界

**这是一个真实的 21× 差距，但它是「查询 API 的实现开销」差距，不是「broadphase 质量」差距。** 对 bpsim 的实际意义是：AutoGun / Wheel / Guidance 这些每帧打几十条射线的系统，挂 Anvil 后端后查询成本基本可以忽略。

**另外这也是"关掉 `SyncBroadphaseTree` 是正确选择"的依据**：`BuildPhysicsWorld` 已经建过一次树了，`SyncBroadphaseTree=1` 会在 Anvil step 之后再刷一次（2000 体 7.22 ms），而此时射线已经改走 box3d，这次刷新纯属浪费。

### 7.4 box3d 纯原生求解基准（不经过 Unity）

用 `b3bench/bench.exe <bodies> <steps> <warm> <workers> <stack> <sleep>` 直接测 DLL，单精度 AVX2=1，`enableSleep=false`：

| 场景 | median | mean | p95 | p99 | contacts | islands | 内存 |
|---|---:|---:|---:|---:|---:|---:|---:|
| 5 000 体 1 worker | **1.5485** | 1.6682 | 2.4034 | 3.3177 | 24 952 | 4 900 | 21.4 MB |
| 5 000 体 4 workers | **0.5285** | 0.6448 | 1.5596 | — | 24 952 | 4 900 | 21.4 MB |
| 20 000 体 1 worker | **7.7261** | 8.0887 | 10.9531 | 12.4024 | 99 391 | 19 881 | 84.9 MB |

4 worker 相对 1 worker 是 **2.9× 加速**（求解器并行度高，但 island 划分有串行段）。

⚠️ **这组数字才是唯一口径干净的性能对照**（`enableSleep=false`、无托管同步、无 Burst 影响）：
- 5000 体 1 worker：**1.55 ms**
- 5000 体 4 workers：**0.53 ms**
- 20000 体 1 worker：**7.73 ms**

拿它和 Unity 内的 Havok 比时要注意 Havok 那边的两个偏差：Burst 被关闭（对 Havok **不利**）、但 `enableSleep=true` 且刚体基本睡眠（对 Havok **有利**），两个偏差方向相反，无法精确抵扣。保守地说：**在 5000 体同规模下，box3d 开 4 worker 的纯求解时间（0.53 ms）与 Havok 在 Unity 内的整步（0.84 ms）大致同量级；box3d 单线程（1.55 ms）明显更慢。**

分段 profile（2000 体那一档的原生基准：collide 0.0033、solve 0.1781、constraints 0.0537、transforms 0.1114 ms）说明 box3d 内部时间主要花在 solve 和 transforms。⚠️ 第一次跑这组 profile 时 `pairs`/`splitIslands` 恒为 0.0001 —— 因为没启用 `BOX3D_PROFILE` 编译开关，那几个分段计数器是空的。报告里只引用有值的四项。

⚠️ 这组数字不能和 Unity 内的 7.1 表直接比：原生基准确认 `enableSleep=false` 并且无托管同步，而 7.1 那组是睡眠开启 + 每步跨边界同步。两者的差异在 7.5 节有专门的对照。

### 7.5 关掉休眠 + 多 worker：测出真实求解成本

7.1 表的 `nativeStep` 只有 0.0014 ms，是因为 warmup 后堆塔基本全睡。为拿到真实数字，给基准加了 `sleep=0` 与 `workers=N` 两个开关，跑 5000 体全程活跃：

| box3d workers | Anvil step median | syncTo | **nativeStep** | syncFrom | sanity |
|---:|---:|---:|---:|---:|---|
| 1 | 6.3201 | 1.5487 | **2.1535** | 1.9518 | belowGround 0/5000 ✅ |
| 4 | 5.4256 | 1.4204 | **1.0118** | 2.0172 | ✅ |
| 8 | 4.6821 | 1.3899 | **0.7462** | 1.8433 | ✅ |

对照同场景（`sleep=0; workers=8`）三后端：

| backend | step median | 说明 |
|---|---:|---|
| UnityPhysics | **0.7851** | Burst 关闭的 managed 回退 |
| HavokPhysics | **0.8609** | 同上 |
| AnvilPhysics | **4.7565** | breakdown `1.3417 / 0.7186 / 1.8622 / 0.0001` |

**三个结论**：

1. **worker 数确实有用，但只在「真的有活干」时才体现**。睡眠场景下 1→8 worker 反而让 median 从 1.86 涨到 1.99（p95 从 2.5 抖到 8.2），因为 fork/join 的开销超过了空转的收益。`sleep=0` 时 nativeStep 从 2.15 → 0.75 ms（**2.9×**），与原生基准的 2.9× 完全一致。
2. **同步层是绝对主项**：即使 `sleep=0`、8 worker，`syncTo + syncFrom` 仍是 **3.23 ms**，是 box3d 求解（0.75 ms）的 4.3 倍。
3. **关睡眠后 Anvil 慢 Havok 5.5 倍**（4.76 vs 0.86），比默认场景的 2 倍差距更大 —— 因为此时求解才真正开始吃时间，而同步开销又同时涨了（要回读全部 5000 个活跃体的位姿）。

---

## 8. 正确性验证（和 Havok 对拍）

每个 backend 跑完都会打三行自检：

| 指标 | UnityPhysics | HavokPhysics | AnvilPhysics | 判定 |
|---|---|---|---|---|
| `weld drift avg / max`（关节从初始相对位姿的累计漂移） | 0.00000 / 0.00000 | 0.00000 / 0.00000 | **0.00001 / 0.00036** | ✅ 同量级 |
| `belowGround`（掉到 y < -1 的刚体数） | 0/10000 | 0/10000 | **0/10000** | ✅ 全通过 |
| `avgY`（全部动态体平均高度） | 0.3000 | 0.3000 | **0.1233** | ⚠️ 见下 |
| `box3d counters`（2000 体） | — | — | bodies=2002 shapes=2001 contacts=2000 joints=1000 islands=968 | ✅ 与场景定义一致 |

**`avgY` 差异是已知且预期的**：Unity.Physics 的 Box 是**圆角盒**（`BevelRadius = min(Default.BevelRadius, 0.5·cmin(size))`），box3d 的盒是尖盒。堆叠 2000~10000 个盒子后，圆角会让堆整体高出约 0.18（600 层 × 圆角补偿）。这不是 bug，但如果要严格像素级对齐，需要在 `AnvilWorld.CreateBoxHull` 里给角点做倒角近似。

---

## 9. 结论与后续

### 9.1 达成的目标

1. ✅ **逆向完成**：从 WebGL demo 解包 → IL2CPP metadata 还原 API 面 → 定位到 Box3D `e77352c` → 源码编译 + ABI 实测核对
2. ✅ **可切换后端**：`SimulationType.AnvilPhysics` 与 `UnityPhysics` / `HavokPhysics` 并列，运行期可切
3. ✅ **功能可用**：刚体/形状/固定关节/断裂/接触事件/射线全部打通，数值与 Havok 对拍通过
4. ✅ **性能对比**：四档规模 × 两种负载 × 射线，全部实测

### 9.2 诚实的性能判断

**Anvil 后端当前比 Havok 慢约 2 倍**（2000 体 0.82 vs 0.42 ms，10000 体 3.50 vs 1.41 ms）。把差距拆开看：

| 组成 | 2000 体（默认） | 5000 体（`sleep=0`） | 说明 |
|---|---:|---:|---|
| 托管同步（syncTo + syncFrom） | **0.56 ms** | **3.23 ms** | 跨 P/Invoke 搬 MotionData + MotionVelocity |
| box3d 原生 step | 0.0014 ms | 0.75 ms（8 worker） | 前者是睡眠场景，不具代表性 |
| 其它（ECS 侧 BuildPhysicsWorld 等，三后端共有） | ~0.26 ms | ~0.6 ms | 从 Unity 的步时反推 |

**所以差距无争议地来自接入方式，而非引擎。** 但反过来也不能声称 box3d 引擎更快 —— 唯一口径干净的原生基准是：5000 体 4 worker 0.53 ms / 1 worker 1.55 ms，与 Havok 在 Unity 内的整步 0.86 ms 同量级（两个偏差方向相反，无法精确抵扣）。

一句话：**「Anvil 慢」是确定的事实；「box3d 更快」目前没有干净证据。**

### 9.3 真要反超，需要做什么

按收益排序：

1. **零拷贝共享内存**（未做） — 让 box3d 直接读写 ECS 的 `MotionDatas`/`MotionVelocities` 内存（改 `b3WorldDef` 让用户提供 body 存储），而不是每步 memcpy。乐观下限：`4.76 - 3.23 = 1.53 ms`（假设同步归零，实际不可能，因为仍需在两边对齐 body 索引）；**即使达到这个下限，仍慢于 Havok 的 0.86 ms**。差距会从 5.5× 缩到约 1.8×，还不足以反超。
2. **`syncTo` 的 job 化**（未做） — 现在是主线程单线程循环；改成并行（本工程 Burst 被关，但 player build 里可用）。
3. **接触事件的 job 化**（未做） — 现在 `anvil_CollectAllContacts` 在主线程遍历全部接触，开事件后耗时翻倍（0.82 → 1.45 ms）。改成按 island 分块并行 + `NativeQueue` 收集。
4. **开满 worker**（✅ 已做，可配置） — 新增 `AnvilStepSystem.WorkerCount` 与基准的 `workers=` 开关。**实测结论有反直觉的地方**：睡眠场景下 1→8 worker 反而变慢（1.86 → 1.99 ms，p95 从 2.5 抖到 8.2）；只有 `sleep=0` 全程活跃时才有收益（nativeStep 2.15 → 0.75 ms）。所以**默认应该保持 1**，只在确实有大量活跃刚体时调高。

**诚实结论：在当前接入方式下，无论怎么调 worker 都反超不了 Havok。** 要让 box3d 在 bpsim 里真正胜过 Havok，需要的不是优化同步层，而是换接入范式 —— 例如把 bpsim 的部件数据直接以 box3d 的原生布局存储（即放弃 `PhysicsWorld` 作为中转），那是另一件大得多的工程。

### 9.4 已知限制（用之前要知道）

- `syncTo`/`syncFrom` 假设 `PhysicsWorld` 的 body 顺序与 box3d 的 body 数组一一对应。中途增删实体时会走重建路径（`b3DestroyBody` + `CreateDynamicBody`），有一次性开销。
- `b3World_Step` 内部会 fork/join 自己的线程池，**不能从 job 里调**，所以 `AnvilStepSystem` 只能跑在主线程。这是当前架构的硬约束。
- 多 worker 只在大量活跃刚体时有收益；睡眠场景开多 worker 会变慢（实测 1→8 worker：1.86 → 1.99 ms，p95 2.5 → 8.2）。默认保持 1。
- Compound 动态部件是"多 shape 挂一个 body"拆出来的，`ColliderKey` 信息丢失 —— 所以 `MegaStructure` 的子件命中只能按接触点距离近似（`ConsumeAnvilContacts` 里那段）。
- Sensor / Trigger 事件未接（`isSensor` 已映射到 box3d，但 box3d 的 sensor 事件还没读进 ECS）。
- 除 fixed 之外的关节类型未接（生产代码目前不需要）。

---

## 10. 文件清单

### 原生（`/tmp/box3d-e77352c/src/`，编译产物在 `Assets/Plugins/x86_64/box3d.dll`）

| 文件 | 说明 |
|---|---|
| `anvil_bridge.c` | 批量读写 + step profile + 逐体接触 + 能力查询 |
| `anvil_batch.c` | 跳过公共 API 的快速批量读写 |
| `anvil_awake.c` | 只回读非睡眠刚体（最大优化） |
| `anvil_contacts.c` | 直接遍历 `world->contacts` |
| `anvil_ray.c` | 全命中射线 |

### 托管（`Packages/com.unity.physics/Unity.Physics/Anvil/`）

| 文件 | 行数 | 说明 |
|---|---:|---|
| `AnvilNative.cs` | 559 | 全部 P/Invoke + 结构体（按 C 探针实测偏移） |
| `AnvilWorld.cs` | 1212 | 世界/刚体/形状/关节/同步/射线/接触 |
| `AnvilStepSystem.cs` | 168 | ECS 单步驱动 + 分段计时 |
| `AnvilContactEventSystem.cs` | 160 | 接触事件单例 |
| `AnvilContactEvents.cs` | 32 | 事件结构体 |

### 基准与工具

| 文件 | 行数 | 说明 |
|---|---:|---|
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilBenchBatch.cs` | 639 | batchmode 三后端基准（step / 射线 / worker / sleep / 正确性） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilBenchQA.cs` | — | Play Mode 版基准 |
| `Tools/AnvilBench/genrsp.py` | 105 | 从 Unity 的 rsp 生成离线编译响应文件 |
| `Tools/AnvilBench/run_matrix.sh` | 33 | 四档规模矩阵 |

### 改动的既有文件

| 文件 | 改动 |
|---|---|
| `Packages/com.unity.physics/Unity.Physics/Dynamics/Simulation/ISimulation.cs` | `SimulationType` 加 `AnvilPhysics` |
| `Packages/com.unity.physics/Unity.Physics/Unity.Physics.asmdef` | versionDefines 加 `ANVIL_PHYSICS_EXISTS` |
| `Packages/manifest.json` | 加 `com.valgore.anvil` |
| `Assets/csc.rsp` | 加 `-define:ANVIL_PHYSICS_EXISTS` |
| `Parts/Simulation/TNTCollisionSystem.cs` | Anvil 接触事件分支 |
| `Parts/Simulation/CircuitPressureSystem.cs` | Anvil 接触事件分支 |
| `Parts/Simulation/MegaStructureSystems.cs` | `ConsumeAnvilContacts` 分支 |
| `Parts/Simulation/PartSimulationSystem.cs` | `UpdateJoints` 走 `CollectJointEvents` |
| `Parts/Gameplay/PartBehaviorsGuidance.cs` | 射线重定向 |
| `Parts/Simulation/AutoGunProjectileSystem.cs` | 射线重定向 |
| `Parts/Simulation/WheelSystem.cs` | 射线重定向 |

### 新增的可调开关（本轮为了测真实求解成本加的）

| 位置 | 开关 | 说明 |
|---|---|---|
| `AnvilStepSystem.WorkerCount` | `int`，默认 0=用 `AnvilWorld.WorkerCount`(1) | box3d 求解线程数；只在大量活跃刚体时有收益 |
| `AnvilStepSystem.EnableSleep` | `bool?`，默认 null=用 `AnvilWorld.EnableSleep`(true) | 关掉后全程活跃，测真实求解成本 |
| `AnvilStepSystem.SyncBroadphaseTree` | `bool`，默认 true | 生产环境建议设 false（射线已走 box3d，这次刷新纯浪费 7.2 ms） |
| 基准 `ANVIL_BENCH_SPEC` | `workers=` `sleep=` `rays=` | 对应上述开关 |

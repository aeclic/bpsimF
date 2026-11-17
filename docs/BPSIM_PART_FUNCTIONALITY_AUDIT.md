# bpsim 部件功能补全审计（2026-10-21）

## 结论

- **本轮新增了三个真正缺失的运行时行为：`MotorWheel`（动力轮）、`JetEngine`（喷气发动机）与 `Wing`（机翼升力，覆盖 Wings / Tailplane / MetalWing / MetalTail）。**
- **风扇 / 螺旋桨 / 旋翼不是缺失功能。** 它们由 `FanPropellerSystem`（ECS 系统）驱动，力和转速都按引擎功率缩放，`ProcessTouch` 也会切换它们的开关。
- **发动机（Engine / EngineSmall / EngineBig）也不需要独立行为。** 在 BPSS 里 `Engine` 本身不产生推力，只提供动力并切换烟雾/火焰/音效；bpsim 通过 `PartExtensionComponent.EnginePower/PowerConsumption` 把动力聚合到焊死组件上，供风扇、动力伞、动力轮使用。
- **本轮补齐了燃料系统（FuelBox / FuelTube + 喷气发动机耗油）与猪族（Pig / KingPig / GoldenPig + PartLevitation）。**
- **轮族不是缺口：视觉滚动、齿轮箱倒车、轮子地面接触都由 `WheelSystem`（ECS）实现并有 QA 覆盖。** 本轮核对时发现旧文档把它们列为「未实现」，实际早在 10-23 就已落地；同时删掉了 `PartBehaviorsPower.MotorWheelBehavior` 这段会与 ECS 侧重复施力的死代码。
- 验证：`Bpsim.QA.PartGameplayQA.Run("all")` **45/45 场景 PASS**（含灯族 6 个、`spotlight`、`pumpkin`、燃料 3 个、猪族 5 个），日志 `Logs/bpsim-gameplayqa.log`。

## 验证环境与方式

- Unity China `2022.2.23f1c1`，Entities / Unity Physics `1.0.0-pre.44`。
- 编辑器内进 Play Mode 后，通过 DSH bridge 调静态方法：
  ```bash
  python Tools/BPSim/bridge.py call playmode '{"action":"enter"}'
  python Tools/BPSim/bridge.py invoke Bpsim.QA.PartGameplayQA Run '["all"]'
  python Tools/BPSim/bridge.py invoke Bpsim.QA.PartGameplayQA Status
  ```
- QA 会加载空场景、摆放部件、调用 `PartSimulation.ProcessTouch` 触发，按场景等待 0.6–2.8 s 后检查数值。

### 本轮全量结果（节选，数值随帧率略有浮动）

| 场景 | 结果 | 关键数值 |
|---|---|---|
| hook | PASS | attached=True，拉动 7.89 |
| gun | PASS | shots=1 hits=1 |
| gen | PASS | generated=2 total=5 joints=4 |
| light | PASS | voided=2 |
| rocket | PASS | travel=5.27 speed=5.99 |
| tnt | PASS | separationX=7.12 |
| balloon | PASS | frame y=3.76 |
| umbrella | PASS | y=-4.66 frame y=-5.66 vy=-4.06 |
| poweredumbrella | PASS | y=5.78 vy=4.22 power=1.36 engine=150 consumption=100 |
| spring | PASS | punches=1 |
| kicker | PASS | y=4.43 |
| timebomb | PASS | separationX=7.27 |
| bellows | PASS | gain=2.81 |
| **motorwheel** | **PASS** | **power=1.27 engine=150 parts=3 joints=2 x=9.87 vx=13.15** |
| **jetengine** | **PASS** | **speed=17.23 x=14.86** |
| **wing** | **PASS** | **y=1.51 vy=1.87 x=3.63 angle=21.1° force=6.78 N mass=0.75** |

## 新增 1：MotorWheel（动力轮）

文件：`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs:776`

- `Init` 读取焊死组件的引擎功率并保持关闭；`Trigger` 切换开关（无引擎功率时拒绝并记日志）。
- `Tick` 把推力施加到**整个焊死组件**（`ComponentThrust.Apply`），而不是单个轮子。原因与气球/动力伞一致：部件之间是固定关节，单点冲量会被关节吸收；BPSS 也是通过 `AddForceAtPosition` 推整个刚体。
- 公式对齐 BPSS `MotorWheel.FixedUpdate`：
  - 功率因子 `factor = pow(min(enginePower / powerConsumption, 20), 0.585)`（比>1 时；否则 `pow(factor, 0.75)`，与 `Contraption.GetEnginePowerFactor` 一致）；
  - 最大速度 `15 * factor`；
  - 推力斜坡 `pow(throttle, 0.4)`，`throttle` 随时间增长到 1；
  - 速度衰减 `sqrt(1 - |v| / maxSpeed)`；
  - 基础加速度 `18 * factor`（BPSS 的力除以质量后量级相当）。
- 没做地面接触检测：BPSS 在轮轴处向下 raycast，命中地面后把力施加在接触点；bpsim 的行为层目前没有 `PhysicsWorldSingleton` 查询入口。现在的实现是直接推组件，因此**在空中也会产生推力**，这是已知差异，列为后续工作（见缺口 6）。

### QA 摆位要点

BPSS/bpsim 的轮子 `ConnectionDirection = Up` 且不是连接源（`IsConnectionSource=false`），所以**轮子必须放在车架正下方**才会焊上：

```
MetalFrame @ (0, 0)
MotorWheel @ (0, -1)   ← 只有这个方向能形成关节
Engine     @ (1, 0)    ← 发动机是 target-only，连到车架右面
```

第一次测试把轮子放在 `(1, 0)`，结果 `joints=1`、只有轮子自己飞出去；改为 `(0,-1)` 后 `parts=3 joints=2`，整车一起前进。

## 新增 2：JetEngine（喷气发动机）

文件：`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs:866`

- `Trigger` 切换开关；`Tick` 沿部件本地 `Forward()`（+X）给自身加冲量：
  - 加速度 `22 * (1 - v / 30)`，速度上限 30；
  - 每帧 `PartGameplayUtil.AddVelocity`（等价于持续推力）。
- 与 BPSS 的差异：原版有燃料/过热/`m_angle` 角度推力/关闭条件；**bpsim 没有燃料系统**（`PartType` 无燃料部件，`grep Fuel` 为空），所以当前是无限燃料、固定 +X 方向。角度调节和火焰特效列为后续。

### QA 摆位

单独一个 `JetEngine` 放在 `(0,0)` 即可；QA 里旋转 0，检查 1.4 s 后的水平位移/速度。

## 新增 3：Wing / Tailplane / MetalWing / MetalTail（机翼升力）

文件：`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs:914`（`WingBehavior`）；`PartGameplayManager.cs:195-199` 把四种翼面统一映射到该行为。

- 对齐 BPSS `Wings.cs`：`liftConstant = 0.8`；13 点响应曲线（-180,0）、（-135,-0.2）、（-90,0）、（-45,-0.2）、（-10,0）、（10,1.5）、（15,1.75）、（19,0.8）、（22,0.1）、（45,0.2）、（90,0）、（135,-0.2）、（180,0）。
- 迎角是**速度相对翼弦（本地 +X）的带符号角**：`sign(cross(chord, velocity).z) * acos(dot)`，逆时针（抬头）为正。
- 升力方向 `normalize(cross(worldZ, velocity))`，大小 `0.8 * speed² * coefficient`，单件上限 100 N；再按焊死组件总质量换算成速度增量，经 `ComponentThrust.Apply` 施加（与气球/动力伞/动力轮同一套“推整组”的机制）。
- 翼面没有开关，`Init` 即启用（BPSS 的翼面也一直生效）；速度低于 0.5 m/s 时不计算，避免静止抖动。
- QA：`wing` 场景在仿真开始 0.2 s 后给翼面加 `(8, 4)` 的初速度（约 +14° 迎角），再等 0.6 s；要求 `vy > 1` 且 `y > 0.2`。没有升力时自由落体会得到 `vy < 0`、`y < 0`，所以阈值能明确区分。实测约 `y=1.51, vy=1.87, angle=21°, coeff=0.30, force=6.78 N`（阻力会让速度下降，升力随之减小，但仍是正爬升）。

## 新增 4：灯族（PointLight 全 7 个索引 + SpotLight）

bpsim 里 `PartType.PointLight` 的索引 = BPSS prefab 编号 − 1：

| bpsim 索引 | BPSS prefab | BPSS 脚本 | bpsim 行为 |
|---|---|---|---|
| 0 | `Part_PointLight_01_SET` | `PointLight` | `PointLightBehavior` |
| 1 | `_02_SET` | `Mushroom` | `PointLightBehavior`（variant=mushroom） |
| 2 | `_03_SET` | `Lantern` | `PointLightBehavior`（variant=lantern） |
| 3 | `_04_SET` | `PointLight` | `PointLightBehavior` |
| 4 | `_05_SET` | `AlienPointLight` | `PointLightBehavior`（variant=alienlight） |
| 5 | `_06_SET` | `DecelerationLight` | `DecelerationLightBehavior` |
| 6 | `_07_SET` | `AutoControlLight` | `VoidLightBehavior` |

- **唯一能复刻的那一半是换图。** BPSS 的 `OnTouch` 只做三件事：翻转 `activated`、`activeSprite.SetActive(activated)`、`inactiveSprite.SetActive(!activated)`，外加 `lightSource.isEnabled`。bpsim 这个仓库**没有光源渲染层**（没有 `PointLightSource` / `EntityLight` / `LightManager`），所以“点亮”只能表现为 sprite 换图；这一点在验收里也是唯一可观测的量。
- 实现方式沿用已有范式：`PartEntityFactory.CreateChildMarker` 按子物体名把 `ActivePointLightSprite` / `InactivePointLightSprite` / `ActiveFullPointLightSprite` / `InactiveFullPointLightSprite` 翻译成 `PointLightVisual { Active, Full }`；`PartGameplayUtil.SetPointLightVisual` 遍历 `LinkedEntityGroup` 增删 `Unity.Rendering.DisableRendering`（`SceneRenderSystem` 的查询带 `WithNone<DisableRendering>`，所以真生效）。
- **`AutoControlLight`（索引 6）顺带修了一个既有视觉 bug**：prefab 里 `ActiveFullPointLightSprite` 与 `InactiveFullPointLightSprite` 的 `m_IsActive` 都是 1，也就是两张“斜向图”一直同时在显示。现在按 BPSS `CurrentType = m_gridRotation >= Deg_45 ? 1 : 0` 只显示其中一对（方型号用普通那对，斜型号用 Full 那对），另一对隐藏。
- `SpotLight`：BPSS 在 `FixedUpdate` 里 `m_lightConeContainer.localScale = Lerp(cur, new Vector3(target,1,1), dt*30)`，`target = activated ? 3 : 0.001`。bpsim 用 `PostTransformScale` 承载非等比缩放（`LocalTransform.Scale` 只能等比）；**同时必须把容器的 `LocalTransform.Scale` 归 1** —— prefab 里 `Container` 的 localScale 是 `(0,0,0)`，`LocalToWorld = LocalTransform 缩放 × PostTransformScale`，留着 0 会让光锥永远 0 倍（同 `MechanicalGateSystem.UpdateVisuals` 的写法）。8 个 `*Attachment` 的显隐本来由 `PartSceneUnmanaged` 的附件逻辑负责，未重复实现。
- 与 BPSS 的差异（bpsim 没有对应机制，只保留可移植的部分）：
  - `Mushroom`：原版 `LightSystem` 关闭时强制常亮且不可切换；bpsim 没有该设置，按“开”处理——默认熄灭、可点击点亮。
  - `Lantern`：原版点亮后按 `sin(t·mainSinSpeed)·sin(t·subSinSpeed)·flicker` 抖动 `lightTransform.localScale` 与灯罩 `SphereCollider.radius`，纯光效，未实现。
  - `AlienPointLight`：原版额外翻转 `LightManager.NightVisionOn`（全屏夜视）；bpsim 无夜视渲染，只在日志里留一行说明。
  - 所有灯的原版 `EntityLight` 光照渲染、`DecelerationLight` 的实际减速效果（bpsim 走的是物理，减速灯当前只换图 + 保留既有减速半径）都不在本次范围内。

### QA

新增 6 个场景：`pointlight`(0)、`mushroom`(1)、`lantern`(2)、`pointlightoff`(3，点两次验“关回去”)、`alienlight`(4)、`decelerationlight`(5)、`spotlight`。

- 灯的验证器 `VerifyLampVisual`：要求“可见集合 == {Active==期望开关 && Full==期望型号}”，且 `wrong == 0`（不该显示的一张都不能显示）。
- `spotlight` 验证器同时读 `PostTransformScale` 的 x（应到 3.000）与 `LocalToWorld` 的 c0 长度（应同样是 3.000），确认缩放真的传到了渲染侧。
- 全量 `Run '["all"]'` **37/37 PASS**（2026-10-24 10:07），日志 `Logs/bpsim-gameplayqa.log`；实测 `spotlight cone=(3.000,1.000) localScale=1.000 worldX=3.000`、`PointLight#6 sprites=4 visible=1 hidden=3 wrong=0`。

## 新增 5：南瓜（Pumpkin）

BPSS 侧两个文件：`Pumpkin.cs`（62 行，状态机全部挂在 `INFeature.FixedPumpkin` 上）与 `FixedPumpkinManager.cs`（143 行，真正的刚体冻结逻辑）。

原版语义：

- `PostInitialize()` 在 `FixedPumpkin` 开启时直接 `m_enabled = true` —— **南瓜放下去的瞬间就已经固定**，玩家不需要先点一下。
- `OnTouch()` → `SetEnabled(!m_enabled)` → `FixedPumpkinManager.NeedsUpdate = true`。
- `FixedPumpkinManager.UpdatePumpkins()`：先扫出所有 `IsEnabled()` 的南瓜的 `StrictConnectedComponent`（索引 0 与其它索引分两张表），再把落在这些组件里的**每个零件的每个 Rigidbody** `constraints = FreezeAll`、层切 `Ground`、`Sleep()`；关掉时按记录的原始 `constraints` 还原。
- `RotatablePumpkin` 只影响 `EffectDirection()` 与 `PrePlaced()` 的 `m_autoAlign = AutoAlignType.Rotate`，是建造期朝向逻辑，与固定语义无关。

bpsim 对应实现：

| 原版 | bpsim |
|---|---|
| `m_enabled` | `PumpkinPart.Enabled`（`Parts/Pumpkin.cs`） |
| `RigidbodyConstraints.FreezeAll` + `isKinematic` | `PhysicsMass.InverseMass = 0`、`InverseInertia = 0` 并清 `PhysicsVelocity` |
| `m_data` 字典记原始 constraints | `PumpkinFrozen { InverseMass, InverseInertia, Source }` 直接挂在被冻结的零件上 |
| 切 `Ground` 层 | 不复刻：bpsim 碰撞过滤是 `CollisionFilter` 位掩码，没有 Ground 层这个玩法概念；逆质量为零后求解器本来就不会再推它 |

系统：`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/FixedPumpkinSystem.cs`（`[UpdateInGroup(typeof(PartSimulationSystemGroup))]`），只在 `MarkDirty()` 之后重算一次（点南瓜 / 场景切换），平时一帧一次 `if (!dirty) return;`。behavior `PumpkinBehavior`（`PartBehaviorsPumpkin.cs`）只翻转状态与置脏，不直接搬物理数据。

QA：`pumpkin` 场景放「南瓜 + 铁框」在空中、无地面支撑；触发器在点击**之前**先记录 `InverseMass` 与 `Applied`（证明触发前确实是冻结态），点击后要求：`Enabled==0`、`Applied==0`、两个零件都不再有 `PumpkinFrozen`、逆质量回到 2.0 / 1.0、整体下落超过 1 单位。实测 `massBefore=0.0000 appliedBefore=1 massAfter=2.0000 frameMassAfter=1.0000 yBefore=0.00 yAfter=-11.18`，系统日志 `[FixedPumpkin] components=1 frozen=1 restored=0` → 点击 → `components=0 frozen=0 restored=1`。


## 新增 6：猪族（Pig / KingPig / GoldenPig）与 PartLevitation

BPSS 里猪族由一大串 MonoBehaviour 组成（`Pig.cs` 755 行、`KingPig.cs` 277、`GoldenPig.cs` 43、`KingPigMouth.cs`、`FaceRotation.cs` 140、`FixedRotation.cs`、`PartLevitation.cs`、`PigHat.cs` 129、`PigMask.cs` 58、`SpriteAnimation.cs` 259）。逐条对照后，**真正影响物理的只有阻尼模型和一条 `CanBeEnclosed()`**，其余全是表现层：

| 原版行为 | 是否迁移 | 原因 |
|---|---|---|
| `Pig.FixedUpdate`：`speed < 1` 时 `drag = angularDrag = 0.2 + 2.5*(1-speed)`，否则 `0.2 / 0.05` | **是** | 决定猪「推一下才滚得动」的手感 |
| `KingPig.Initialize` / `GoldenPig.Initialize`：固定 `drag = 0.5`、`angularDrag = 1` | **是** | 同上 |
| `CanBeEnclosed() => true`（三者都是） | 无需迁移 | bpsim `BasePart.CanBeContained` 默认允许 |
| `PartLevitation.FixedUpdate`：`AddForce(up * (enclosed ? enclosedForce : force))` | **是**（组件 + 系统） | 原版 force 值在 AssetBundle 里，取不到 → 默认不挂 |
| 13 种表情状态机 / 瞳孔跟随 / 眨眼 / 打鼾 / 咀嚼 | 否 | bpsim 图集里每只猪只有一张静态脸（`Part_Pig_<i>_<n>` 的 `n` 是帽子/眼镜/耳朵等**外观配件**，不是表情） |
| 帽子 / 面具（`PigHat` / `PigMask`）、`FaceRotation`、`SpriteAnimation`、`KingPigMouth` | 否 | 纯视觉/动画层，bpsim 无 Animator 也无需 |
| 表情音效、`AudioChorusFilter/Distortion/Echo`、碰撞星星/汗滴粒子 | 否 | bpsim 没有音频与粒子子系统 |

实现在 ECS 侧只有两件事：

- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Pig.cs`：`PigFamily`（`Pig` / `KingPig` / `GoldenPig`）与 `PigMotion { Family, PeakSpeed, Linear, Angular }`、`PartLevitation { Force, EnclosedForce }`。
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PigMotionSystem.cs`：两个 `[BurstCompile] IJobEntity`。`DampingJob` 每步按速度重算并写 `Unity.Physics.PhysicsDamping`——**阻尼做成组件数据而不是手动缩放 `PhysicsVelocity`**，因为 `PhysicsDamping` 是 Unity.Physics 的声明式阻尼，由求解器在速度积分里应用，手写缩放会和焊接约束、重力积分打架。`LevitationJob` 每步加 `force * inverseMass * dt`。

行为外壳 `PartBehaviorsPig.cs` 的 `PigBehavior` 只负责挂 `PhysicsDamping` + `PigMotion`、以及 `Describe()` 上报 `family / damping / peak / expressions`。点击（`Trigger`）在原版只会叫一声换个表情，这里退化成计数并打日志说明「无表情美术」。

QA（4 个场景）：`pig`（注入 0.4 m/s 并 `PhysicsGravityFactor = 0`，要求 `linear` 落在慢速区 1.2–2.71 且等于 `angular`）、`pigfast`（注入 5 m/s，要求回落 `0.200 / 0.050`）、`kingpig` / `goldenpig`（注入 2 m/s，要求恒为 `0.500 / 1.000`）、`levitation`（现场挂 `PartLevitation{Force=30}`，要求 `y` 抬升超过 1 单位）。实测：

```
result: PASS slow linear=2.237 angular=2.237 speed=0.19
result: PASS fast linear=0.200 angular=0.050 speed=4.71
result: PASS heavy(KingPig) linear=0.500 angular=1.000
result: PASS heavy(GoldenPig) linear=0.500 angular=1.000
result: PASS yBefore=-0.37 yAfter=9.67 applied=True
```

## 新增 7：燃料系统（FuelBox / FuelTube）与喷气发动机耗油

BPSS 侧两个文件：`FuelBox.cs`（274 行，容器）与 `FuelTube.cs`（297 行，连接介质 + 阀门），加上 `FuelSystem.cs`（213 行）的分油循环与 `JetEngine.cs` 的 `IFuelConsumer` 实现。

原版语义：

- `FuelSystem.FixedUpdate`：把燃料箱 / 燃料管 / 耗油件用并查集按「管子邻居规则」聚成连通组；每步先汇总每组的 `RequiredFuelAmount` 与 `ExpectedSupplyFuelAmount`，再按 `min(可供给/需求, 1)` 的比例给每个耗油件供油、按同比例从燃料箱抽走；最后让同一焊接组件里的 `Engine` 给燃料箱缓慢回油（`0.2 * dt` 每台发动机）。
- `FuelBox`：容量 `4`（按朝向分档：`Deg_135/180`、`Deg_225/270` 为 `1E+10`，后者还标 `infinite`）；`SupplyFuelAmount = min(amount, 1*dt)`、`RefuelingAmount = 0.5*dt`；液面 sprite 按 `ratio` 缩放与平移；固定冲量累积超过阈值会爆炸。
- `FuelTube`：`Common` / `Auto` / `Valve` 三种；只有 `Valve` 可点击（翻转 `m_enabled` 并置 `NeedsUpdate`）；阀门关掉时 `GetConnectedParts()` 不返回邻居，于是燃料组被切断。
- `JetEngine.IsTriggerable()` 要求所在燃料组的**燃料箱数量 > 0**，`ConsumeFuel()` 里供给不足时 `SetEnabled(false)` 自动熄火。

bpsim 对应实现：

| 原版 | bpsim |
|---|---|
| `IFuelContainer` / `IFuelConsumer` 接口 | `FuelContainer { Amount, Capacity, SuppliedStep, Infinite }` / `FuelConsumer { Required, Supplied, Requesting }` 组件 |
| `FuelTube.CurrentTubeType` + `m_enabled` | `FuelTubeLink { Kind, Open }` |
| 并查集分组 | `FuelSystem.Rebuild()`（低频，`MarkDirty()` 驱动）+ `NativeDisjointSet`，同组成员在 `NativeList` 里排成连续区间 |
| 每帧 `foreach part` 汇总 | `DistributeJob`（`[BurstCompile] IJobParallelFor`，**以组为并行单位**，组间零共享写入） |
| `Contraption.FindPartJoints` 判连接 | 同一 `PartConnectedComponent` + 网格四邻域（装着燃料箱的框按框的格子算） |
| 爆炸 / 质量随油量变化 | 不复刻：改质量要重算 `PhysicsMass.InverseInertia`，燃料箱通常焊在载具上，收益小于风险 |

新的部件类型（`PartType.FuelBox = 49`、`FuelTube = 50`、`Max = 51`）需要 prefab 与 `PartResources` 登记，两个 prefab 由 `Temp/gen_fuel_prefabs.py` 生成（`PartGroup_49_FuelBox.prefab` / `PartGroup_50_FuelTube.prefab`，叶子分别叫 `FuelSprite` / `TubeVisual`，由 `CreateChildMarker` 翻译成 `FuelLevelVisual` / `FuelTubeVisual`）。`PartExtensionMap.json` 里 **`FuelTube.isConnectionSource` 必须为 `true`**：bpsim 的焊接要求至少一方是连接源，而 FuelBox / JetEngine 都不是，若不打开则三件永远不会焊到一起（表现为整排悬空固定、速度恒为 0）。

`JetEngineBehavior` 相应重写：`Init` 挂 `FuelConsumer`，`Trigger` 先查 `HasFuelSource()`（读 `FuelGroupIndex` + `FuelSystem.TryGetState(...).Containers > 0`），`Tick` 每步写 `Required = (22 * 0.2/1000 + 0.1) * dt`、读回 `Supplied`，供油耗尽即 `Enabled = false`。

QA（3 个场景 + 原 `jetengine` 改造）：`fuel`（油箱→燃料管→发动机，要求油量下降且 `vx > 0.3`）、`fueldry`（先把油箱掏空再点火，要求发动机自己熄火）、`fuelvalve`（关阀门后点发动机，要求**拒绝启动**且推进方向速度 < 0.3）。实测：

```
result: PASS amount=3.854 capacity=4.0 supplied=0.0021 required=0.0021 engineOn=True vx=9.60
result: PASS engineOff=True requesting=False amount=0.0000
result: PASS valveOpen=False engineOn=False thrustSpeed=0.00
```

`fuelvalve` 有个坑：阀门关闭后**同一帧**点发动机时 `FuelSystem` 的组信息还是上一步的（重建是低频的），发动机就会带着空油箱启动一次。修法是给 `FuelSystem` 加 `RebuildNow()`，`FuelTubeBehavior.Trigger()` 翻转阀门后立刻调用它——只在玩家点击这种低频路径上付开销。


## 现有功能盘点（已确认可用）

| 部件 | 实现 | 说明 |
|---|---|---|
| GrapplingHook | `GrappleHookBehavior` | 索引 0–5 抓钩，6 连发枪 |
| Generator（8–10） | `GeneratorBehavior` | 生成部件 |
| PointLight | `PointLightBehavior` / `DecelerationLightBehavior` / `VoidLightBehavior` | 索引 0/3 基础灯、1 Mushroom、2 Lantern、4 AlienPointLight 换图；5 减速 + 换图；6 虚化 + 换图（含 *Full 那一对） |
| SpotLight | `SpotLightBehavior` | 索引 0–3 点击开关，光锥容器 `Container` 的 x 缩放 0.001 ↔ 3 |
| Rocket / RedRocket / CokeBottle / SodaBottle | `RocketBehavior` | 0.5 s 延迟、1.8 s 燃烧、加速度 16、限速 12 |
| TNT | `TNTBehavior` | 引信 + 爆炸分离 |
| Balloon / Balloons2 / Balloons3 | `BalloonBehavior` | 净升力 1.55g、上升限速 5.5、吸附半径 1.6 |
| Umbrella | `UmbrellaBehavior` | 打开/关闭下落速度 3.2 / 5.6 |
| PoweredUmbrella | `PoweredUmbrellaBehavior` | 力 80、加速度上限 40、上升 4.5 / 下落 3.2，按引擎功率缩放到 0.75 次方 |
| Spring / SpringBoxingGlove | `SpringBehavior` | 半径 1.8、力 9、冷却 0.35；拳套有锥形判定 |
| Kicker | `KickerBehavior` | 力 13，一次性 |
| TimeBomb | `TimeBombBehavior` | 1.6 s 引信、半径 4.2、力 14 |
| Bellows | `BellowsBehavior` | 10 N 向上、0.5 s 喷气、0.3 s 间隔、压缩到 0.3 倍 |
| Fan / Propeller / Rotor | `FanPropellerSystem` | 风扇 7 N（索引 5 为 60 N）、螺旋桨 37 N、旋翼 120 N；转速 1000×功率因子+700；已含贴图子物体旋转 |
| Engine 系列 | 数据层 | 只贡献 `EnginePower` / `PowerConsumption`，无独立行为（与 BPSS 的推力部分一致） |
| Pumpkin | `PumpkinBehavior` + `FixedPumpkinSystem` | 放下即固定；点击把整个焊接组件冻成静态体 / 松开（`PhysicsMass.InverseMass = 0`），见“新增 5” |
| Pig / KingPig / GoldenPig | `PigBehavior` + `PigMotionSystem` | 阻尼模型（普通猪速度相关、猪王/金猪常数 0.5/1），点击只记一次「无表情美术」，见“新增 6” |
| FuelBox / FuelTube | `FuelBoxBehavior` / `FuelTubeBehavior` + `FuelSystem` | 燃料连通组、液面、阀门开关；喷气发动机有油才出力，见“新增 7” |
| CartWheel / NormalWheel / SmallWheel / MotorWheel / StickyWheel / OffRoadWheel | `WheelBehavior`（点击）+ `WheelSystem`（物理与视觉） | 射线找地面、接触点冲量、15×功率限速与 `sqrt(1-v/vmax)` 衰减、齿轮箱取负倒车、`WheelPivot` 真转 / `FakeWheelPivot` 摆动；StickyWheel 另有阻尼 2 与 350 法向吸附 |

## 仍未补的缺口（按优先级）

1. **Wings / Tailplane / MetalWing / MetalTail 的升力。** **已实现并验证**（`WingBehavior`，见“新增 3”；QA `wing` PASS：y=1.51、vy=1.87，同时能看到迎角/升力系数/升力的诊断值）。
2. **CartWheel / NormalWheel 的视觉滚动。已实现并验证**（`WheelSystem.UpdateVisual`）：`Wheel.Angle += -360 * SpinSpeed / Circumference * dt`，半径取 `WheelPivot` 子实体自带球碰撞体（CartWheel 0.36、MotorWheel 0.45、OffRoad 0.90），再用 `quaternion.RotateZ` 写回 `LocalTransform.Rotation`；`FakeWheelPivot` 走 `sin(2θ)*8°` 摇摆分支。动力轮同一路径，所以「位移有了但轮子不转」这个偏差不存在。证据：`cartwheel` PASS `angle=481.93 pivotSin=0.645 radius=0.36`。
3. **Gearbox 倒车。已实现并验证**：`Gearbox` 组件只存开关位，`WheelSystem.CollectReversingComponents()` 把「处于倒车状态的焊接组件」汇总成集合，`UpdateThrottle` 对集合内的动力轮把 `Throttle` 取负。证据：`gearbox` PASS `activated=True throttle=-1.00 reversing=True vx=-0.84`。
4. **StickyWheel / OffRoadWheel 的抓地差异。已实现并验证**：`WheelKind.Sticky` 走 `ApplyStickyForce`（贴地阻尼 `m_drag=2` + 只在命中静态地面时施加 `m_stickiness=350` 法向力）；`OffRoadWheel` 在 bpsim 里是 `MotorWheel` 索引 7，`Wheel.Create` 识别成 `WheelKind.OffRoad` 且 `m_force=65`，半径取 0.9 的 pivot 球。证据：`stickywheel` PASS `kind=Sticky vx=12.98`、`offroadwheel` PASS `kind=OffRoad radius=0.90`。
5. **发动机视觉/音效。** BPSS `Engine.cs` 切换烟雾、火焰、音调；bpsim 目前只有动力数据，没有这些表现。
6. **轮子的地面接触。已实现并验证**：`WheelSystem.RaycastGround` 用 `CollisionWorld.CastRay` 从 `WheelPivot` 沿 `LastContactDirection` 打 `Radius + 0.1` 距离，`AllHitsCollector` 取回整条射线后跳过属于本部件的命中（`BelongsToPart` 查 `LinkedEntityGroup`），命中即置 `HasContact/Grounded` 并记 `LastContactDirection`/`LastForceDirection`；未命中时按 BPSS 的 `m_spinSpeed *= 0.98` 衰减。推力只走命中分支，所以「空中也推进」的偏差已经不存在。证据：`motorwheel` PASS `contact=True radius=0.45`。**仍未做**的是冰面/砾石等地面材质分支（bpsim 没有对应表面材质）。
7. **灯族已补齐**（见“新增 4”）：`PointLight` 索引 0–6 与 `SpotLight` 全部有行为；仍未复刻的是光源渲染本身（原版 `EntityLight`）以及 `Lantern` 的抖动、`Mushroom` 的 `LightSystem` 联动、`AlienPointLight` 的夜视。
8. **猪族已补齐（降级版）**（见“新增 6”）：`Pig` / `KingPig` / `GoldenPig` 的**阻尼模型**与 `PartLevitation` 已迁移；表情 / 瞳孔 / 帽子 / 呼吸音效 / 碰撞星星粒子没有对应资源，明确不复刻。
9. **Pumpkin（南瓜）已补齐**（见“新增 5”）：放下去即固定、点击把整个焊接组件冻成静态体。未复刻的是 `RotatablePumpkin` 的朝向自动对齐（建造期逻辑）与切 `Ground` 层。
10. **燃料系统已补齐**（见“新增 7”）：`FuelBox` / `FuelTube` 两个新部件 + `FuelSystem` 并查集拓扑 + Burst 分油作业，喷气发动机改为「有油才出力、没油自动熄火」。
11. 其它非交互/结构件（InterfacePart / GroundPart / CustomPart 等）不属于“可触发功能”，未纳入本轮。

## 代码位置

| 文件 | 内容 |
|---|---|
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehavior.cs` | `PartGameplayKind` 新增 `MotorWheel = 15`、`JetEngine = 16`、`Wing = 17` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs` | `PowerFactor`、`JetEngineBehavior`、`WingBehavior`（原来的 `MotorWheelBehavior` 已删除：物理全在 `WheelSystem`，留着会和 ECS 侧重复施力） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Wheel.cs` | `WheelKind`（Free/Motor/Sticky/OffRoad）、`Wheel` 运行时状态、`WheelPivot { Fake }` 标记 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/WheelSystem.cs` | `RaycastGround`（`CastRay` + 自命中过滤）、`ApplyContactForce`、`ApplyStickyForce`、`UpdateThrottle`（齿轮箱取负）、`UpdateVisual`（转 `WheelPivot`）、`CollectReversingComponents` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsWheel.cs` | `WheelBehavior`：只负责点击切换 `Wheel.Enabled` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartGameplayManager.cs` | `CreateBehavior` 映射（191–199） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/PartGameplayQA.cs` | 场景注册（167–178）、`BuildMotorWheelScene`（485）、`VerifyMotorWheel`（720）、`BuildWingScene`（506）、`VerifyWing`（777）；轮族另有 `cartwheel`/`gearbox`/`stickywheel`/`offroadwheel` 四个场景与 `ReadPivotRotation` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PointLight.cs` | `PointLightVisual { Active, Full }`、`SpotLightCone` 标记组件 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsLight.cs` | `PointLightBehavior`（灯族 0–4）、`SpotLightBehavior` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartGameplayUtil.cs` | `SetPointLightVisual`（486）、`SetSpotLightCone`（530） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/PartGameplayQA.cs` | `LampScene` / `VerifyLampVisual` / `BuildSpotLightScene` / `VerifySpotLight` / `ToggleTwice` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Pumpkin.cs` | `PumpkinPart { Enabled, Applied }`、`PumpkinFrozen { InverseMass, InverseInertia, Source }` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/FixedPumpkinSystem.cs` | 按焊接组件冻结/解冻（`MarkDirty()` 驱动，低频） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPumpkin.cs` | `PumpkinBehavior`（放下即固定、点击翻转） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/PartGameplayQA.cs` | `BuildPumpkinScene` / `TriggerPumpkin` / `VerifyPumpkin` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Pig.cs` | `PigFamily`、`PigMotion { Family, PeakSpeed, Linear, Angular }`、`PartLevitation { Force, EnclosedForce }` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PigMotionSystem.cs` | `DampingJob`（写 `PhysicsDamping`）、`LevitationJob` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPig.cs` | `PigBehavior`（挂阻尼组件、上报状态） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Fuel.cs` | `FuelContainer` / `FuelConsumer` / `FuelTubeLink` / `FuelGroupIndex` / `FuelGroupState` / `FuelLevelVisual` / `FuelTubeVisual` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/FuelSystem.cs` | 并查集拓扑重建 `Rebuild()` + `RebuildNow()` + `DistributeJob` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsFuel.cs` | `FuelBoxBehavior`（容量分档、液面）、`FuelTubeBehavior`（阀门开关、换图） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsPower.cs` | `JetEngineBehavior`（866 起）：接 `FuelConsumer`、没油熄火 |
| `Assets/GameObject/PartGroup_49_FuelBox.prefab` / `PartGroup_50_FuelTube.prefab` | 由 `Temp/gen_fuel_prefabs.py` 生成，已登记进 `PartResources.asset` |
| `Assets/TextAsset/PartExtensionMap.json` | `FuelTube.isConnectionSource = true`（焊接前提） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/PartGameplayQA.cs` | `BuildFuelScene` / `VerifyFuel` / `TriggerFuelDry` / `VerifyFuelDry` / `TriggerFuelValve` / `VerifyFuelValve`；`BuildPigScene` / `SetPigVelocity` / `VerifyPigSlow` / `VerifyPigFast` / `VerifyHeavyPig` / `TriggerLevitation` / `VerifyLevitation` |

## 复验

```bash
python Tools/BPSim/bridge.py call playmode '{"action":"enter"}'
python Tools/BPSim/bridge.py invoke Bpsim.QA.PartGameplayQA Run '["all"]'
# 约 45 s 后
python Tools/BPSim/bridge.py invoke Bpsim.QA.PartGameplayQA Status
```

预期最后一行 `done:all`，每个场景均为 `PASS`；完整日志在 `Logs/bpsim-gameplayqa.log`。

### 编译判定（踩过的坑）

`bridge.py call compile '{}'` 只是 `CompilationPipeline.RequestScriptCompilation()`，**在 play mode 里会被忽略、在编辑器空闲时也可能什么都不做**；而 `compile_status` 只统计 bridge 自己加载后抓到的日志，所以**编译失败时它照样返回 `errors: []`**。可靠的判定方式：

```bash
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"; N=$(wc -l < "$LOG")
python Tools/BPSim/bridge.py invoke UnityEditor.AssetDatabase Refresh '[]'   # 真正触发导入+编译
sleep 30
tail -n +$((N+1)) "$LOG" | grep -E "error CS|Tundra build"
ls -l --time-style=+%H:%M:%S Library/ScriptAssemblies/Assembly-CSharp.dll   # 时间戳必须变新
```

新增 `.cs` 文件后必须先 `AssetDatabase.Refresh` 让 Unity 生成 `.meta` 并导入，否则 `RequestScriptCompilation` 完全看不到它们。

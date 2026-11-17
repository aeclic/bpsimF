# 汽水 / 火箭推力移植（BPSS 901-17.0 → bpsim）

参考工程：`../BPSS[工程]901-17.0`（2D 原版），重点文件 `Assets/Scripts/Assembly-CSharp/Rocket.cs`
本文记录 bpsim 侧「汽水（CokeBottle / SodaBottle，各 5 个皮肤）正确出力」的修复依据、实现与验收。

## 1. 原版语义（逐条对齐）

四族（CokeBottle / SodaBottle / Rocket / RedRocket）在原版共用同一个 `Rocket` 脚本，
差别只有 prefab 上序列化的字段 + `Initialize()` 里乘进去的 INSettings 倍率：

| prefab | m_boostForce | m_maximumSpeed | INSettings（ISettingsB，即对局实际那份） | 落地值 |
| --- | --- | --- | --- | --- |
| Part_SodaBottle_01..04 | 35 | 10 | CokeSodaForce ×2 / CokeSodaSpeed ×2 | 70 / 20 |
| Part_CokeBottle_01..04 | 35 | 10 | 同上 | 70 / 20 |
| Part_SodaBottle_05（异星） | 150 | 150 | AlienSoda 200 / 200（绝对值，不再乘） | 200 / 200 |
| Part_CokeBottle_05（异星） | 35 | 25 | AlienCoke 150 / ∞ | 150 / 40（见 §3） |
| Part_Rocket_01…, RedRocket | 50 | 18 | Rocket ×2 / ∞ | 100 / 18 |

其它三条从 `Rocket.cs` 读出来的关键点：

- **方向**：prefab 上 `m_direction = (1,0,0)`，FixedUpdate 里
  `AddForceAtPosition(force * transform.TransformDirection(m_direction), pos, ForceMode.Force)`，
  即沿**零件自身 +X**；`EffectDirection()` 同样是 `Rotate(Direction.Right, m_gridRotation)`。
  贴图是横躺的，所以旧实现的「往局部 +Y（Up）怼」是错的。
- **力度是力不是速度**：原版加在 contraption 的刚体上，加速度 = 力 / 质量。
- **开关**：`ISettingsB.SwitchableCokeSodaRocket = true` 时 `OnTouch` 只做 `m_enabled = !m_enabled`，
  `m_enabled` 为真期间 FixedUpdate 一直加力；`m_ignitionTime` / `m_boostDuration` 只影响粒子与可视化。
- **限速是软衰减**：`LimitForceForSpeed()` 里
  `return forceMagnitude / (1f + alongSpeed - m_maximumSpeed)`（超出上限才衰减）。

## 2. bpsim 侧改动

| 文件 | 改动 |
| --- | --- |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviorsLaunch.cs` | **新增**。`LaunchProfile`（型号表）+ `LaunchBehavior`（推力/开关/限速） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartGameplayManager.cs:179-185` | 四族映射到 `LaunchBehavior`（原来是旧的 `RocketBehavior`） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/PartLaunchQA.cs` | **新增**。Play 模式验收探针，26 项断言 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/PartBehaviors.cs` | 旧的 `RocketBehavior`（一次性点燃、沿局部 +Y 推）已删除：换成 `LaunchBehavior` 后无任何引用 |

实现要点：

- 每 tick：`Δv = force * dt / mass`，`mass = ComponentThrust.TotalMass(Part)`（整块焊件），
  再把这个 Δv 平摊给同一 `ConnectedComponent` 的所有可动件（`LaunchBehavior.Push`）。
  这样「车上装一瓶汽水」推的是整台车，而不是先把瓶子从焊点上扯下来。
- `Enabled` 跟着点击翻转，开着就一直出力（不是旧实现的一次性点燃）。
- 粒子按用户要求**未移植**；旧实现里的点火/燃烧可视化也一并没有带过来。

## 3. 三处有意的偏离（都有实测依据）

1. **把 ∞ 限速兑现成 40**（`LaunchBehavior.UnlimitedSpeedCap`）。
   原版是 2D 有地面有墙，∞ 会被地形兜住；bpsim 的刚体没有墙，100 力打在 0.5 质量的瓶子上
   是 200 m/s²，十几秒后飞出关卡并把定步长求解器炸掉。

2. **到顶直接停手，而不是原版的 `force/(1+超出量)` 软衰减**。
   实测同一个公式在 bpsim 上会漂到上限的 **1.6 倍**（70/0.5kg 的汽水冲到 31.5 m/s，上限 20；
   火箭 32.9，上限 18）——原版靠 2D 阻力把速度按住，bpsim 阻尼小得多。
   改成 `alongSpeed >= m_speedLimit` 就这一 tick 不加力后，速度稳定在 **21.2 / 201.6 / 20.2**
   （超出的部分是最后一步加力的余量），掉回上限以下会自动续上。

3. **推力方向取网格朝向（`PartTransform.Rotation`），不取实体旋转**。
   实测放下 `rotation = 1` 的部件后，`PartTransform.Rotation` 是 1，但实体的
   `LocalTransform.Rotation` 与 `LocalToWorld.Rotation` 都还是 0°
   （部件实体旋转只在 `PartTransformSystem` 里同步，本项目跑的是 managed DOTS fallback）。
   若读实体旋转，所有横躺的汽水/火箭都只会朝世界 +X 推。改用
   `GridRotation(rotation, false) * (1,0,0)` 后，`rotation = 1` 的汽水实推 +Y（实测 v=(0, 20.8)）。
   ——这是本仓库一个既存现象（不止推力部件会受影响），已在 QA 里记为 `INFO` 而不是失败项。

## 4. 验收

Play 模式下：

```
POST http://127.0.0.1:8791/api  {"method":"invoke_static","params":{"type":"Bpsim.QA.PartLaunchQA","method":"Run","args":["all"]}}
POST http://127.0.0.1:8791/api  {"method":"invoke_static","params":{"type":"Bpsim.QA.PartLaunchQA","method":"Status","args":[]}}
```

报告同时写到 `Logs/bpsim-partlaunchqa.log`。最新一次：**done | pass=26 fail=0**，摘要：

```
PASS profile CokeBottle#0        force=70.0  limit=20.0
PASS profile CokeBottle#4 异星    force=150.0 limit=40.0
PASS profile SodaBottle#0        force=70.0  limit=20.0
PASS profile SodaBottle#4 异星    force=200.0 limit=200.0
PASS profile Rocket#0 / RedRocket force=100.0 limit=18.0
PASS skins CokeBottle 0..4      5/5 预制体
PASS skins SodaBottle 0..4      5/5 预制体
PASS 汽水 CokeBottle#0 出力      v.x=21.20 x=49.79 mass=0.50
PASS 可乐 SodaBottle#0 出力      v.x=21.20
PASS 异星汽水 SodaBottle#4 出力  v.x=201.57
PASS 火箭 Rocket#0 出力          v.x=20.18
PASS 朝向 网格旋转               grid=1
INFO 朝向 实体旋转               localToWorld=0.0 local=0.0 期望≈90
PASS 朝向 旋转 90° 后向上出力    v=(0.00,20.80)
PASS 小车 汽水推动整个组件       bottle.v.x=21.20 passenger.x=32.00
PASS 开关 触发后出力 / 再触发关掉 / 三触发重新打开  v1=116.7 v2=109.5 v3=203.4
```

## 5. 踩过的坑

- **新增 .cs 必须先 `AssetDatabase.Refresh`**，只调 `compile` 不会把新文件编进程序集
  （表现为 `invoke_static ... type not found`）；反过来，**Refresh/compile 的触发时机必须晚于文件写入**，
  否则 DLL 是旧的、跑的还是改之前的代码（本次第一次验「限速」时就吃了这个亏：源码 20:29:32，
  编出来的 DLL 是 20:29:17）。判断办法：`ls -l Library/ScriptAssemblies/Assembly-CSharp.dll` 与源文件比时间。
- `PartBehaviorsLaunch.cs` 里用了 `PartSimulatorUnmanaged` 就必须 `using Bpsim.Parts.Simulation;`，
  否则报 `error CS0246` —— 而 Unity 只在 Editor.log 里写，
  桥接的 `compile_status` 当时还报 0 errors，容易被骗。
- Play 模式被拒绝时报的是 `All compiler errors have to be fixed before you can enter playmode!`，
  真正的 `error CS` 要去 `%LOCALAPPDATA%/Unity/Editor/Editor.log` 里找。

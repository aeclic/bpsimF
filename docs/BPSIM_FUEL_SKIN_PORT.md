# bpsim：燃料件并入喷气引擎皮肤 + 删除 FuelBox/FuelTube

本次改动的目标（用户口径 m01118 / m02364）是「删除部件 59/60」——即 bpsim 里
`PartType.FuelBox`(49) / `PartType.FuelTube`(50) 两个独立部件类型。做法不是简单删表项，
而是先按用户指示核对「喷气引擎这些皮肤本身有没有燃料功能」。

## 一、结论：燃料件本来就是 JetEngine 的皮肤

BPSS 901-17.0 的 `BasePart.PartType` 里**没有** FuelBox / FuelTube：

| 类型 | 值 |
| --- | --- |
| `PartType.JetEngine` | 37 |
| `PartType.MAX` | 51 |

而预制体族 `Part_JetEngine_01_SET … _06_SET` 是混编的：

| 索引 | BPSS 上挂的脚本 | 说明 |
| --- | --- | --- |
| 0 / 1 | `JetEngine.cs` | 发动机本体 |
| 2 / 3 | `FuelTube.cs` | 燃料管（普通 / 自动） |
| 4 | `FuelBox.cs` | 燃料箱（带液面） |
| 5 | `FuelTube.cs` | 燃料阀门（唯一可点的管） |

bpsim 的 `PartGroup_46_JetEngine.prefab` 本来就是照这个族做的（子物体
`Part_JetEngine_0..5`，`ManagedPart` 全是 `m_partType: 46`），所以合并只是把行为层
接上去，不需要动预制体。

## 二、改动清单

### 1. 类型表
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartType.cs`
  - 删除 `FuelBox = 49`、`FuelTube = 50`；
  - `SkinPart` 从 51 前移到 **49**，`Max` 52 → **50**；
  - 加注释说明燃料件是 JetEngine 的皮肤。
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartTypeExtensions.cs`
  - 删除两行别名（`FuelBox => "燃料箱"`、`FuelTube => "燃料管"`）。

### 2. 行为分派
- `Parts/Gameplay/PartGameplayManager.cs`
  - `PartType.JetEngine` 不再一律给 `JetEngineBehavior`，改为按索引：
    2/3/5 → `FuelTubeBehavior`，4 → `FuelBoxBehavior`，其余 → `JetEngineBehavior`。
- `Parts/BasePart.cs`
  - `IsTriggerable` 的 `case PartType.FuelTube (index==5)` 换成
    `case PartType.JetEngine: return PartIndex == 5 || PartIndex <= 1;`
  - `GetTriggerButtonInfo` 仍按 `PartIndex` 走（不需要特殊处理）。
- `Parts/PartAutoAlign.cs`：删掉 `case PartType.FuelBox:`。

### 3. 实体烘焙
- `Parts/PartEntityFactory.cs`
  - `CreateChildMarker`：`"FuelSprite"` 之外再加 `"Fuel"`（`Part_JetEngine_4` 的子物体叫
    `Fuel`）→ `FuelLevelVisual`；
  - `CreatePart`：`PartType.JetEngine` 且索引 2/3/5 时给实体加 `FuelTubeVisual`
    （阀门换贴图 `SetFuelTubeSprite` 找的就是「带 `FuelTubeVisual` + `SpriteID`」的实体）。

### 4. 按钮贴图
- `UI/PartButtonList.cs`
  - 删掉 `FuelTube` 的批量注册与 `FuelBox` 的单条注册；
  - 改为按索引注册 JetEngine 的 6 个皮肤：0/1 `PartButton_JetEngine`、
    2/3/5 `PartButton_ValveFuelTube`、4 `PartButton_Vcc`；
  - 去掉 `FindSpriteInfo` 里「JetEngine 一律把 PartIndex 抹成 -1」的老逻辑，
    否则燃料管会顶到引擎图标。

### 5. 资源
- `Assets/TextAsset/PartExtensionMap.json`
  - 删除 `FuelBox`、`FuelTube` 两个 key；
  - `JetEngine` 索引 4（燃料箱）`connectionDirection` 从 `None` 改成 **`Any`**。
    这是关键：`PartSimulator.CanConnectTo` 要求「即使对方不是连接源，它的
    `ConnectionDirection` 也必须包含指向我的方向」（PartSimulator.cs:325），
    `None` 会让燃料箱彻底焊不到管子上 → 燃料组里恒为 0 个容器 → 发动机拒绝启动。
  - `JetEngine` 索引 3（自动管）同样从 `None` 改成 `Any`（它是连接源却没有端子掩码，
    等于谁都连不上）。
- `Assets/MonoBehaviour/PartResources.asset`：删除 `PartGroup_49_FuelBox` 与
  `PartGroup_50_FuelTube` 两条引用。
- 删除文件：`Assets/GameObject/PartGroup_49_FuelBox.prefab(.meta)`、
  `Assets/GameObject/PartGroup_50_FuelTube.prefab(.meta)`。

### 6. 蒙皮部件编号跟着前移
- `Assets/GameObject/PartGroup_51_SkinPart.prefab` → 改名
  `PartGroup_49_SkinPart.prefab`，`ManagedPart.m_partType` 51 → 49（guid 不变，
  `PartResources` 按 guid 引用，所以改名安全）。

### 7. QA
- `Bpsim/QA/PartGameplayQA.cs`：`fuel` / `fueldry` / `fuelvalve` / `jetengine` 四个场景
  全部从 `PartType.FuelBox|FuelTube` 改写成 `PartType.JetEngine` 的索引
  （4 燃料箱、2 管、5 阀门、0 发动机）。

## 三、燃料系统本身没有改动

`Parts/Fuel.cs`（`FuelContainer` / `FuelConsumer` / `FuelTubeLink` / `FuelGroupIndex` /
`FuelGroupState` / `FuelLevelVisual` / `FuelTubeVisual`）、
`Parts/Simulation/FuelSystem.cs`（并查集 + 四邻域连通 + 回油）、
`Parts/Gameplay/PartBehaviorsFuel.cs`（`FuelBoxBehavior` / `FuelTubeBehavior`）
都是原样保留，只是挂载位置从「独立 PartType」变成「JetEngine 的索引」。

`PartBehaviorKind.FuelBox / FuelTube` 这两个**玩法种类**枚举值也保留——它们描述的是
行为种类而不是 `PartType`，与本次删除无关。

## 四、验收

见 `Logs/bpsim-gameplayqa.log` 中 `fuel` / `fueldry` / `fuelvalve` / `jetengine` 四个场景的
`result:` 行。

# BPSIM 图层 / 连接 / 无网格虚线系统

> 参考实现：`BPIR`（https://gitee.com/yctf123123/bpir，路径 `Assets/Scripts/Assembly-CSharp/ContraptionLayer*.cs`、`LayerPanelUI.cs`、`JointManager.cs`）与主项目 BPRC 的 `ContraptionLayers.cs` / `ContraptionLayerView.cs` / `INBuildLayerSelector.cs`。
> 本工程按 ECS 重建：占位表以 `PartGridMap` 的 `int3.level` 作为图层，图层号与渲染/降噪/存档解耦；没有把图层塞进 `PartTransform`。

## 1. 操作入口

- 右上角、`关卡` 按钮左侧有一个**三叠层图标按钮**（`LayerPanelCanvas/LayerButton`），由 `LayerPanelUI` 在运行时自建，不依赖 `BuildToolbar`/`UserInterface.prefab`。
- 点击按钮展开竖向「建造图层」菜单：
  - `全部（查看）`：只读总览；此时活动层为 0，放置/移动/旋转/删除都会被 `PartSceneUnmanaged.CanEdit` 拒绝。
  - `图层 N（公共层）`（N=1）与 `图层 N  M`（M=该层零件数）：点击切换活动层。
  - 底部 `＋ 添加图层` / `－ 删除顶层`；删除仅当顶层为空。
- 模拟开始后菜单自动隐藏，避免占用驾驶 HUD。
- 工具栏另有「无网格」开关；开启后普通网格隐藏，并绘制黄色虚线建造辅助框。

## 2. 数据模型

| 类型 | 位置 | 说明 |
| --- | --- | --- |
| `PartBuildLayer` | `Bpsim/Parts/PartBuildLayer.cs` | `IComponentData { byte Value }`；`MinLayer=1`、`MaxLayer=16`、`DepthStep=0.02f`；`Clamp(int)`；`PartBuildLayerUtility.GetLayer/SetLayer/EnsureLayer` |
| `PartLayerTint` | `Bpsim/Rendering/PartLayerTint.cs` | 每渲染实体的 `float4` 图层乘色；`White=(1,1,1,1)` |
| 占位表 | `PartSceneUnmanaged.m_partMap` | 键 `(x, y, layer)`；每层独立占位，因此**跨层可重叠**、同层仍互斥 |

`PartSceneUnmanaged` 新增：`ActiveLayer`、`LayerCount`、`CanEdit`、`SetActiveLayer`、`SetLayerCount`、`AddLayer`、`RemoveTopLayer`、`GetPartLayer`、`CountPartsOnLayer`、`TrySetPartLayer`；所有格表读写（`FindPartAt`、`FindPartGridAt`、`PlacePartAt`、`RemovePartAt`、`TryMovePartContent`、`TryRotatePart`、`TryMoveAllParts`、`TryRotateAllParts`、`RebuildGrid`、`UpdateNeighbours`）都按活动层或显式 `layer` 参数执行。

## 3. 图层规则（对齐 BPIR）

- **占位**：每层一张独立占位表，跨层同格允许；同层同格按原有 `CanPlacePartAt` 拒绝。
- **编辑**：所有建造操作只作用于活动层；`全部（查看）` 不可编辑。
- **连接**：同层四邻域可连接；层 2..N 的零件按 BPIR `Contraption.ConnectPart` 的原始规则桥接层 1：**先看「正下方」同格**有没有层 1 零件，有就只连那一个（四个方向逐个试方向判定，命中即止），**没有**才回退连层 1 的四邻域；层 2..N 彼此不直接连接。
- **纵深**：`PartFreeTransformSystem` 在合成位置时写 `z -= (layer-1)*0.02`，高图层更靠前，天然形成遮挡。
- **降噪**：非活动层的 `PartLayerTint` 变为 `(0.55,0.58,0.65,1)`；活动层与「全部」为白色；模拟开始瞬间恢复白色，避免带进驾驶画面。
- **物理/碰撞**：本期只做了关节/连通分量的层感知；**没有**移植 BPIR 的「同层碰撞、跨层忽略」物理层矩阵（见限制）。

## 4. 渲染：`_LayerTint`

部件 shader 原本的 `_Color` 只在 `_BlendFactor.y` 不为 0 时参与（普通部件 `_BlendFactor=(1,0,0,0)`），并且被 `ColoredFrameSystem` 每帧覆盖，不能直接拿来做图层降噪。因此新增 per-instance 属性链路：

- `Assets/Shader/Bpsim_URP_Unlit_Texture_Instancing.shader`：Properties/UnityPerMaterial/DOTS instancing 块各加 `_LayerTint`；顶点按 `BPSIM_INSTANCED_PROP(float4,_LayerTint,float4(1,1,1,1))` 传入 `TEXCOORD3`，片元 `rgb *= input.layerTint.rgb`。
- `Bpsim/Parts/SceneRenderSystem.cs`：实例布局从 6 个 float4 变 7 个；第 7 个 `_LayerTint` 来自 `PartLayerTint`（缺组件回退白色）。
- `Bpsim/Parts/PartEntityFactory.cs`：每个渲染实体创建时加 `PartLayerTint.White`。
- `Bpsim/Parts/PartBuildLayerDimSystem.cs`：`PresentationSystemGroup` 里每 0.25s 按活动层刷新渲染实体的 `PartLayerTint`。

## 5. 连接系统

- 建造期 `PartConnectionPreview`（挂在 `UserInterface` 下）：
  - 同层四邻域：青色 `(0.25,0.9,1)`；
  - 层 2..N → 层 1 同格/四邻域：橙色 `(1,0.55,0.15)`，同格画短竖线、异格沿垂直方向偏移 0.14 避免与青线重合；
  - 仅在 `GridlessEnabled || ActiveLayer > 1` 且非模拟时显示。
- 模拟期 `PartSimulator`：
  - `BuildPartsJob` 的 `PartMap` 键改为 `(x,y,layer)`；
  - `SeedWorldTransformJob`（`PartSimulator.Run` 里紧跟 `InstantiatePartsJob`）按 `PartTransform` 用 `BasePart.MoveTo/RotateTo` 重算并写回 `LocalTransform`，同时补 `WorldTransform`。原因是 `BuildJointsAndComponentsJob` 是同一帧同步 `IJobExtensions.Run`，跑在 `TransformSystemGroup` 之前：那时实例化出来的部件既没有落位后的 `LocalTransform`（`PartTransformSystem` 模拟期整个跳过），也没有 `WorldTransform`（只有 `LocalToWorldSystem` 会给「有 LocalTransform、没有 WorldTransform」的实体补）——`PartSimulatorUnmanaged.AddFixedJoint` 直接索引 `WorldTransformLookup` 会抛 `ArgumentException: A component with type:Unity.Transforms.WorldTransform has not been added to the entity`，整份 job 中断，一个关节都建不出来；
  - `BuildJointsAndComponentsJob` 同层四邻域按 `CanConnectTo` 建 fixed joint；层 2..N 再按上面 §3 的规则桥接层 1（`CanConnectAcrossLayers` 做方向判定），并入连通分量。
- **未移植**：BPRC/BPIR 的显式连接点 prefab 结构（`ConnectionPoints/Point-*`、`PartConnections.TryGetConnection`）。bpsim 预制体没有该节点；目前连接判定完全沿用 `PartSimulator.CanConnectTo` 的方向/强度规则。

## 6. 无网格黄色虚线

- `Bpsim/UI/GridlessOverlay.cs`：`BuildToolController` 启动时挂到 `UserInterface`。
- 开启无网格且非模拟时：
  - Brass `(0.71,0.59,0.37)` 画整装配外包围 + 起始十字；
  - Ivory `(0.93,0.89,0.79)` 画每个同层连通组的包围盒；
  - 虚线由 768 个 `LineRenderer` 池拼段（Dash .34 / Gap .22，z=-0.6）。
- `PartScene.GridlessEnabled` 的 setter 现在会立即调用 `SetGridEnabled()`：`gridVisible = m_gridEnabled && !m_gridlessEnabled`，所以切换无网格时普通网格同步隐藏/恢复。

## 7. 存档格式

`Schematics.Unit` 追加 `int Layer`（默认 1，clamp 1..16）。旧档兼容：

- JSON：字段缺失为 0 → `Normalize()` 归一到 1；写出含 `layer`。
- CSV：12 列 `type,index,x,y,rotation,flipped,offsetX,offsetY,rotationDegrees,scaleX,scaleY,layer`；读取仍接受 6 列 / 11 列旧档，缺省 layer=1。
- XML：新增 `layer` 属性；旧档缺省为 0，读取后归一到 1。

`PartCreationRequest`、`PartSceneJobs.SavePartsJob`、`PartManager.SaveScene`、`BuildUndoHistory`（快照走 `SaveParts`/`PlaceParts`）全部透传层号，所以撤销会恢复图层。

## 8. 验收

| 套件 | 结果 | 报告 |
| --- | --- | --- |
| `Bpsim.QA.LayerConnectionQA` | **27 / 27 PASS** | `Logs/bpsim-layerconnectionqa.log` |
| `Bpsim.QA.FreeTransformQA` | **67 / 67 PASS** | `Logs/bpsim-freetransformqa.log` |
| `Bpsim.QA.BuildToolbarQA` | **118 / 118 PASS** | `Logs/bpsim-toolbarqa.log` |
| `Bpsim.QA.BpirImportQA` | **32 / 32 PASS** | `Logs/bpsim-bpirimportqa.log` |

Batch 入口：`Assets/Editor/LayerConnectionQaEditor.cs`（`-executeMethod Bpsim.EditorTools.LayerConnectionQaEditor.RunBatch`）。
统一入口：`Tools/unity-qa.bat <LayerConnection|FreeTransform|BpirImport|BuildToolbar>`，内部分派到各自的 `Bpsim.EditorTools.<X>QaEditor.RunBatch`。
BPIR 导入的 batch 入口：`Tools/unity-bpir-qa.bat` → `Bpsim.EditorTools.BpirImportQaEditor.RunBatch`。
这些驱动自己进 Play Mode 并调 `EditorApplication.Exit`，所以**不能**走带 `-quit` 的 `Tools/unity-batch.bat`（那是编译/构建用的）。

**batch 模式不重放关卡会话**：`LevelSession.AutoRestore` 默认值改为 `!Application.isBatchMode`。以前自动化跑 QA 时会继承上一次交互会话留在 PlayerPrefs 里的关卡（例如 `Level_20`），`LevelManager.RestoreSession` 把相机按关卡包围盒推到 `orthographicSize >= 50`，于是 `PartScene.SetGridEnabled` 按设计隐藏细网格，`LayerConnectionQA` 的 `grid restored`、`BuildToolbarQA` 的 `grid mode cycles back to snap` 就会假失败；关卡里的零件还会污染 `BuildToolbarQA` 的 `magnifier zooms out` 断言（它期望场景里有够多零件把概览尺寸顶上去）。交互运行和打包版本仍然默认恢复。

视觉证据：`Temp/layer_dim.png`（层降噪）、`Temp/layer_full2.png`（黄虚线 + 图层面板）、`Temp/connection_visual3.png`（连接线）。

## 9. 限制

1. **跨层碰撞未禁用**：模拟期各层仍在同一物理层，BPIR 的 6 层物理矩阵没有移植；跨层叠放只在建造占位与关节层面成立。
2. **显式连接点未移植**：见 §5。
3. **层数/无网格状态不存档**：`LayerCount` 由载入时的最大层号推出；无网格开关是运行时/场景状态，不写进 `Schematics`。
4. **层号上限 16**：面板允许一直添加，但 `Clamp` 到 16。
5. 模拟开始后图层面板隐藏；图层降噪在 `PresentationSystemGroup` 每 0.25s 刷新一次，层切换的视觉反馈最多延迟 0.25s。

## 10. 涉及文件

- 新增：`Bpsim/Parts/PartBuildLayer.cs`、`Bpsim/Parts/PartBuildLayerDimSystem.cs`、`Bpsim/Rendering/PartLayerTint.cs`、`Bpsim/UI/GridlessOverlay.cs`、`Bpsim/UI/PartConnectionPreview.cs`、`Bpsim/UI/LayerPanelUI.cs`、`Bpsim/QA/LayerConnectionQA.cs`、`Assets/Editor/LayerConnectionQaEditor.cs`。
- 修改：`PartSceneUnmanaged.cs`、`PartScene.cs`、`PartCreationRequest.cs`、`Schematics.cs`、`SchematicsCsvLoader.cs`、`SchematicsXmlLoader.cs`、`PartSceneJobs.cs`、`PartManager.cs`、`PartEntityFactory.cs`、`PartDrawer.cs`、`PartFreeTransformSystem.cs`、`PartSimulator.cs`、`SceneRenderSystem.cs`、`BuildToolController.cs`、`Bpsim_URP_Unlit_Texture_Instancing.shader`。
- BPIR 导入：新增 `Bpsim/Parts/SchematicsBpirCsvLoader.cs`、`Bpsim/QA/BpirImportQA.cs`、`Assets/Editor/BpirImportQaEditor.cs`、`Tools/unity-bpir-qa.bat`；修改 `SchematicsFormat.cs`（`BpirCsv = 5`）、`Schematics.cs`（格式分发）、`UI/SceneLoader.cs`（下拉第 5 项「BPIR 存档」）、`PartSimulator.cs`（`SeedWorldTransformJob` + 跨层规则）、`SchematicsCsvLoader.cs`（读取器容错：空行/注释/`43.0` 整数/BPIR 14 列指路）。
- QA 收尾：`Tools/unity-qa.bat`（四套件统一 batch 入口）、`Levels/LevelSession.cs`（`AutoRestore` 在 batch 下默认关闭）、`QA/FreeTransformQA.cs`（`csv 12 columns` → `csv 13 columns`，与 `LayerConnectionQA` 对齐）。

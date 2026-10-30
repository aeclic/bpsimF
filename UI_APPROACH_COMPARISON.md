# RPG MAKER U2U Demo vs BPSim：UI 制作方式对比

> 分析对象
> - **U2U**：`D:\Program Files (x86)\Steam\steamapps\common\RPGツクールU2U Demo`（RPG MAKER U2U「P2D 地图编辑器体验版」，Unity **6000.3.17f1**，IL2CPP 发行版）
> - **BPSim**：本仓库（Unity **2022.2.23f1c1**，反编译/重写源码工程）
>
> 结论基于对发行包内 `global-metadata.dat`、`level0`、`resources.assets`、`sharedassets0.assets`、`ScriptingAssemblies.json`、`StreamingAssets` 的取证，以及本仓库的 prefab / C# / Packages 静态分析；取证脚本见 `Tools/u2u-ui-probe/`（第 7 节）。

---

## 0. 一句话结论

| | U2U | BPSim |
|---|---|---|
| 做法 | **设计系统 + 声明式文档 + 代码后置**：Figma 设计令牌 → USS 类 → UXML 页面 → C# 自定义元素（MVVM）→ 运行时 `UIDocument` 装配 | **手工 prefab + 序列化引用**：Editor 里直接摆 uGUI 层级、拖字段引用、代码 `AddListener / Instantiate` |
| 技术栈 | Unity UI Toolkit（UIElements），界面全部由 USS/UXML + 自定义元素构建，未发现 uGUI 界面资产 | uGUI（一份 `UnityEngine.UI.dll` 插件），**0** 处 UI Toolkit 引用 |
| 布局 | USS flexbox + ScrollView/SplitView + 弹性容器 | RectTransform 绝对定位/锚点手摆；面板"侧栏↔浮层"靠运行时备份/恢复矩形 |
| 样式 | 约 2500 个 BEM 命名的 `u2u-*` 类 + 设计令牌 + 主题（TSS） | 颜色/尺寸写在 prefab 里 + 1 个圆角 shader |
| 规模 | ~105 个自定义元素、14+ 个 UXML 页面（另有约 40 个事件指令页）、382 个编辑器类 | 26 个 UI 脚本 / 4142 行；11 个 UI prefab |

两者**不是同一种 UI**（工具型编辑器 UI vs 游戏内 HUD/建造 UI），选型差异有合理性；但 BPSim 在**样式复用、本地化、布局弹性、诊断**上确实存在结构性欠账。

---

## 1. U2U 的 UI 生产体系（自上而下）

### 1.1 运行形态：空场景 + 运行时装配

`RPGU2U_Demo_Data/level0`（启动场景）里只有 4 个对象：

```
Main Camera / Directional Light / EventSystem / "P2D Standalone Host"
```

没有 Canvas、没有控件 prefab。整个编辑器界面由宿主脚本在运行时从 `Resources` 加载并构建；`EventSystem` 配 Input System（`DefaultInputActions` 里有 `UI/Click`、`UI/RightClick`、`UI/ScrollWheel` 等动作）。

### 1.2 资产结构：`Resources/FigmaUi/`

从 `global-metadata.dat` 提取到的 UI 资产名（节选）：

- **UXML 页面**（`Resources/FigmaUi/*.uxml`）：`U2uMainWindowWired`、`U2uEventEditorRev2Wired`、`U2uEventCommandTextWired`、`U2uDatabase{Actor/Class/Skill/Item/Weapon/Armor/Enemy/EnemyGroup/State/Animation/Term/Type/System1/System2/Tileset/CommonEvent}Wired`、`U2uCommandPicker`、`U2uObjectPicker`、`U2uSwitchPicker`、`U2uTilesetNameList`…
- **事件指令表单**：`U2uEcm*` 一族（`U2uEcmChoices`、`U2uEcmConditionalBranch`、`U2uEcmTransfer`、`U2uEcmPictureShow`、`U2uEcmShop`、`U2uEcmMoveRoute`、`U2uEcmPluginCommand`……），每个 RPG Maker 指令一张页面。
- **共享样式表**（6 张）：`U2uShared.uss`、`U2uComponents.uss`、`U2uDatabaseCommon.uss`、`U2uEventCommandModals.uss`、`U2uMainWindowWiring.uss`、`U2uTutorialGuide.uss`。
- **单控件专用样式表**：`P2dSpinnerField.uss`、`P2dDisclosureCaret.uss`、`P2dFocusRing.uss`、`P2dTextOverflow.uss`、`P2dHierMenuScroll.uss`、`P2dDialogBodyScroll.uss`、`P2dMapPreviewMenu.uss`、`P2dMapPreviewPerf.uss`、`P2dRuntimeColorPicker.uss`、`P2dV2InputCursor.uss` —— **控件自带皮肤**，谁用谁 `EnsureStyles(root)` 载入。
- **图标**：`FigmaUi/Icons/*.png`（`U2uChevronDown`、`U2uScrollArrow{Up,Down,Left,Right}`、`u2u_icon_parttype_*`、`u2u_icon_pen` ……），带图标回退逻辑（`FigmaIconOrFallback` / `FigmaIconFallbackWarned`）。

`resources.assets` 里能数到**约 2500 个 `u2u-*` 类名**，BEM 风格（296 个 `__element`、230 个 `--modifier`），并按域分前缀（因二进制里相邻字符串会粘连，以下数字为粗分近似）：

| 前缀 | 含义 | 数量（近似） |
|---|---|---|
| `u2u-cmp-` | 通用控件库（accordion/checkbox/combobox/spinbox/split/statusbar…） | 381 |
| `u2u-mww-` | 主窗口壳（titlebar/menutop/maplist/objlist/objset/inspector/transgrp/picker/split…） | 131+ |
| `u2u-ecm-` | 事件指令模态 | 158 |
| `u2u-evr-` | 事件编辑器 | 108 |
| `u2u-db*` | 数据库各页（actor/state/skill/enemy/item/weapon/term/tileset/class/system…） | 数百 |

这套命名说明 UI 是**先有设计规范（Figma），再有 CSS 类，最后才有 C#**。

### 1.3 控件：`U2UE_*` 自定义元素 + 页面代码后置

元数据里有 **105 个 `U2U.P2D.Standalone.U2UE_P2d*` 类**，例如：

- 外壳/模态：`U2UE_P2dAppMenuBar`、`U2UE_P2dModalChrome`、`U2UE_P2dBusyOverlay`、`U2UE_P2dHoverTip`、`U2UE_P2dDialogTabNav`、`U2UE_P2dDialogEscClose`
- 数据/表格：`U2UE_P2dDbRowTable`、`U2UE_P2dObjectListRowSettingPopup`、`U2UE_P2dListKeyboardNav`、`U2UE_P2dSimpleMapPickerModal`
- 图形化：`U2UE_P2dCurveGraph`、`U2UE_P2dPieChart`、`U2UE_P2dLightGizmo`、`U2UE_P2dAnimationTimelineView`、`U2UE_P2dAnimationPreviewStage`、`U2UE_P2dRuntimeColorPicker`
- 专有皮肤：`U2UE_P2dMzDialogSkin`（用 `img/system/Window.png` 做 MZ 风格九宫格窗口）、`U2UE_P2dSpinnerField`、`U2UE_P2dDisclosureCaret`、`U2UE_P2dUnitSuffixLabel`
- 页面级：`U2UE_P2dEventEditorModal`、`U2UE_P2dCommandParamEditModal`、`U2UE_P2dEventCommandListPane`、`U2UE_P2dDatabaseEditorModal`、`U2UE_P2dTilesetSettingsPane`、`U2UE_P2dBattleTestModal`

页面侧的命名成对出现：**`U2uMainWindowWired.uxml` ↔ `U2uMainWindowWired` 类**、`U2uDatabaseTilesetWired.uxml ↔ U2uDatabaseTilesetWired`。这是典型的"UXML 只放结构、同名类做 Wiring（绑定命名元素 + 行为）"分工；`EnsureWired` 也出现在元数据里。控件用 Unity 6 的 `[UxmlElement]` / `UxmlSerializedData` 体系（资产里能看到 `Button/UxmlSerializedData` 这类节点）。

### 1.4 设计令牌与主题

- 令牌：`FigmaTokens` / `ApplyFigmaTokens`，常量名如 `FigmaRadiusSmall`、`FigmaComboHeight`、`FigmaFontSize`、`FigmaDialogBg`、`FigmaDialogContentBg`、`FigmaDialogRootBg`、`FigmaTableHeaderBg`、`FigmaTableBorder`、`FigmaFocusUnderline`、`FigmaSkin`、`FigmaStyles`、`FigmaResetClass`。
- 主题：`ThemeStyleSheet` + `com.unity.ui/.../Default.tss` 作为基线，`P2dEditorColorScheme` + `ApplyThemeStyleSheet` 做编辑器配色切换。
- 文本：`U2uPanelTextSettings`（`PanelTextSettings` 资产）+ `UIPackageResources/Fonts/NotInter/NotInter-Regular.ttf`，外加 `segoeui.ttf` / `seguiemj.ttf` / `seguisym.ttf` 回退（emoji / 符号）。

### 1.5 数据绑定：MVVM

`U2UE_P2dMapMVVM`、`U2UE_P2dMapMVVMBase`、`U2UE_P2dMapViewBase`、`P2dMapView`，以及 `NotifyMapSelected`、`notifyMvvm` 这类日志点——地图数据变化通过 MVVM 通知视图，而不是在控件里 `SetText`。

### 1.6 多窗口 / DPI / 输入法

这里最能体现"编辑器级"投入：

- 原生插件 `RPGU2U_Demo_Data/Plugins/x86_64/U2uMultiWindow.dll`（日志 `%TEMP%\U2uMultiWindow.log`），配合 `U2uWin32WindowMetrics`、`U2uSecondWindowUitkEventBridge`、`U2uSecondWindowKeyboard`、`P2dSecondWindowRtResizeGate`、`P2dSecondWindowDialogStack`、`P2dModelessSecondWindow`、`P2dDialogSecondWindow`、`IP2dSecondWindowDialog`。
- **每个 OS 窗口拥有一套自己的 UI Toolkit 面板/RenderTexture**，标题栏也是 UI Toolkit 画的（`u2u-mww-titlebar-btn`、`--hover`、`--pressed`、`--close`、`maxicon-back/front`、`u2u-mww-titlebar-drag`），并处理跨窗口的下拉弹层（`U2uSecondWindowDropdownPopup/Placement`）与 IME 组合串（`U2uSecondWindowImeCaret`、`[U2uMultiWindow] CancelImeComposition`）。
- 缩放：`P2dPanelScaleMode { ConstantPixelSize, ConstantPhysicalSize, ScaleWithScreenSize }`、`P2dPanelScaleDiagnostics`、`RegisterMainPanelDpiScaleFollow`、`ApplyMainPanelDpi…`，日志形如 `panelSettings: scaleMode=…`、`[P2dPanelScale] …`——**面板缩放是自己算的**，不是简单 `CanvasScaler`。

### 1.7 本地化

- 服务/表：`P2dEditorLocale`、`P2dEditorLocaleBlob`、`P2dL10nService`、`P2dL10nUiTable`、`P2dL10nProject`、`P2dL10nIdRegistry`、`P2dL10nColumns`、`P2dL10nGlossary*`、`P2dBundledL10n(+Bake)`，资产在 `Resources/Locale/`。
- 字段级绑定：`P2dL10nField` + `P2dL10nFieldBinding` + `P2dL10nField.uss`（输入框旁的本地化编辑按钮）。
- 源头是 **Excel**：`P2dL10nXlsx`（`Sheet`/`Cell`）、`P2dL10nImporter/Exporter`、`P2dL10nJsonSplice`。
- 交付：手册 `html/` 有 12 个语言目录（de/en/es/fr/it/ja/ko/pl/pt/ru/zh_CN/zh_TW）；教程 `StreamingAssets/U2U/Tutorial/*.json` 每条文案内嵌 `en_US/de_DE/es_ES/fr_FR/it_IT/ko_KR/pl_PL/pt_BR/...`。

### 1.8 交互细节与动效

- 无障碍/键盘：焦点环 `P2dFocusRing(.uss)`、`P2dDialogTabNav`、`P2dDialogEscClose`、`P2dDialogInitialFocus`、`P2dListKeyboardNav`、层级菜单 `P2dHierarchicalMenu`、悬停提示 `U2UE_P2dHoverTip`。
- 动效：`LitMotion` + `LitMotion.Extensions`（`TextMeshProMotionAnimator`）。

### 1.9 生产线全景

```
Figma 设计（令牌/组件/图标）
        │  导出
        ▼
U2uShared/Components/DatabaseCommon/EventCommandModals .uss      ← 约 2500 个 u2u-* 类（BEM）
U2u*Wired.uxml / U2uEcm*.uxml                                   ← 页面结构（≈14 + 40）
        │  代码后置
        ▼
U2UE_P2d* 自定义元素（105）+ XXWired 类（绑定命名元素/行为）+ MVVM
        │  运行时
        ▼
PanelSettings(P2DMapEditorPanelSettings) ─┬─ UIDocument（主窗口）
                                          ├─ UIDocument（第二窗口/RT）
                                          └─ UIDocument（预览/调试覆盖层）
        ▲
        │ P2dL10nService / P2dL10nField（XLSX → 表 → 字段）
```

---

## 2. BPSim 的 UI 生产体系

### 2.1 运行形态：单场景 + prefab 装配

`Assets/Scenes/MainScene.unity` 只有 `SceneCamera` 和 `CoreManager`；`CoreManager.m_prefabList` 列了 5 个 prefab：

```
StartPage / SpriteManager / PartMananger / UserInterface / SimulationInterface
```

`CoreManager.Awake()` 逐个 `Instantiate`。界面全部来自 prefab，场景里没有布局。

### 2.2 资产与布局

- `Assets/GameObject/UserInterface.prefab`：**69 个 RectTransform / 71 个 GameObject / 105 个 MonoBehaviour**；两个 Canvas 都是 `ScreenSpaceOverlay`（`m_RenderMode: 0`，`sortingOrder` 0 与 1，`pixelPerfect: 1`），`CanvasScaler` 统一 `ScaleWithScreenSize`、`1920×1080`、`matchWidthOrHeight = 1`（按高度匹配）。
- 产品 UI prefab 共 11 个：`UserInterface`、`StartPage`、`SimulationInterface`、`PartSelectionPanel`、`PartButtonList`、`PartDrawingPanel`、`PartPropertyPanel`、`CustomPartSelector`、`UserSettingsPanel`、`ScreenshotPanel`、`SceneLoader`（`DebugUI*` 是调试面板，不算产品 UI）。
- 图片：`UserInterface.prefab` 里 19 个 `m_Sprite` 引用中 **18 个是九宫格**（`m_Type: 1`），只有 1 个简单图。
- 字体：`SourceHanSansCN-Regular/Bold`、`FrutigerNeueLTW1G-Regular/Bold`、`Consolas`、`PerfectDOSVGA437`；文本用 **legacy `Text`**（`m_Font` 直接指向字体资产，中文标签用思源黑体，拉丁用 Frutiger）。

### 2.3 布局：手摆 + 运行时搬运

- 整份 `UserInterface.prefab` 里只有 **3 个 LayoutGroup + 2 个 ContentSizeFitter**，其余全是锚点 + `sizeDelta` 的绝对定位。
- "面板停靠侧栏 ↔ 浮层"不是两套布局，而是 `SwitchableInterface` 在 `Awake` 时把面板和子 RectTransform 的 `anchoredPosition/sizeDelta` 备份进 `Dictionary<RectTransform, UITransformValue>`，切换时 `Move(size)` / `Restore()` 逐个回写（见 `Assets/Scripts/Assembly-CSharp/Bpsim/UI/SwitchableInterface.cs`）。

### 2.4 接线方式

```csharp
[SerializeField] private Button m_loadButton;
...
m_loadButton.onClick.AddListener(LoadScene);
...
m_gameObject.transform.Find("Text").GetComponent<Text>().text = m_counter.FPS.ToString();
```

- 面板：`Instantiate(m_settingsPanelPrefab).transform.SetParent(m_root.SubCanvas.transform, false)`。
- 文本：多处用 `transform.Find("Text")` 字符串路径取（`UserInterface.ActionBarView.Update`、`PartDrawingPanel.Update`、`PartButton.Awake` 里的 `transform.Find("PartTexture")` / `Find("Index")`）。

### 2.5 自绘与性能取向

- **圆角/圆形**：`Assets/Shader/Bpsim_RoundedRectangle.shader` + `Bpsim.UI.RoundedRectangle` 组件，运行时 `Shader.Find("Bpsim/RoundedRectangle")` 并 **每个元素 `new Material`**（见 README 截图里右上角的圆形 X/^ 按钮）。
- **自定义可交互控件**：`UIButton : Button`（暴露 `PointerDown/Up/Enter/Exit/Click/Drag` 与状态事件）、`ToggleSwitch : Selectable`（`Image` 三段拼装 + UniTask 逐帧补间）、`PartSliderButton`、`PartTriggerButton`。
- **图集直出**：不用 `Sprite` 而是 `RawImage` + `uvRect`——`Rendering/SpriteUtility.PopulateRawImage`、`UISprite`、`PartButton.SetSprite(enabled, texture, uvRect, scale, rotation)`；部件图标来自部件渲染管线的一张图集，允许每按钮独立旋转/缩放/着色（`Initialize()` 里按 `ComponentRank` 算 HSV 色）。
- **3D→UI**：`CanvasConverter` 把带 `MeshRenderer/MeshFilter` 的 3D 对象就地改成 `RectTransform + CanvasRenderer`，让 3D 部件直接进 Canvas（当前脚本内无调用点，属预留/遗留）。

### 2.6 绑定：自制迷你 MVVM（仅属性面板）

`Bpsim/ComponentModel/`：`ObservableObject`、`DependencyObject`、`DependencyProperty<T>`、`DependencyProxy<T>`、`Binding` 扩展（`Toggle/ToggleSwitch/Dropdown`）。用法：

```csharp
m_viewModel = new DependencyProxy<PartDrawingSettings>(settings);
m_brushSizeX.Bind(m_viewModel.GetProperty<Vector2>("BrushSize"), Parser<float>.Default,
                  v => v.x, (v, x) => v.WithX(x));
```

属性以**字符串名**寻址（`"BrushSize"`、`"HighPartTypePriority"`），没有编译期校验；事件按钮仍走 `onClick.AddListener`。

### 2.7 缺失项（相对 U2U）

| 能力 | BPSim 现状 |
|---|---|
| 样式表/令牌 | 无；颜色/字号/圆角散落在 prefab 与代码（`PartButton.Initialize` 里的 HSV 公式） |
| 主题/换肤 | 无 |
| 本地化 | 无 i18n 框架；prefab 里硬编码中英混杂：`关闭`、`截图`、`取消`、`重置`、`部件类型与材质`、`加载场景`、`文件路径`、`浏览`、`旋转朝向`，同时留 `New Text`、`INPUT`、`OPTION` 占位 |
| 键盘/焦点 | 无焦点系统、无 Tab 导航（纯鼠标） |
| UI 诊断/测试 | 无 `PanelScaleDiagnostics` 之类；UI 改动无自动验证 |
| 文本系统 | legacy `Text`（非 TMP；工程里 `TMPro` 引用数为 0） |

---

## 3. 逐项对比

| 维度 | U2U | BPSim |
|---|---|---|
| UI 框架 | **UI Toolkit**（`UIDocument` + `PanelSettings` + USS/UXML） | **uGUI**（`UnityEngine.UI.dll` 插件，prefab 层级） |
| 引擎/构建 | Unity 6000.3.17f1，IL2CPP 发行包 | Unity 2022.2.23f1c1，源码工程 |
| UI 存放 | `Resources/FigmaUi/` 下的 UXML/USS/PNG，运行时加载 | `.prefab`（YAML）里烘焙的层级与属性 |
| 布局模型 | USS flexbox、`ScrollView`、分割面板、弹性容器 | RectTransform 绝对定位 + 锚点；仅 3 LayoutGroup |
| 响应式 | 面板缩放 `P2dPanelScaleMode` + DPI 跟随 + 每窗口逻辑尺寸 | `CanvasScaler` 1920×1080 按高度匹配 |
| 样式 | 约 2500 个 BEM 类 + `FigmaTokens` 令牌 + TSS 主题 | prefab 内联值；1 个圆角 shader 承担"形状" |
| 控件 | 105 个 `U2UE_*` 自定义元素（表格/图表/时间轴/取色器/九宫格皮肤…） | 4–5 个 `Button/Selectable` 子类 + legacy `InputField/Dropdown` |
| 页面组织 | UXML 结构 + `XXWired` 代码后置（每页一对） | MonoBehaviours + `[SerializeField]` 引用，无页面模板概念 |
| 数据绑定 | MVVM（`P2dMapMVVM*`、`P2dMapViewBase`） | 字符串键迷你 MVVM（仅属性面板）+ 手写回调 |
| 文本/字体 | TextCore + `PanelTextSettings` + NotInter + Segoe 回退 | legacy `Text` + 思源黑体/Frutiger，按 Text 逐个指定 |
| 本地化 | XLSX→L10n 表→字段绑定；12 语言；教程 JSON 内嵌多语言 | 无；字符串硬编码在 prefab |
| 多窗口 | 原生多窗口插件，每窗口独立 UIDocument/RT，自定义标题栏、IME 桥、对话框栈 | 单窗口；主 Canvas + SubCanvas（只有 sortingOrder 分层） |
| 交互完备度 | 焦点环、Tab 导航、ESC 关闭、列表键盘导航、悬停提示 | 鼠标为主 |
| 动效 | LitMotion | UniTask 逐帧补间（`PlayAlphaAnimation` 等） |
| 规模 | ~382 个 Standalone 类 / 105 元素 / 14+ UXML / 约 2500 类名 | 26 脚本 / 4142 行（占全工程 19430 行的 21%）；11 个 UI prefab |
| 诊断/审计 | `P2dPanelScaleDiagnostics`、各类 Audit/Report | 无 |
| 迭代方式 | 改 USS 只动样式；改 UXML 不动 C#；令牌全局生效 | 改 prefab 要在 Editor 手调；字段引用/`Find("Text")` 易断 |

---

## 4. 为什么两边会走得这么远

1. **产品形态不同**。U2U 是"RPG Maker 编辑器"：数据库 20 页表单、40+ 事件指令参数页、多窗口、12 语言、可换肤。这是典型的**内容密集型工具 UI**，UI Toolkit 的 CSS 式布局/主题化/可复用元素正好对症。BPSim 是游戏内 UI：一条动作栏 + 部件列表 + 若干属性面板，元素量级差一到两个数量级。
2. **美术管线不同**。U2U 有 Figma 设计源（`FigmaUi`、`FigmaTokens`、`FigmaSkin`），UI 是先设计再实现；BPSim 的 UI 是**复刻原版 Bad Piggies 的视觉**，直接吃反编译出来的贴图/字体，设计-实现是同一个人在同一份 prefab 里完成。
3. **性能取向不同**。BPSim 的目标是 7000+ 部件仍流畅（README、`PartGroup_6_MetalFrame.prefab` 的网格管线），它把"少 GameObject、复用 3D 网格、图集 UV 直出、单 shader 画圆角"当成原则；UI Toolkit 的文本/图元重建与布局计算在这种项目里需要额外 profile，而它的 UI 元素本来就不多，uGUI 更省心。
4. **引擎与工具成熟度**。U2U 在 Unity 6，能用 `[UxmlElement]` / `UxmlSerializedData` / VectorImage / 新版 UI Builder；Unity 2022.2 的运行时 UI Toolkit 可用，但工具链与生态明显弱，迁移 ROI 低。
5. **成本结构不同**。U2U 的 UI 投入是"平台级"的（多窗口原生插件、IME、DPI、L10n 管线）；BPSim 的 4k 行 UI 代码如果按同样标准重构，收益不抵成本。

---

## 5. 如果 BPSim 要借鉴（按性价比排序）

1. **加一层设计令牌**（半天级）。新建 `UITheme : ScriptableObject`（颜色/字号/圆角/间距/九宫格边距），把 `PartButton.Initialize` 里的 HSV 公式、prefab 里散落的颜色常量收敛进去。这一步不需要动框架，最直接解决"配色/尺寸散落"。
2. **收口图片与材质**（1–2 天）。`UISprite`/`SpriteUtility` 已经在做"图集 + UV 矩形"，可以再抽 `UIAtlas` 资源与九宫格参数；`RoundedRectangle` 目前每元素 `new Material`，改成共享材质 + `MaterialPropertyBlock`（或每实例共用一份按 w/h/r 排序的缓存）可省掉一批材质实例与绘制批次。
3. **把脆弱引用换成强引用**（1 天）。`transform.Find("Text")` / `Find("PartTexture")` / `Find("Index")` 改为 `[SerializeField]` 字段或一次缓存；`DependencyProxy` 的字符串属性名可加成 `nameof` 常量或用生成代码，避免改名后静默失效。
4. **补最小 i18n**（按需）。当前 prefab 里中英混杂（`关闭` 与 `New Text` 并存）。最小方案：文本改 key + 语言表（JSON/ScriptableObject）+ 一个 `LocalizedText` 组件；同时保留 `SourceHanSansCN` 作 CJK 字体。
5. **布局弹性**（按需）。`SwitchableInterface` 的"备份/恢复 RectTransform"可以保留，但新面板建议直接用 `LayoutGroup`/`ContentSizeFitter`，否则每加一种停靠状态就要再写一份矩形表。
6. **不建议全量迁 UI Toolkit**。若确实想要 UITK 的样式能力，可**混合试点**：新做/重度表单类面板（如 `PartDrawingPanel`、将来的关卡编辑器）用 `UIDocument` + PanelSettings，游戏内 HUD 保持 uGUI；Unity 2022.2 支持运行时 UITK，两套可以共存于同一 Canvas 之上（注意排序与输入互斥）。

---

## 6. 边界与不确定项

- U2U 的**玩家运行时 UI**（战斗/消息窗口 `P2dBattle*`、`P2dMessageWindowRuntime` 等）是否同样全部走 UI Toolkit，仅凭体验版无法完全确证；能确认的是编辑器/独立版（`U2U.P2D.Standalone`）是 UITK，且带 `U2UE_P2dBattleTestModal`、`U2UE_P2dMzDialogSkin` 这类 UITK 实现。
- `Wired` 后缀是否由代码生成器产出无法确证；证据只支持"UXML 页面 ↔ 同名代码类"的代码后置关系（`EnsureWired`、成对命名）。
- 未启动 U2U Demo 做实机截图；视觉结论来自手册截图（`html/ja/images/`）与二进制取证。

---

## 7. 复现（脚本在 `Tools/u2u-ui-probe/`）

```bash
PY="C:/Users/aecli/.dsh/dsh-runtimes/dsh-primary-runtime/dependencies/python/python.exe"

# UI 技术栈：PanelSettings / UIDocument / UXML / USS / VisualElement
"$PY" Tools/u2u-ui-probe/_u2u_ui_probe.py

# USS 类名规模与 BEM 统计
"$PY" Tools/u2u-ui-probe/_u2u_ui_probe4.py

# 自定义元素清单（U2UE_P2d*）、主题、L10n、多窗口
"$PY" Tools/u2u-ui-probe/_u2u_ui_probe5.py
"$PY" Tools/u2u-ui-probe/_u2u_ui_probe7.py

# UXML/USS/图标资产名
"$PY" Tools/u2u-ui-probe/_u2u_ui_probe11.py

# 资产内字符串（u2u-* 类、字体、图标）
"$PY" Tools/u2u-ui-probe/_u2u_assets_probe2.py

# 教程 JSON 的本地化结构
"$PY" Tools/u2u-ui-probe/_u2u_ui_probe9.py   # 命名空间确认
```

BPSim 侧可直接检索：

```bash
grep -c "RectTransform\|m_UiScaleMode" Assets/GameObject/UserInterface.prefab
grep -rn "UnityEngine.UIElements\|UIDocument" Assets/Scripts | wc -l   # 0
grep -rn "TMPro" Assets/Scripts | wc -l                                # 0
grep -rln "LayoutGroup" Assets/GameObject/*.prefab                     # 很少
```

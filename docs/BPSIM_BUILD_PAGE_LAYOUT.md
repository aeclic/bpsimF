# 建造页布局改造：底部场景选项卡 + 部件列表内嵌图层条

按用户三轮要求逐步收敛，最终形态：

1. **「加载场景」选项卡移到底部**，水平范围与画布对齐（第一轮，参考图 PaintTool SAI Ver.2）；
2. **图层面板并进左侧部件列表内部**，夹在左侧工具栏与部件网格之间，宽度与部件列表相同，层按钮正方形向右堆叠、长按可左右拖动（第二轮）；
3. **部件列表顶到工具栏下沿（零间隙）**，层按钮改圆角并略缩小、按钮间距不变（第三轮）。

改前：`SceneTabView` 在顶部 ActionBar 下方横跨整屏（y −90..−150），`LeftSidebar` 从 −150 起满高 930，部件列表 840 高；图层面板是右上角 52² 图标按钮 + 下拉 228 宽菜单（`LayerPanelUI`，运行时自建 Overlay Canvas 浮窗）。

## 一、prefab 几何（`Assets/GameObject/UserInterface.prefab`）

主 Canvas 为 1920x1080、`matchWidthOrHeight = 1`，下列数值都是参考分辨率下的 uGUI rect。

| 节点 | fileID | anchors | anchoredPosition | sizeDelta | pivot |
| --- | --- | --- | --- | --- | --- |
| `LeftSidebar` | 224184140609576627 | (0,1)-(0,1) | (0, −90) | 450 × 990 | (0,1) |
| ├ `ActionBar`（左侧工具栏） | 224004936310361357 | (0,1)-(1,1) | (0, 0) | 0 × 90 | (0,1) |
| └ `PartSelector` | 224853914702154834 | (0,1)-(1,1) | (0, −90) | 0 × 900 | (0,1) |
| `SceneTabView` | 224534948657868948 | (0,0)-(1,0) | (0, 8) | −900 × 60 | (0.5,0) |
| `SceneScreen` | 224566718759428703 | (0,0)-(1,1) | (450, −150) | −450 × −230 | (0,1) |
| `RightSidebar` | 224266788721764480 | (1,1)-(1,1) | (0, −150) | 450 × 930 | (1,1) |

要点：

- `LeftSidebar` 的 `anchoredPosition.y` 由 −150 → **−90**、高度 930 → **990**：顶到顶部 ActionBar 下沿，底到 Panel 底部（−1080），中间不留空档。
- `PartSelector` 的 `anchoredPosition.y` 保持 −90（正好在左侧工具栏 90 高之下）、高度 840 → **900**（= 990 − 90），与工具栏**零间隙**、铺满剩余高度。
- `SceneTabView` 从「顶部横跨整屏」改成「底部锚定」：`anchorMin (0,0) / anchorMax (1,0)`、`pivot (0.5,0)`、`anchoredPosition (0,8)`、`sizeDelta (−900, 60)` —— 左右各留 450（`LeftSidebar` / `RightSidebar` 的宽度），tab 条左右边缘与画布（`SceneScreen` 的 x 450..W−450）完全对齐。
- `SceneScreen` 的 `sizeDelta.y` 由 −150 → **−230**（底部再多让 80 = tab 条 60 + 上下留白 20），避免画布内容被 tab 条压住。
- `RightSidebar` 不动。
- Panel 子物体顺序保持不变（`ActionBar` 0、`SceneTabView` 1、`SceneScreen` 2、`LeftSidebar` 3、`RightSidebar` 4）：`BuildToolbarQA` 断言「hint 的 sibling index > SceneTabView」，顺序不变即通过（实测 `hintIndex=5 tabIndex=1`）。
- 注意区分两颗 ActionBar：`UserInterface.ActionBar`（BuildToolbar 用的那颗，父物体是 Panel，装 Load/Save/Screenshot/Identify/Settings/Simulation）与 `LeftSidebar/ActionBar`（左侧 4 个工具按钮 + 运行时追加的 6 个构建按钮 + 放大镜）。本次只动后者所在的容器。

## 二、`LayerPanelUI` 重写（`Assets/Scripts/Assembly-CSharp/Bpsim/UI/LayerPanelUI.cs`）

v1 是「右上角 52² 图标按钮 + 下拉 228 宽菜单」→ v2 是「左上独立浮窗」→ **v3 是「部件列表内部顶端的横向层条」**（当前版本，约 700 行）。

- **不再自建 Canvas**：`ResolveSidebar()` 取 `UserInterface.Instance.PartSelector.transform.parent`（= `LeftSidebar`），在其中把 `PartSelector` 的 rect 归位为「工具栏下沿、铺满剩余高度」，再把 `LayerStrip` 挂到 `PartSelector` 下面（`SetSiblingIndex(childCount-1)` 压在 `Scroll View` 之上），最后把 `Scroll View` 的 `offsetMax.y` 设为 `-StripHeight`，让部件网格让出这条带子。
- 尺寸常量：`StripHeight = 92`、`SquareSize = 76`、`SquareSpacing = 8`、`StripPadding = 8`、`SmallSize = 48`、`CornerRadius = 12`、`RefreshInterval = 0.2`。
- 配色抄 prefab 原生控件：`s_stripColor (0.145,0.145,0.145,1)`（= 侧栏工具栏底灰 37）、`s_buttonColor (0.35,0.35,0.35,1)`、`s_smallColor (0.26,0.26,0.26,1)`、`s_activeColor (0, 0.19607843, 0.4, 1)`（= LoadButton 那颗深蓝 Image）、`s_textColor (0.93,0.93,0.93,1)`、`s_mutedColor (0.72,0.75,0.8,1)`。
- 每层一个 **正方形**（`SquareSize`）格子，横向排列：底色 `Image s_buttonColor` + `Foreground`（`s_activeColor`，选中时 enable）+ `Button` + 中央数字 + 下方 12pt 计数（「N 件」/「空」）；`Layer 0` 是「全部」格。末尾 `＋`/`－` 两个 48² 小按钮调 `AddLayer`/`RemoveLayer`。
- **圆角**：`ApplyRounded(Image)` + `GetRoundedSprite()` 运行时生成 24×24 RGBA32 圆角贴图（`Sprite.Create` 带 `border = (12,12,12,12)` + `SpriteMeshType.FullRect`，`Image.type = Sliced`），逐像素按到最近圆角圆心的距离做 1–2px 抗锯齿边；`hideFlags = HideAndDontSave`，静态缓存一份。
- **长按左右拖动**：`LayerStripDrag`（`IBeginDragHandler/IDragHandler/IEndDragHandler`）挂在 `LayerStrip` 上，拖的是 `Content.anchoredPosition.x`，夹到 `[-overflow, 0]`；`overflow <= 0.5` 时不启动；`OnEndDrag` 置 `m_suppressClick`，按钮 `onClick` 见到该标记就忽略（避免「拖动顺手切了层」），`LateUpdate` 清标记。
- 层状态仍全部读 `scene.Unmanaged`（`ActiveLayer` / `LayerCount` / `CountPartsOnLayer`），UI 不持有副本；`Refresh(force)` 用 signature `activeLayer|layerCount|每层计数` 做增量，只有 `layerCount` 变了才重建格子。
- 保留公共 API：`Initialize(BuildToolController)`、`SetPanelVisible(bool)`（`LayerConnectionQA.ShowLayerPanel/HideLayerPanel` 依赖）；仍由 `BuildToolController.Start()` 用 `AddComponent<LayerPanelUI>().Initialize(this)` 挂在同一 GameObject 上（`layer panel installed` 断言依赖）。

## 三、默认层数对齐 BPIR（6 层）

- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartBuildLayer.cs` 新增 `public const byte DefaultLayerCount = 6;`。依据：BPIR 的 `LayerPanelUI.GetMaxLayer()` 是 `Mathf.Clamp(Mathf.Min(INSettings.GetInt(INFeature.LayerCount), 6), 1, 6)`，而 `INDeclarationSettingsExp`（从 `BPIR 10.4.1/.../resources.assets` 用 UnityPy 解出的 TextAsset）里 `LayerCount` 的 `value` 就是 **6**；`MultiLayerBuild` / `LayerSelfCollision` / `DimInactiveLayers` 均为 True。
- `PartSceneUnmanaged.Initialize` 里 `m_layerCount = 1` → `PartBuildLayer.DefaultLayerCount`；`m_activeLayer` 仍为 1。
- `PartBuildLayer.MaxLayer = 16` 保留为硬上限（旧存档里更高的层不受影响，`LayerConnectionQA` 的 `Clamp(17) == 16` 断言仍成立）。

## 四、验证

桥接 Play 模式（DshBridge，端口 **8791**）实拍：

- 截图方法：`invoke_static type=Bpsim.QA.BuildToolbarQA method=Capture args=["Logs/layout-final.png"]`。
  **注意**：桥接自带的 `method=screenshot` 只渲染相机画面，**不含 Screen Space Overlay 的 uGUI**，不能用它验 UI。
- 像素实测（1325x590，CanvasScaler match=1 → 缩放 590/1080 ≈ 0.546）：
  - 纵向 x=25：y 0..44 顶部工具栏；y 49..56 侧栏底色 51（= `LeftSidebar` 的 Image 0.2）；y 57..89 深蓝 (0,50,102) 工具按钮；y 90..97 底色 51；**y 98..143 图层条（底 37 = `s_stripColor`，内含 89 = `s_buttonColor`）**；y 144..148 底 37；y 149 起部件网格。→ 部件列表已顶到工具栏下沿，图层条是列表内部顶端的一条带，三者之间**零间隙**。
  - 横向 y=130：x 0..3 底 37、x 5..44 一个 40px 方块、x 46..49 4px 间隙、x 51..90 **深蓝 (0,50,102) = 选中层**、x 96..137 / 142..183 / 188..228 依次向右排列、x 235..245 被右边缘裁切（可拖动）。方块 40px ≈ 76 × 0.546 = 41.5，间距 4px ≈ 8 × 0.546。
  - 圆角：方块四角有 49/69/83/88 → 89 的 1–2px 渐变过渡，即 `ApplyRounded` 的 9-slice 圆角生效。

自动化回归：

| 探针 | 结果 |
| --- | --- |
| `Bpsim.QA.LayerConnectionQA.Run` | `PASS pass=27 fail=0`（含 `scene defaults \| active=1 count=6`、`layer panel installed`、`csv 13 columns`） |
| `Bpsim.QA.BuildUiInteractionQA.Run` | `PASS pass=31 fail=0` |
| `Bpsim.QA.BuildToolbarQA.Run` | `FAIL pass=114 fail=2` —— 两条都是既有 flaky，与布局无关，见下 |

`BuildToolbarQA` 的两条失败（历史上同一探针在别的会话里也出现过 `FAIL pass=104 fail=1` / `FAIL pass=110 fail=1`）：

- `FAIL grid mode cycles back to snap | mode=Snap gridEnabled=True gridless=False` —— 三态循环的第三跳要求 `gridObject.activeSelf`，而 `PartScene.SetGridEnabled()` 只在 `orthographicSize < 50` 时点亮 `m_grid`；本关（`1 Cave · 第 23 关`）的相机被拉到 ≥50，所以 `m_largeGrid` 亮、`m_grid` 不亮。断言盯的是 `m_grid`，与层条无关。
- `FAIL magnifier zooms out | near=5 overview=1.257` —— 探针先 `ScaleTo(max(MinSize, 5))`，而本关的部件包围盒只要求 `1.257` 的视高，`overview > near * 1.5` 自然不成立。`magnifier detects the sidebar | left=0.186`、`magnifier centers the build`、`magnifier restores zoom/point` 全 PASS，说明放大镜本身正常（`left=0.186` 正好是左侧栏 450/1920 ≈ 0.234 扣掉部分遮挡后的值）。
- 布局相关的断言全 PASS：`hint draws above scene tabs | hintIndex=5 tabIndex=1`、`toolbar buttons do not overlap | buttons=10`、`toolbar clears fps`、`file buttons relay out`。

`LayerConnectionQA` 覆盖了「layer panel installed」（`BuildToolController` 上挂了 `LayerPanelUI`）以及图层/连接/无网格的完整行为链，说明布局重写没有动到逻辑。

## 五、踩坑记录

- `LayerConnectionQA.Run` 报 `DontDestroyOnLoad can only be used in play mode`：说明编辑器当时**不在 Play 模式**（被手动停掉或超时退出）。先 `playmode action=enter` 再等场景加载完（日志出现 `[BpsimLevels] restore: ...`）才调探针。
- 桥接只能 `POST /api`（body `{"method":..., "params":{...}}`），直接 `POST /playmode` 会 404；端口是 **8791**（skill 默认 8790）。
- 进 Play 后 `changing` 会持续 60–90 秒（域重载 + 场景 restore），期间 `/status` 反复抖动；域重载窗口内桥接会 `ConnectionRefusedError [WinError 10061]`，脚本必须自带重试。`Capture` 在 `playing && changing` 时就能成功，不必等 settled。
- 编辑器被别的会话拿去打包时窗口标题变 `Building Player`、`/api` 报 `editor main thread stalled`；把窗口拉到前台（`ShowWindow` + `SetForegroundWindow`）即可恢复。
- 桥接 `open_scene` 在 Play 模式中会失败（`This cannot be used during play mode`），要先进/出 Play 再开场景。
- 本仓库多个目标文件（`LayerPanelUI.cs`、`UserInterface.prefab` 等）在 git 里是**未跟踪**状态，改动前务必自己备份（本次备份在 `/tmp/UserInterface.prefab.v{2,3}.bak`、`/tmp/LayerPanelUI.cs.v2.bak`）。
- `SchematicsCsvLoader` 被别的会话加了第 13 列 `FrameShape`，`LayerConnectionQA` 原来的 `csv 12 columns` 断言因此过期，已同步改成 `csv 13 columns`（读侧 `array.Length >= 13` 才解析，旧 12 列存档仍可读）。

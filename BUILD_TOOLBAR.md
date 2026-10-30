# bpsim 顶部建造工具栏（参考主项目 BPRC）

把主项目 BPRC 建造界面的**顶部工具栏**搬到 bpsim-main，并全部做成**图标按钮**：
骨架克隆 `UserInterface.prefab` 里 `ActionBar` 现有的按钮（保留 `Image` + `Button` + `ResourceBinder`
的既有装配），底板换成九宫格圆角、文字层换成白色图标，重新排布成
「文件操作 ｜ 建造工具 ｜ 网格 / 撤销 ｜ FPS」四组；部件属性继续用现有的
`PartPropertyPanel`（右侧栏 / 浮层那套），没有另做菜单。

## 工具栏按钮

按钮无文字：桌面端鼠标悬停、安卓端长按 0.5 秒，都会在工具栏下方弹一条提示（`Hint`）显示功能名。

| 按钮 | 图标 | 行为 |
| --- | --- | --- |
| 加载场景 / 保存场景 / 截图 / 设置 | `Icon_Load` / `Icon_Save` / `Icon_Screenshot` / `Icon_Settings`（齿轮） | 原有按钮，位置重排为等距，逻辑不变 |
| **模拟（播放 / 停止）** | `Icon_Simulate` → `Icon_Stop` | **已搬出工具栏**，见「Besiege 式 HUD」——左上角大播放键 |
| **平移部件** | `Icon_Move` | 按住部件拖动 → 按格整体搬运（容器件连同被包容件一起走）；**原地单击**（整段手势没移动过）→ 按部件步长旋转一次。原来「平移部件」「旋转部件」是两枚按钮，合并后靠「有没有拖动」区分，和快速建造里「短按旋转 / 长按拖动」是同一套手感 |
| **整车平移** | `Icon_MoveAll` | 拖动 → 整张蓝图按拖动格数平移；**原地单击** → 整张蓝图绕包围盒中心旋转 90°（位置与朝向一起变）。原来「整车平移」「整车旋转」是两枚按钮，同样合并 |
| **部件变换** | `Icon_FreeTransformPart` | 单击部件选中 → 拖红/绿箭头自由平移、拖圆环连续旋转、**拖四角/四边拉伸**（Shift 保持比例）。只写 `PartFreeTransform`，不动网格占用表。原「部件变换」与「自由变换」是两枚按钮，控制器里是同一套手势、唯一差别是缩放手柄画不画，所以手柄改常显、按钮合并 |
| **载具变换** | `Icon_TransformMachine` | 整车包围盒中心显示平移/旋转/等比缩放 gizmo，所有部件的自由偏移、连续角与缩放一起烘焙，不重建网格占用表 |
| **网格（三态）** | `Icon_Grid` / `Icon_Gridless`（随状态换图） | 单击循环：**显示+吸附 → 隐藏（仍吸附）→ 自由放置 → 显示+吸附**。原来「网格」「无网格」是两枚独立按钮，而网格可见性其实是 `gridEnabled && !gridlessEnabled` 合成的，两枚按钮能凑出「网格：开 + 无网格：开 + 屏幕上看不到网格」这种自相矛盾的状态 |
| **撤销** | `Icon_Undo` | 或 `Ctrl+Z`；24 步历史，见下 |
| **放大镜（概览镜头）** | `Icon_Magnify` | **不在顶栏**，排在左栏「光标 / 增加 / 减少 / 框选」右边第 5 位，见「左栏放大镜按钮」 |

合并掉的按钮及去向（这些按钮名不会再出现在 `ActionBar` 下，`BuildToolbarQA` 里有断言守着）：

| 已删除 | 去向 |
| --- | --- |
| `BuildAdjustButton`（部件调整） | **双击部件**已经能打开同一个 `PartPropertyPanel`（`PartOperator` 订阅 `SceneScreen.DoubleClicked`），按钮纯重复 |
| `BuildRotateButton`（旋转部件） | 并进 `BuildMoveButton` 的原地单击；属性面板自带的旋转按钮仍在 |
| `BuildRotateAllButton`（整车旋转） | 并进 `BuildMoveAllButton` 的原地单击 |
| `BuildFreeTransformPartButton`（自由变换） | 并进 `BuildTransformPartButton`，缩放手柄常显 |
| `BuildGridlessButton`（无网格） | 并进三态网格按钮 |

`BuildTool` 枚举里这些旧值**保留但不再对应按钮**（`Adjust/Rotate/RotateAll/FreeTransformPart`），
`BuildToolController.SetTool` 会先过一遍 `Normalize()` 再比对，旧脚本传旧值仍然落到合并后的行为上；
数值只追加不插队，存档与蓝图身份不变。

## 图标

图标是**程序生成**的白色单线字形，不是从 BPRC / U2U / Besiege 里抠出来的位图：
那三个项目的图标要么带各自的调色板和描边风格、要么是 3D 渲染或打包在 AssetBundle 里，
直接拿来会和 bpsim 现有按钮的圆角深蓝底板冲突，也带不进这个仓库的授权链。
生成脚本 `_codex_work/topbar/make_icons.py` 在 64×64 逻辑网格上按「Besiege 式扁平单线 +
bpsim 现有深蓝圆角底板」的风格画，4× 超采样后 LANCZOS 缩到 128×128 输出到
`Assets/Resources/BuildToolbar/Icon_*.png`（白字透明底，由 uGUI `Image` 上色，
所以激活 / 禁用态只需改 `color`）。改图标就改脚本重跑，再跑
`_codex_work/topbar/make_icon_metas.py` 补 `.meta`（Sprite、`alphaIsTransparency`、无 mipmap）。

设置按钮是**实心齿轮**（八颗梯形齿 + 中心轴孔的单片多边形），不是「圆环 + 八根短线」：
后者在 36px 下会糊成一个圆，读起来像时钟。齿轮用实心轮廓画，任何尺寸都不散。

`BuildToolbar.ApplyIconSkin` 在运行时给每个按钮：
`Image.sprite = PlateSprite`（九宫格圆角底板，见下）+ `Image.Type.Sliced`，隐藏原 `Text` 层，
再加一个 36×36 的 `Icon` 子物体（`preserveAspect`、`raycastTarget = false`）。

`PlateSprite` 不是 `Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd")`：
在这个工程里那句会稳定失败并往 Console 刷
`The resource UI/Skin/Background.psd could not be loaded from the resource file!`，
按钮底板就退化成没有 Sprite 的纯色方块（没有圆角）。改成 `BuildToolbar.FindPlateSprite()` ——
从 `Canvas` 下已有的 `Image` 里借第一张 `type == Sliced` 的 Sprite，也就是左边栏
`ViewButton/Background` 本来就在用的那张内置九宫格图。零新增美术资源，且和左边栏按钮同款。

> 底板不能用**静态字段初始化器**取：MonoBehaviour 的字段初始化器在 `AddComponent` 时执行，
> 那里调 `Resources.GetBuiltinResource` 会被 Unity 拒绝
> （`UnityException: GetBuiltinResource is not allowed to be called from a MonoBehaviour constructor`），
> 而且异常抛在 `AddComponent` 内部，后面的 `Initialize` 整个不会跑——症状是工具栏一个按钮都没有。
> 所以 `m_plateSprite` 是普通字段，在 `Initialize` 里赋值。

## 图标按钮的说明文字（悬停 / 长按）

图标按钮没有文字，所以补了一层提示条（`BuildToolbar.CreateHint/UpdateHint`）：

- **桌面**：每帧用 `RectTransformUtility.RectangleContainsScreenPoint` 判断
  `Mouse.current.position` 落在哪个按钮上（不走 EventSystem 射线，overlay canvas 下更快、
  也不依赖 UI 是否可交互），在工具栏下方弹一条深色圆角提示，文字就是按钮的功能名。
- **安卓 / 触屏**：没有悬停，改成「按住 0.5 秒显示」。越过阈值时把按钮临时置为
  `interactable = false` —— uGUI 的 `Button.OnPointerUp` 里 `IsActive()` 会检查 `interactable`，
  所以抬手不会顺手触发 onClick（长按只用来「看」，不会把工具打开或把一步撤销掉）；
  下一帧再恢复并 `Refresh()`。
- 提示条本体 `Image.raycastTarget` 与文字 `raycastTarget` 都是 false，不会挡住下面的按钮或场景。

> 提示条挂在 **`ActionBar` 的父物体 `Panel`** 上并 `SetAsLastSibling()`，不是挂在 `ActionBar` 里。
> uGUI 按层级顺序绘制，`ActionBar` 的整棵子树都排在 `Panel/SceneTabView` 之前，而提示条在
> y = -96 处正好压在场景标签条（-90 ~ -150）上——挂在 `ActionBar` 里会被标签条盖住。
> `Panel` 与 `ActionBar` 左上角重合（两者都顶到画布左上），所以按 `ActionBar` 坐标算出的
> `anchoredPosition` 在 `Panel` 坐标系里数值完全一致。
> `BuildToolbarQA` 的 `hint draws above scene tabs` 就是断言这个 sibling 顺序。

## 布局

工具栏自身是 `internal class BuildToolbar`，`UserInterface.Awake()` 里
`m_actionBar.GameObject.AddComponent<BuildToolbar>().Initialize(this, m_buildTool)` 挂上去。
按钮设计尺寸 64×64（左边栏工具按钮是 60，这里略大以容纳 36px 图标）、步进 72、
分组之间额外 24；文件按钮从 **x=428** 起排——左边整块让给了 HUD
（播放键中心 56、边长 76，占 18～94；紧跟其后是横向滑条 104～386）；
工具组与网格/撤销组各留一次分组间隔；FPS 文本右锚定在 `-48`（宽 88、给工具栏留 104 的余量）。

**窄窗口自动压缩**：`Initialize()` 排完一轮后调 `Reflow()`，按 `ActionBar.rect.width`
从 `k = 1` 往下试，直到「最后一颗按钮右缘 + 半宽」不再越过 FPS 文本（画布宽 − 104）。
`k` 同时压按钮边长、间距、分组空隙和图标尺寸（`icon = min(36, size × 0.5625)`）。
实测 1440 宽的游戏视图落到 `k ≈ 0.78`（边长 50、步进 56.16、工具栏右缘 1239 < FPS 左界 1348）；
1920 及以上是 `k = 1`，和设计值完全一致。`Initialize()` 时顶栏还是拉伸锚定、`rect.width`
可能为 0，`Update()` 里等宽度 ≥ 400 且与上次不同再收一次。
`BuildToolbarQA` 的 `toolbar clears fps` / `file buttons relay out` / 图标尺寸断言都读
`BuildToolbar.LastStep` / `LastButtonSize`，所以两种宽度下都成立。

配色：普通态底板沿用现有按钮的深蓝 `(0, 0.196875, 0.4)`、图标近白；激活态底板
`(0, 0.482, 0.722)`、图标纯白；不可用态底板 `(0.18, 0.18, 0.18)`、图标 35% 透明度。

## Besiege 式 HUD（播放键 + 抬头的横向滑条）

布局参照 Besiege 的建造界面：**左上角一个大号绿色播放键**，**它右侧紧挨着一条横向滑条**
（和 Besiege 的 `TimeScale` 一样是横的：细灰滑槽 + 白色圆旋钮 + 左侧模式图标 + 百分比），
建造时缩放镜头、模拟时调时间倍速（见下）。
两者都由 `BuildHud`（`Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildHud.cs`）创建，
并且**住在自己的 Canvas 上**（`BuildHudCanvas`，`sortingOrder = 5`）：

| 画布 | sortingOrder | 模拟时 |
| --- | --- | --- |
| 主画布（`UserInterface.m_canvas`） | 0 | 关掉 |
| 模拟界面（`SimulationInterface`） | 2 | 打开 |
| **HUD（`BuildHudCanvas`）** | **5** | **一直打开** |
| `SubCanvas`（模态面板） | 10 | 关掉 |

这就是「运行时也能显示」的实现方式：不是靠把主画布留着，而是让这两个控件不挂在主画布下。
`SubCanvas` 排在 HUD 之上（`UserInterface.Awake()` 里显式设成 10），所以打开场景加载 / 保存 /
设置 / 截图面板时，播放键不会被误触。

### 播放键

播放键**不是新建的**：它就是 `ActionBar` 上原来的「模拟」按钮（`ActionBarView.SimulateButton`），
`BuildHud.CreatePlayButton()` 把它 `SetParent` 到 HUD 画布、换九宫格底板、隐藏文字层、
加一个 `Icon` 子物体，然后重绑 `onClick`。

| 状态 | 位置 | 尺寸 | 底板 | 图标 | 提示 |
| --- | --- | --- | --- | --- | --- |
| 建造（有场景） | 左上角 `anchor(0,1)`，`(56, -45)` | 76×76 | 绿 `(0.11, 0.52, 0.23)` | `Icon_Simulate` 40px | 开始模拟 |
| 建造（无场景） | 同上 | 同上 | 灰 `(0.18, 0.18, 0.18)` + `interactable=false` | 同上 | 开始模拟 |
| 模拟中 | **原地不动** `anchor(0,1)`，`(56, -45)` | 76×76 | 红 `(0.62, 0.17, 0.16)` | `Icon_Stop` 40px | 停止模拟 |

和 Besiege 一样（`HUD > TopBar > Align (Top Left) > PlayButton`），播放键**在建造与模拟里
是同一颗、同一个位置、同一个尺寸**，模拟开始后只是底板转红、图标换成停止方块；这样
「运行时也显示」不用再算第二套坐标，也不会在切换瞬间跳一下。建造态 76 比工具栏按钮（64）
大一号，这是 Besiege 里播放键的观感。因为播放键 + 右侧滑条占掉了工具栏最左的整块
（0～386），`BuildToolbar.FileStartX` 从 56 挪到 **428**。

停止走 `SimulationInterface.EndSimulation()` 而不是直接 `PartManager.EndSimulation()`：
后者不会销毁模拟里的 `PartButtonList`，下一次模拟会叠出第二份。

### 抬头的横向滑条（建造 = 缩放，模拟 = 时间倍速）

同一条横向滑条在两个界面里干两件事，样式照 Besiege 的时间滑条
（`HUD > TopBar > Mover > Align (Top Left) > TimeScale`：一条细灰滑槽 + 一颗白色圆旋钮 +
时钟图标 + 青色百分比，没有填充条、没有底板）。实现在
`Assets/Scripts/Assembly-CSharp/Bpsim/UI/HudSlider.cs`。

| 界面 | 用途 | 左侧图标 | 百分比 | 写哪里 |
| --- | --- | --- | --- | --- |
| 建造 | 镜头缩放 | `Icon_ZoomIn`（右端 `Icon_ZoomOut`） | 隐藏 | `SceneCamera.ScaleTo()` |
| 模拟 | 时间倍速 0..200% | `Icon_Clock`（右端图标收起） | 显示（青色） | `Time.timeScale` |

- **缩放**：`value` 0 = 最远、1 = 最近，`size = min * (max/min)^(1-value)`。
  指数映射，线性映射会让前半段几乎看不出变化（1→500 的中点是 250，而几何中点是 22.4）。
  只写 `SceneCamera.ScaleTo()`，不碰相机内部状态，所以滑条与滚轮永远落在同一个区间
  （`MinSize = 1` .. `MaxSize = 500`，来自 `MainScene.unity` 的序列化值）。
- **双向同步**：滚轮缩放后 `BuildHud.Update()` 每帧 `SyncFromCamera()`，旋钮自己跟过去；
  拖动期间（`IsDragging`）不跟，否则会和手指打架。
- **倍速**：`0%` = 暂停、`100%` = 原速、`200%` = 两倍速，和 Besiege 的
  `delegateTimeScale = percentagey * 2f` 一致。写的是 `Time.timeScale`：ECS 的
  `UpdateWorldTimeSystem` 用 `Time.deltaTime` 推进世界时间、`FixedStepSimulationSystemGroup`
  的 `FixedRateCatchUpManager` 跟着它走，所以**物理步长不变、每秒步数变** ——
  慢放不会让物理变粗糙，这也是 Besiege 的做法（`TimeSlider.Update()` 直接写 `Time.timeScale`）。
  每次进模拟从 100% 开始，回建造界面时把 `Time.timeScale` 还原成 1（`SetMode` 里做，
  `OnDisable`/`OnDestroy` 再兜一次，避免退出模拟时把整局卡在暂停）。
- **镜头脚本改用 `Time.unscaledDeltaTime`**：`SceneCamera.Update/LateUpdate` 原本吃
  `Time.deltaTime`，倍速拉到 0（暂停）时 `0.5f / deltaTime` 会算出 `Infinity`，
  而且慢放时镜头会跟着变慢。缩放/平移是界面操作，不该受仿真倍速影响。
- 结构（尺寸按 Besiege 实测的比例）：`HudSlider` 热区 232×44（整条都能拖）→
  `ModeIcon` 22×22 + `Track`（160 长、6 厚细线）→ `Knob`（16px 白圆）+ `ZoomOut`（右端 22×22）；
  倍速模式把 `ZoomOut` 那一格换成青色的 `Value`（百分比文字），热区因此变宽到 254×44。
  Besiege 原版实测是 166 长 / 5 厚 / 14px 旋钮，这里同量级。
- 行内排版由 `HudSlider.LayoutRow()` 负责，**点左右对称的空隙**：只有缩放模式时
  `图标—12—滑槽—12—缩小图标`（两端再各留 2），倍速模式 `图标—12—200%—12—滑槽`。
  早先的固定坐标把百分比文字那一格一直留着，缩放模式下就变成「放大镜 —— 一大段空白 —— 滑槽」，
  左空隙 64、右空隙 10，观感是断开的；现在谁可见谁就占位，热区也跟着内容收尾。
- 拖拽换算用 `local.x - track.rect.xMin`：滑槽 pivot 在左边缘，竖版时代那个
  `local.x + width / 2` 的写法会把左半段全判成 50% 以上（点最左端直接跳到中速），横向化后必须去掉。
- 位置：**建造与模拟都是同一处**——播放键右侧 `(104, -45)`：播放键占 18～94，
  滑条从 104 起（10px 空隙）、到 358 止（254 宽，倍速模式最宽），和播放键同一水平线，
  与 Besiege 的「播放键 + 时间滑条同一行」一致。它落在顶栏下方、
  左侧栏（`LeftSidebar`，x 0～450）之上，所以不与部件网格冲突。

### HUD 自己的提示层

播放键搬走之后，`BuildToolbar` 的悬停命中只在 `ActionBar` 子树里找，够不到它，
所以 HUD 自带一套同款提示（`BuildHud.UpdateHint/UpdateTouchLongPress`，长按阈值同为 0.5 秒，
长按播放键时同样临时 `interactable = false` 以吞掉抬起时的 onClick）。
两套提示条样式一致，只是各管各的控件。



顶部工具栏是**另一套模式**（`BuildTool`），和左栏的 View / Place / Erase / Select 互斥：

- 工具激活时调 `PartOperator.SetNeutral()` —— 往 ToggleGroup 里塞一个隐形 Toggle 并点亮，
  把左栏四个按钮全灭。必须这么做是因为 `ToggleGroup.m_AllowSwitchOff == 0`，
  否则永远有一个 Toggle 亮着，`PartOperator.Mode` 会一直是 `Place`/`Erase` 抢指针。
- `Mode` 变成 `None` 之后 `SceneCamera.CanMove()` 返回 false（它的判断是
  `Mode != View` 时只在模拟中允许平移），于是拖部件时镜头不会跟着跑。
- 工具取消（再点一次同一个按钮）时 `SetMode(PartOperationMode.View)`，回到原样。

## 按钮状态刷新

「网格 开/关」和「撤销」两枚按钮的可读状态取决于**当前场景**，而工具栏是在
`UserInterface.Awake()` 里挂的，那时还没有场景。如果只靠 `StateChanged` 事件，
初始化后图标会一直停在「关 / 不可用」。

`BuildToolController.SyncVisibleState()` 每帧比对 `GridEnabled` / `CanUndo` 两个可见量，
只在真的变化时广播一次（不会每帧重建 UI），所以场景加载完、撤销栈增减时按钮都会跟上。
网格按钮用图标透明度表示开关（关 → 45%），撤销按钮不可用时底板变灰、`interactable = false`，
两者的悬停提示文字也跟着改成「网格：开/关」。

## 撤销

`BuildUndoHistory`（`MaxDepth = 24`，和 BPRC 一致）存的是**整场快照**：一张
`Schematics.Unit[]`（`X, Y, Type, Index, Rotation, Flipped`）。

选择快照而不是逐条反向操作，是因为 bpsim 的场景本质就是这张表，
`PartGridInfo.Occupied`、大件占格、容器/被包容关系、四邻附件渲染开关
（`AlignAttachments`）全都可以由「清空 + 重新放置」正确重建；
逐条反向操作则要自己维护这些隐式状态，很容易在「移走一个容器件」这类情况下留下脏格。

回放的「清空」是 `PartSceneUnmanaged.ClearParts()`，它**按实体销毁**而不是按格删除：
早期写法是对每个部件按自己的 `PartTransform` 坐标调 `RemovePartAt`，遇到两个部件共用同一格
（叠加放置、或搬运后表项与实体对不上）时第二个部件找不到表项，会留在 `m_parts` 里变成幽灵部件
（撤销后场景越撤越多）。现在直接把 `m_parts` 里的实体全塞进待销毁列表再 `m_parts.Clear()`，
不再依赖网格表。

记录点：

- 平移/旋转/整车变换：在**第一次真正改动之前**抓一张（`Record` + `m_recorded` 去重）。
- 放置/擦除笔刷：`PartDrawer` 只在「上一帧也按住」时才动手，所以
  `BuildToolController.TrackDrawingUndo` 用同条件在那一帧先抓快照，一次拖动只记一步。
  这也是 `BuildToolController` 标 `[DefaultExecutionOrder(-50)]` 的原因——
  它必须比 `PartManager.Update()`（真正跑放置 job 的地方）更早执行。
- 撤销一次拖动中「取消工具」会把本次手势已做的改动回退（`CancelDrag(revert: true)`）。

## 左栏放大镜按钮（参考 BPSS）

左栏模式行（`PartOperator` 的 `ViewButton` / `PlaceButton` / `EraseButton` / `SelectButton`，
即玩家口中的「光标 / 增加 / 减少 / 框选」）右边多挂了一颗**放大镜**按钮，
点一下把镜头拉远到**装得下整台载具**并把视野挪到载具中心，再点一下回到原来的视距与位置。
图标 `Assets/Resources/BuildToolbar/Icon_Magnify.png` 是与其他图标同一条生成线做出的
简化放大镜（左上镜环 + 右下柄，不带 +/-，避免和 HUD 滑条两端的 `Icon_ZoomIn`/`Icon_ZoomOut` 混淆）。

### BPSS 那边的语义（为什么不是「切换正/俯视」）

BPSS 901-17 的建造界面**没有**独立的放大镜按钮资源，也没有「切换正交/透视」这种开关。
它的「拉远看全局」就是**建造态的最大拉远＝预览态**：

- `IngameCamera.cs` 的滚轮/双指缩放被夹在 `Min(m_cameraMinZoom, m_cameraBuildZoom) .. m_cameraMaxZoom`；
- 一旦缩到 `currentFOV <= Min(m_cameraMinZoom, m_cameraBuildZoom)`（`IngameCamera.cs:333`/`:348`），
  游戏就切 `LevelManager.GameState.PreviewWhileBuilding` → `CameraState.Preview`，
  跑 `EnablePreviewMode()` 把视距指数逼近 9，同时视点回到关卡控制点（`ControlPoints[last]`）；
  `HandlePreviewSwipe()`（屏宽 0.4 的横向甩动）是另一条等同入口。

bpsim 没有关卡相机控制点，所以等价物取「**按载具包围盒拉远 + 居中**」：
这就是「一眼看全车」的手感，且不改变任何玩法状态。

### 实现

`Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildMagnifier.cs`（`internal class BuildMagnifier`），
由 `UserInterface` 在装完工具栏/HUD 之后挂到左栏：

- **克隆模板**：`PartOperator.transform.Find("ViewButton")`，改名 `MagnifyButton`，
  排在 `SelectButton` 之后（`GetSiblingIndex() == select + 1`，步距沿用左栏的 65.7）。
  克隆体上的 `DoubleClickableButton` 被移除，换成独立的 `Toggle`（`group == null`），
  以免双击被当成「两次切换」。
- **开**：存下 `SceneCamera.TargetSize` / `TargetPoint`，然后
  `ComputeOverview()` 读 `PartSceneUnmanaged.TryGetPartsBounds()` 的包围盒：
  `half = max(extents.y * 0.5, extents.x * 0.5 / (aspect * usable))`，
  `size = clamp(half * 1.15, MinSize, MaxSize)`；
  横向居中时**扣掉左侧部件栏占的宽度**（`VisibleLeftFraction()`：从 `PartOperator` /
  `PartSelector` 往上找 4 级祖先的 `RectTransform`，用 `WorldToScreenPoint` 取最右缘 ÷ `Screen.width`，
  只采纳 < 0.7 的候选，最终 clamp 到 0..0.7），得到
  `center.x = mid.x - 0.5 * left * 2 * size * aspect` —— 载具落在**可见区**正中而不是整屏正中。
- **关**：`MoveTo(旧 TargetPoint)` + `ZoomTo(旧 TargetSize)`，回到点之前的状态。
- 空场景（没有部件 / 没有活动 PartScene）退化成一键拉远 `EmptyZoomOut = 2.5` 倍，不动镜头。
- 不改玩法状态：`PartOperator.Mode` 保持不变，所以「增加」模式下点放大镜之后仍然能继续放部件。

### 验收

`BuildToolbarQA.CheckMagnifier` 共 11 项：挂载 / 按钮已克隆 / 在框选右边 / 是独立 toggle /
已去掉双击组件 / 图标是 `Icon_Magnify` / 真的有拉远（`> near * 1.5`）/ 认出了侧栏遮挡 /
居中到 `GetPartsCenter()` 扣掉侧栏的期望点 / 保持放置模式 / 复位视距 / 复位镜头位置。

2026-10-28 实测（存档「不败荣光」1648 部件，Game 视图 1327×591）：
`left=0.185`、`bounds=parts=1648 float2(-348.5, 519.5)..float2(-292.5, 553.5)`、
`overview=19.55`、`center=(-328.64, 536.50)`；截图 `Logs/mag_over3.png`（整车入画、居中偏右避开部件栏）。

安卓（雷电模拟器 1920×1080）实测：`[BuildMagnifier] on saved=20 overview=23.657 left=0.234
fitted=True center=(-330.36, 536.50)`，再点一次 `off restore=20 point=(-331.00, 532.00)` 正确复位。

## 新增/改动的文件

| 文件 | 说明 |
| --- | --- |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildTool.cs` | `enum BuildTool { None, Adjust*, Move, Rotate*, MoveAll, RotateAll*, TransformPart, FreeTransformPart*, TransformMachine }`（带 `*` 的旧值保留编号但工具栏不再提供，见上表） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildUndoHistory.cs` | 24 步整场快照撤销 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildToolController.cs` | 工具状态机 + 指针手势 + 快捷键 + 可见状态同步 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildToolbar.cs` | 顶部图标按钮生成、布局、悬停提示与状态刷新 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildHud.cs` | Besiege 式 HUD：左上播放键 + 镜头滑条 + 自带提示层（`sortingOrder 5`） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/HudSlider.cs` | 抬头横向滑条：建造=镜头缩放（指数映射，只写 `SceneCamera.ScaleTo`）、模拟=时间倍速（写 `Time.timeScale`，0..200%） |
| `Assets/Resources/BuildToolbar/Icon_*.png` | 22 张程序生成的白色单线图标（+ `.meta`，含 `Icon_Stop` / `Icon_Knob` / `Icon_ZoomIn` / `Icon_ZoomOut` / `Icon_Clock` / `Icon_Magnify`） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/BuildMagnifier.cs` | 左栏第 5 颗放大镜按钮：按载具包围盒拉远 + 居中（避让左侧部件栏），再点复位 |
| `_codex_work/topbar/make_icons.py` / `make_icon_metas.py` | 图标生成与 `.meta` 生成 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartSceneUnmanaged.cs` | 追加 `ClearParts` / `TryMovePartContent` / `TryRotatePart` / `TryMoveAllParts` / `TryRotateAllParts` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/PartOperator.cs` | 追加 `SetMode` / `SetNeutral` / `PropertyPanel`，`OpenPartPropertyPanel` 改 public |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/UserInterface.cs` | 挂载工具栏与 HUD（`BuildHud` 先于 `BuildToolbar`）、`ActionBarView` 暴露 `GameObject` / `TemplateButton` / `SimulateButton` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/SceneCamera.cs` | 暴露 `MinSize` / `MaxSize` / `TargetSize` 给滑条；`Update`/`LateUpdate` 改用 `Time.unscaledDeltaTime`（不吃仿真倍速，暂停时也不会算出 `Infinity`） |
| `Assets/Scripts/Assembly-CSharp/Bpsim/UI/SimulationInterface.cs` | `EndSimulation()` 改 `internal` 供 HUD 停止键调用 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/BuildToolbarQA.cs` | Play 模式验收探针 |
| `Assets/Editor/BuildToolbarQaEditor.cs` | batch-mode 驱动（`RunBatch`） |

## 没有搬过来的部分（以及原因）

BPRC 顶部工具栏里这些功能在 bpsim 没有对应语义，本轮不做：

- **无网格偏移（`@gridlessX/@gridlessY`）、连续角度（`@fineAngle`）、部件缩放（`PartScale`）**：
  bpsim 的部件位置由 `PartTransform`（整数格 + 旋转枚举 + `Flipped`）决定，
  `PartTransformSystem` 每帧用 `BasePart.MoveTo/RotateTo` 覆盖 `LocalTransform`。
  要做连续变换就得扩组件和存档格式（`Schematics.Unit` 只有 6 个字段），
  属于「改数据模型」而不是「加工具栏」。
- **图层（+ / −，最多 16 层）、连接合并、近邻吸附**：bpsim 的 `PartGridMap` 有 level 维度，
  但建造流程只用 level 0，没有「载具组」概念。
- **框选 / 删除**：bpsim 已有——左栏的 Select 模式（`PartSelectionPanel`）、
  Erase 模式（`PartDrawer`）。工具栏不重复提供。
- **点按旋转**：工具栏原来确实提供过一枚独立的「旋转部件」，现已并进「平移部件」的原地单击
  （见上文按钮表）。这里记一笔是因为本文档早先写过「工具栏不重复提供点按旋转」，
  而当时的实现与这句话相反。
- **参数菜单的滑块/数值框**：沿用现有 `PartPropertyPanel`，不新增通用参数面板。

## 验证

```bash
# 全程序集编译 + Play 模式验收（退出码 0 = 全过）
Unity.exe -batchmode -projectPath <bpsim-main> \
          -logFile <bpsim-main>/Logs/bpsim-toolbarqa-unity.log \
          -executeMethod Bpsim.EditorTools.BuildToolbarQaEditor.RunBatch
```

报告写到 `Logs/bpsim-toolbarqa.log`，逐项 `PASS`/`FAIL`。

2026-10-22 实测 93/93；2026-10-28 加进放大镜的 11 项后实测（batch-mode 全程序集编译 + Play 模式，
`BuildToolbarQaEditor.RunBatch`，退出码 0）：`done:PASS pass=105 fail=0`，报告见 `Logs/bpsim-toolbarqa.log`。
2026-10-29 合并按钮之后重跑，项数随「合并掉的 5 枚按钮不再存在」与网格三态断言变化。
（QA 必须在**建造态**启动：停在模拟态时会有 11 项红——`play button icon | icon=Icon_Stop`、
`zoom slider starts as zoom | mode=TimeScale` 等。）
其中 `PASS BuildGridButton | … icon=Icon_Grid textHidden=True` 这类条目同时验证了图标装配——
每个按钮都有圆角底板 `Image`、`Button`、已加载的 `Icon` 精灵，且原文字层已隐藏。

> 交互式编辑器也可以用桥接直接跑，不必关掉 Unity：
> ```bash
> curl -X POST http://127.0.0.1:8791/api -d '{"method":"playmode","params":{"action":"enter"}}'
> curl -X POST http://127.0.0.1:8791/api -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.BuildToolbarQA","method":"Run","args":[]}}'
> curl -X POST http://127.0.0.1:8791/api -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.BuildToolbarQA","method":"Status","args":[]}}'
> ```
> 注意 `{"method":…,"params":{…}}` 这个外壳是必需的，`{"cmd":…}` 会得到 `unknown method`。
> 退出 Play 有时要发两次 `playmode {"action":"exit"}`（第一次返回 `playing:true` 表示还没生效）。

### 目视验收（截图）

数值断言看不出「压到别的东西上」，所以 `BuildToolbarQA` 还留了两个手动入口
（都是 `public static`，桥接的 `invoke_static` 能直接调）：

| 方法 | 作用 |
| --- | --- |
| `Capture(string path)` | 存一张 PNG，相对项目根解析（`ScreenCapture`，能拍到 Screen Space Overlay 的 uGUI） |
| `Simulate(bool on)` | 等价于点一下 HUD 左上角的播放键（走 `Button.onClick`，不是直接调 `PartManager`） |

```bash
# 进 Play 后先把场景铺起来（FreeTransformDemo 会加载带两个部件的空场景）
curl -X POST http://127.0.0.1:8791/api -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.FreeTransformDemo","method":"Setup","args":[]}}'
curl -X POST http://127.0.0.1:8791/api -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.BuildToolbarQA","method":"Capture","args":["Logs/hud/hud-build5.png"]}}'
curl -X POST http://127.0.0.1:8791/api -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.BuildToolbarQA","method":"Simulate","args":[true]}}'
curl -X POST http://127.0.0.1:8791/api -d '{"method":"invoke_static","params":{"type":"Bpsim.QA.BuildToolbarQA","method":"Capture","args":["Logs/hud/hud-runtime5.png"]}}'
```

实测两张图（Game 视图 1692×871，`CanvasScaler.matchWidthOrHeight = 1` 所以 UI 像素 = 屏幕像素 ÷ 0.881）：

- `Logs/hud/hud-build5.png`（建造态）：左上绿色播放键，右侧紧跟横向滑条 ——
  一枚小放大镜、一段灰色细滑槽（中间白色圆旋钮）、右端另一枚放大镜；
  再往右是文件按钮（文件夹 / 软盘 / 相机 / 齿轮），图标比早先小一号（窄窗口 `Reflow`）。
- `Logs/hud/hud-runtime5.png`（模拟态）：同一位置变红色停止键，
  滑条左侧换成**青色时钟图标 + 青色 `100%`**，滑槽与旋钮不变 —— 和 Besiege
  顶栏那条「播放键 → 时钟 → 百分比 → 细滑槽 → 白旋钮」的读法一致。

`BuildToolbarQA` 覆盖（放大镜 11 项见「左栏放大镜按钮」）：工具栏挂载、**10 个图标按钮**（原有 4 个 + 工具 6 个，逐个检查
存在 / 有底板 `Image` / 有 `Button` / 图标精灵已加载 / 原文字层已隐藏）、
**合并掉的 5 枚按钮不再存在**（`BuildAdjustButton` / `BuildRotateButton` / `BuildRotateAllButton` /
`BuildFreeTransformPartButton` / `BuildGridlessButton`）、
**播放键已离开 ActionBar**、原有按钮重排（`load=428 settings=596.48`，步进读 `LastStep`）、
**按钮两两不重叠**（按 anchor 换算成区间逐个比较，这是「工具栏和上方按钮合并」最容易出错的地方）、
工具栏右边缘不压 FPS、FPS 文本保留、提示条存在且拿到字体、**提示条绘制顺序在场景标签条之后**、
10 个按钮都注册了提示文字、**悬停命中把 10 个按钮的屏幕中心逐个喂进命中逻辑都能命中、
画布空白处不命中**（这一段验的是 overlay canvas 下 `RectangleContainsScreenPoint` 的相机参数），
**HUD 独立画布 + 排序在模态面板之下**、播放键挂在 HUD 上 / 图标正确 / 76 大号 /
**左上角锚定 (56, -45)** / **不与右移后的文件按钮相交**、
**滑条是横条（232×44，倍速模式 254×44）/ 在播放键右侧同一水平线 / 不越过文件按钮 / 结构齐全
（细线滑槽无填充、白旋钮、模式图标、百分比文字、右端放大镜）/
**行内左右空隙一致（图标—滑槽与滑槽—缩小图标都是 12）、热区贴着内容收尾** / 默认缩放模式 /
真的驱动 `SceneCamera` / 指数映射 / 跟随滚轮**、
**切到模拟后滑条换成时间倍速**（模式、`Icon_Clock`、右端图标收起、
0% 暂停 / 100% 原速 / 200% 两倍速、百分比文字、退回建造时 `Time.timeScale` 复原）、
HUD 提示注册与悬停命中、**运行态布局**（播放键原地不动、只换 `Icon_Stop` 和红色、
滑条位置也不动、提示改成「停止模拟」，再摆回建造态验证可逆）、有场景后播放键变绿、
网格开关往返、撤销栈空/非空、部件平移与撤销复原、整车平移与撤销复原、
整车旋转与撤销复原、工具互斥（激活工具后左栏 `Mode == None`），以及
**场景从空开始 + 播种后恰好两个部件**（保证整车旋转的包围盒中心可复现，测试不会因为
上一个探针留下的部件而随机失败）。

> 注意：`-batchmode` 要求没有别的 Unity 实例开着同一个项目，否则会以
> `It looks like another Unity instance is running with this project open.` 退出（exit 21）。

不开编辑器时的快速语法/类型检查（本仓库没有 `Tools\unity-batch.bat`）：

```bash
# 从 Library/Bee 里拿一份 Unity 上次编译用的 rsp，改 -out/-refout 到 _codex_work 下再跑
"<Unity>/Editor/Data/NetCoreRuntime/dotnet.exe" "<Unity>/Editor/Data/DotNetSdkRoslyn/csc.dll" \
    @_codex_work/topbar/build/check.rsp
```

`_codex_work/topbar/build/check.rsp` 是快照，**新增 .cs 后要手动把文件加进清单**
（`_codex_work/topbar/build/rsp_diff.py` 可以比对 rsp 与磁盘上的源文件）。

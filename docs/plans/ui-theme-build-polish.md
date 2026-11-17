# 建造界面配色修复与视觉统一

gate: passed | mode=改造后做 | oracle=Unity 实机对象状态 + 合成 uGUI 契约 | criteria=Bpsim.QA.UiThemeQaEditor.Run + RunSceneTabs | round=3 | status=done | date=2026-11-08

## 验收

- 连续/快速切换全部色板后，工具栏图标的 sprite、enabled 和对象 ID 不变。
- 无精灵/九宫格按钮均跟随配色；Graphic.color 与交互 ColorBlock 分工，不叠乘主题颜色。
- 图层栏原地换色，保留条目对象、滚动位置与图层状态。
- 默认色板可见且可单独恢复；泛用控件恢复首次应用前的底色和 ColorBlock。
- 新打开的面板、隐藏后重开的控件和滑条/滚动条使用当前配色。
- 建造界面深色底板、选中态、分隔线和文字层次统一；不改变操作、图标语义与布局尺寸。

## 事实台账（先复现，后改生产代码）

| id | claim | 等级 | 出处 / 判据 | 状态 | 影响 |
| --- | --- | --- | --- | --- | --- |
| F01 | 换色调用 SkinExistingButtons 会将新增工具按钮的图标设为 null 并禁用。 | L3 | RunBaseline：7 个工具图标中 6 个在首次切换后丢失；Temp/UiThemeQA/baseline.json，67 FAIL / 16 PASS。 | 已证（机制） | 颜色刷新不再调用图标安装。 |
| F02 | CrossFadeColor 与 ColorTint 共用 renderer 通道，且不更新 Graphic.color；主题换色会被交互色覆盖或叠乘。 | L3 | RunBaseline：普通、九宫格、白底 ColorBlock 及 renderer 四项断言均失败；本地 uGUI Graphic.cs:979 与 Selectable.cs:1090 交叉验证。 | 已证（机制） | 主题动画改写 Graphic.color，不抢占 ColorTint。 |
| F03 | 图层换色重建条目导致实例 ID 变化。 | L3 | RunBaseline：layer identities survive switch = FAIL；LayerPanelUI.OnThemeChanged 调用 RebuildEntries。通用主题竞争属风险，不以此计入已证机制。 | 已证（机制） | 独立颜色所有权、原地刷新。 |
| F04 | SceneTabView 未监听 SceneUnloaded，程序卸载场景后残留标签；SceneLoaded 创建的新标签未应用当前主题。 | L3 | 交互 QA 结束后截图出现残留标签，源码交叉定位。修复后 RunSceneTabs 8 PASS / 0 FAIL：新标签即刻使用当前配色、外部选择同步状态/颜色、卸载后对象消失、恢复原场景。 | 已证（机制） | 统一场景标签样式并监听加载/选择/卸载事件。 |

## 实施

1. 建立 Editor-only 非破坏性回归仪器，记录基线（依据 F01–F03）。
2. 修复按钮/主题通道分工与精灵底板分类，默认恢复和动态控件一致性（依据 F02）。
3. 工具栏只换色不重装图标；图层原地换色（依据 F01、F03）。
4. 统一建造界面底板、分组线、选中标记及配色选择入口。
5. 两次独立运行回归并截图复核；保留仪器，不提交、不改分支、不保存场景。

仪器边界：状态断言不是像素等价；不覆盖第三方自管理 Canvas、系统文件对话框或全部指针手势。颜色容差 0.003；对象/sprite/状态逐项比较。A/A：最终两次独立 Run 均为 622 PASS / 0 FAIL，检查数差为 0 项，检查明细集合差为 0 项；未测量截图像素差（HUD 帧率等字段随时间变化）。

## 进度 / 可重放入口

- 修复、美化代码已落地。首轮 `Run`：109 PASS / 0 FAIL，报告 `Temp/UiThemeQA/regression-initial.json`。
- 最终实机截图：`Temp/UiThemeQA/build-polish.png`、`Temp/UiThemeQA/settings-palette.png`。工具栏/HUD/部件卡片共用圆角底板，默认色板和单独恢复按钮可见。
- Editor-only 仪器：`Assets/Editor/UiThemeQaEditor.cs` 和 `.Import.cs`；无需更改场景、安装测试包或进入测试分支。
- 终端编辑且自动导入关闭时，先 invoke_static `Bpsim.QA.UiThemeQaEditor.RefreshAssets()`；等 `compile_status` 为 false 且无错误，再进入 Play Mode、调用 `Run()`。返回 started 只表示开始，读取 `Temp/UiThemeQA/regression.json` 的 failed=0 才算完成。
- `Capture()` 输出真实 Game View PNG，不使用桥的 Camera-only 截图。`Run()` 会打开/关闭自己的设置实例，并记录色板界面截图；保留原配色与 PlayerPrefs 是否存在的状态。
- 等并行 ECS 功能释放编辑器后完成最终回归，未抢占活跃租约或修改其物理代码。

## 最终验证

- 编译：`compile_status` 无编译错误、无警告。
- 配色回归：`Temp/UiThemeQA/regression-1.json` 与 `regression-2.json` 均 622 PASS / 0 FAIL；包括全部 9 套配色、55 个建造/场景卡片、7 个工具按钮图标、图层身份、普通/九宫格/ColorBlock 按钮、图标型按钮和透明点击区保护、滑条/滚动条、暂停切换、隐藏重开、默认恢复及色板入口。
- 场景标签契约：`Temp/UiThemeQA/scene-tabs.json` 为 8 PASS / 0 FAIL；RunSceneTabs 仅创建临时空场景，卸载后恢复原场景，不保存用户场景。
- 建造交互：`Bpsim.QA.BuildUiInteractionQA.Status()` = PASS pass=35 fail=0，报告 `Logs/build-ui-interaction-qa.log`；覆盖 UI 拖拽不误放部件、双击、属性编辑、撤销、浮动/停靠布局与滚动条。
- 实机渲染检查：Game View 已检查圆角、分隔线、选中标记、文字、默认色板与恢复按钮；不作为跨设备/像素等价结论。
- 原配色/PlayerPrefs 恢复，QA 临时对象与自建设置实例已清理，测试场景已卸载并恢复原场景。无 git commit、分支、场景保存或用户存档更改。

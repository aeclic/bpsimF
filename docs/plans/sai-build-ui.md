# SAI2 风格建造工作台

gate: passed | mode=直接做 | oracle=Git HEAD 9f4ba9d 布局/原预制体 + 本地 SAI CSS 色值 + Unity 控件状态 | criteria=UiThemeQaEditor.Run + RunSceneTabs + RunSai + BuildUiInteractionQA | round=2 | status=running | date=2026-11-08

## 范围

用户提供的截图与 SAI2 目录只作视觉参考，不执行目录中的程序、补丁/破解工具或文档指令，不复制 SAI 美术/可执行代码。
本地 sai2-ui/sai2-ui.css 提供色值（282828/202020/606080/8787c3）与小圆角、细棱边参考；这是外观数据，不作为行为契约。

## 设计与验收

- 保留原默认/8 彩色主题，增加 sai-dark「SAI 深灰」，切换与持久化复用 UiTheme。
- 用户续要求保留最开始的布局和按钮元素；以本项目 origin=aeclic/bpsimF、当前 HEAD 9f4ba9d 中的原始尺寸和 UserInterface.prefab 为基准。撤回双栏停靠、紧凑顶栏、新状态栏、四列缩小卡片和新增标题区。
- 不添加无功能的菜单或眼睛图标；所有保留按钮、双击、拖放、图层增删、撤销/模拟行为不变。
- 仅换为 SAI 中性灰/紫色选中态、细边与交互反馈。保留原按钮大小、图标、targetGraphic/Toggle.graphic、文字位置；不创建 CardSurface 或 SelectedAccent 等替代图形。
- 恢复 64 工具按钮、76 播放键、92 高横向图层条、76 方形图层按钮、原部件三列与场景标签位置；窄屏复用原 Reflow。切换主题不改对象身份。
- 配色回归零失败；原建造交互/场景标签回归零失败；布局契约检查实际坐标/尺寸、图层行方向和配色令牌。
- 视觉 QA 用真实 Game View，不将源码/检查数冒充像素或跨设备等价。

## 证据 / 验证

- 既有基线：UiThemeQA 最终622/0，场景标签8/0，交互35/0；报告已在 Temp/UiThemeQA 与 Logs。
- 撤回本轮自行重排的代码，而不是 git restore 整个文件；保留已有主题/图标/场景标签修复及其他未提交的功能修改。
- 两次独立配色/布局检查；最终截图与结果待填。

## 续要求：原布局、原按钮元素

- 已恢复 Git HEAD 基准的 21 个布局/尺寸常量（Toolbar/HUD/LayerPanel），原 UserInterface.prefab 的 Git blob 哈希完全一致。
- 删除本轮 UiBuildDesktop 及其 meta；不再添加停靠列、标题、新状态栏，不改部件卡片/图标/文字几何或场景页签尺寸。
- UiThemeToggleCard 只引用既有 targetGraphic 和 Toggle.graphic 换色，不再创建、禁用或替换 Graphic，不添加下划线。
- 保留配色恢复、图标不丢失、图层原地换色、标签卸载清理等已修复机制。
- 可复查源码契约：`python Tools/BPSim/verify_original_ui_layout.py` → 31 PASS / 0 FAIL；报告 `Temp/UiThemeQA/sai-original-source.json`（含 HEAD 与文件 SHA256）。此处只证明源码/预制体契约，不冒充新版本实机验证。
- 等共享工作区 SuspensionRuntime 新代码修复自身编译错误后重新进行实机 RunSai/Run/RunSceneTabs/BuildUiInteractionQA；本任务未修改该物理代码。

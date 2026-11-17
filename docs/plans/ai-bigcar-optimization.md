gate: passed | mode=改造后做 | oracle=固定版本源码对照；运行时 QA/Profiler/触控回放待采集 | criteria=先完成 T0，再按各任务的二值门禁重判 | round=1 | status=planned | date=2026-11-06

# AI-BigCar 参考后的本项目优化计划

依据：[源码审查 R01–R08](../AI_BIGCAR_REFERENCE_REVIEW.md)。

**本轮只交付检查和修改准备，不授权跳过基线直接改生产逻辑。** 判为“改造后做”的理由：源码能定位候选修改点，但目前没有可用的 Unity 连接、独立 A/A 性能地板或多指状态回放，无法判定收益和玩家可见行为。上面的 gate 表示已完成分流，不表示运行时前提已经通过。

## 1. 范围、边界和已有工作

- [x] 固定参考提交 `2c89b88345c5e861135266b160a83e134edbc245`，检查代码、依赖和许可边界。
- [x] 对照本项目当前工作树，区分新机会、已有能力和不应采用的实现。
- [x] 明确修改入口、先后关系、基线口径和回归要求。
- [ ] T0 运行时基线与状态回读。
- [ ] T1 UI 命中查询临时对象复用。
- [ ] T2 UI 组件缓存与差分刷新。
- [ ] T3 双指中点平移和手势取消/接管。
- [ ] T4 固定步追赶预算实验。
- [ ] T5 按钮重建分配优化与碎片碰撞正确性审查（条件回流）。

不更换 ECS/BRG/物理后端；不升级 Unity 或 Packages；不替换 `BpsimInputModule`；不复制参考项目代码、贴图、音频或编辑器包；不改变存档格式、部件参数或用户选择的时间倍率。不创建分支、提交或恢复点。

本轮检查时 `SceneCamera.cs`、`BuildToolController.cs`、`BuildToolbar.cs`、`BuildMagnifier.cs`、`PartButtonList.cs`、`PartTriggerButton.cs`、`UserInterface.cs` 和 Anvil 托管后端已有未提交改动。**实施前重新检查状态和认领，不覆盖这些工作。** 先完成 [anvil-stability](anvil-stability.md) 已有验证任务，再做涉及调度/后端的实验；不把它的失败归因于本计划。

R 编号只是审查出处，不等于已证的实施事实。生产改动前必须在 `docs/facts.md` 预注册对应“待证” claim，填出假设、互斥预测、机器判据；复现/探测后回写同一行，任务注明实际 F 编号。**现在没有为本计划登记 F 编号，也没有开始任何生产改动。** 文档整理不占用事实台账。

## 2. T0：把候选项改造成可判任务

依据：R01–R04、R06–R07。先在隔离测试场景/载具和独立日志目录建立仪器，再改业务逻辑。

### 2.1 状态回读与输入标定

回读至少包含：UI/画布归属、pointerId/touchId、手指数、按下/释放/取消原因、建造模式、当前部件 Entity、自由偏移/角度/缩放、相机目标位置/尺寸、撤销项数、模拟/暂停状态。

GUI 输入验证前生成坐标标定图：标出画布可交互区域、UI 遮挡矩形、两指点和中点；记录分辨率、DPI、Canvas scaleFactor 和屏幕/世界单位。先证明输入确实到达目标状态，再判拖动/缩放行为。合成 Input System 设备用于 L3 回放；真实 Android/触屏观察另作为 L4 玩家可见验收，不混成一个结论。

仪器放现有 `Bpsim.QA` 独立入口，按现有编辑器条件编译约定隔离，并使用一次性场景/对象。仪器头部写清：运行命令、判据、覆盖边界和失效条件。退出/异常时恢复原场景选择、输入设备、timeScale、设置和相机，不写用户存档，不清空 Console。

### 2.2 基线配置和输出

| 维度 | 预定覆盖 |
| --- | --- |
| 载具 | `perf/angel_2k.csv`、`angel_5k.csv`、`angel_10k.csv`、`angel.csv`、`buhai.csv`；按实际加载的 Parts/Joints/Components 计数，不用 CSV 行数冒充实体数 |
| UI | 关闭/打开属性面板、展开统计、模拟按钮列表、指针在画布/工具栏/输入框/滚动区；另建触发部件较多的测试载具 |
| 输入 | 鼠标点击/拖动/中键；一指、双指同向移动、距离变化、1→2→1→0、任一手指从 UI 起步、失焦/取消 |
| 时间与帧率 | timeScale=0/0.25/0.5/1/2，目标 30/60/120 FPS；记录实际帧率与 VSync，不能只记录请求值 |
| 物理 | UnityPhysics/HavokPhysics/AnvilPhysics 分别记录；同一后端内 A/A、A/B，不把不同后端当等价输出 oracle |
| 构建 | Editor 与 Player 分开。本项目 Editor/Burst 路径有特殊处理，编辑器数字不外推成发布包收益 |

UI 稳态测试预热 120 帧、采样至少 600 帧；拓扑重建单独记录触发次数和每次成本。每个正式比较配置先独立启动采集 A1、A2，再采集修改后 B；同一日志解析两次不算独立 A/A。固定载具、视角、面板、分辨率、倍率、物理步长和后端，只在同一个配置内计算地板。

输出到 `Logs/ai-bigcar/<runId>/`，至少保存构建/源文件指纹、配置、样本数、frame ms 的 p50/p95/p99/max、目标 UI 区段耗时/分配字节、命中查询次数、按钮分配/重建次数、每帧 fixedSteps、实际推进的模拟秒数。每个量注明单位；缺 recorder/无样本/未进入目标状态时返回 ERROR，不能填零并判 PASS。

以下是**待新增的仪器接口，不是现在已存在或已跑通的命令**：

- `Bpsim.QA.UiPerfProbe.Run(string spec)` / `Status()`：采集 UI 区段、帧时间及分配，输出上述结构化报告。
- `Bpsim.QA.GestureReplayQA.Run()` / `Status()`：固定触摸序列，回读状态、相机数值、部件数据和撤销计数。
- `Bpsim.QA.FixedStepBudgetQA.Run()` / `Status()`：验证固定步数上限、时间推进与积压恢复。

T0 的 PASS：状态阳性对照有效，标定与输入回读通过，A1/A2 独立且配置/样本齐全，恢复检查通过；把结果和采集命令写入事实台账。捕获不到状态时继续修仪器，不把 ERROR 计入行为失败率。

## 3. 实施切片

### T1：先消除统一命中查询的明确临时分配

依据：R03；实施 F 编号在 T0 复现后登记。拟改 `UI/UIExtensions.cs` 及专用 QA；暂不合并 `SceneScreen` 和输入模块的 RaycastAll。

1. 增加专用 scratch PointerEventData，按 EventSystem 身份/生命周期重新创建。
2. 不与 `BpsimInputModule` 的按压对象共享，不破坏 pointerPress/pointerEnter。
3. 结果列表保持复用，并在异常路径清理；确认首次命中仍是实际顶层 UI/SceneScreen。
4. 跨调用结果缓存暂缓，直到测到有价值的重复查询并验证同帧 UI 变化、指针身份和执行顺序。

PASS：预热后固定场景查询包装层不再每次分配 PointerEventData；命中对象/布尔值与旧版本逐次一致；更换 EventSystem、开关弹窗、多指不同位置、UI 拖到画布都不串状态；现有 UI 输入 QA 完成且 fail=0。**不要求整个 uGUI/整帧 GC=0。**

### T2：缓存 UI 引用和显示值，不每帧重复格式化

依据：R02；实施 F 编号在 T0 登记。拟改 `UI/PartPropertyPanel.cs`、`PartDrawingPanel.cs`、`UserInterface.cs`、`SimulationInterface.cs` 及 QA；后两项等待现有改动稳定。

1. 创建/打开面板时缓存 Text、InputField、Slider 和必要布局引用，移除热路径 Find/GetComponentInChildren。
2. 使用非分配属性快照判断静态字段变化，仅更新改变的显示项；外部工具、撤销、换部件、换图层仍能使显示失效。
3. 动态姿态默认按实时时间 10 Hz 采样，FPS 文字跟随现有约 0.5 秒结果更新；静态编辑操作立即刷新。
4. 保留输入焦点、SetTextWithoutNotify/SetValueWithoutNotify、防重入及模拟只读规则。布局、显隐和数据刷新分离，尺寸/可选项变化才 Reflow。

PASS：预热后静态属性/部件名显示区段不再持续分配格式化字符串；值变化能在下一次规定刷新内显示，直接编辑即时反映；聚焦输入不被覆盖；NaN/非法值不污染 ECS；撤销、模拟只读、停靠/浮动、图层与皮肤/形状项 QA 通过。暂停下 UI 仍更新。目标区段分配下降，整帧 p95 不回退超过性能门禁容差。

### T3：双指中点平移 + 明确手势接管/取消

依据：R01、R05；生产改动前先复现现有边界并登记 F 编号。拟改 `UI/SceneScreen.cs`、`SceneCamera.cs`、`UI/SimulationInterface.cs`、`Parts/PartDrawer.cs` 及手势 QA；必要时小范围补 `BuildToolController`/`BuildUndoHistory` 的未提交手势记录处理，不整套替换撤销实现。

1. 保留现有 PointerData 构造和单指语义，另提供显式相机手势数据/取消原因，避免破坏旧 QA 和建造消费者。
2. 锁定稳定 touchId 对：初始间距/尺寸给缩放比例，中点增量给平移；距离接近零、第三指、指序变化时重定基准或取消，不产生 NaN/跳变。
3. 第二指接管时取消部件预览，回到按下前变换；相机归属保持到这一触摸会话结束，不把二指退一指当新的建造按下。
4. 两指起点都在画布才允许接管。UI 上按下、弹窗覆盖、画布禁用、场景切换、失焦或 Canceled 都走明确清理路径。
5. 用 unscaled 时间维持暂停下的相机响应，保留 QuickBuild、中键、gizmo、放大镜预览和模拟触摸只触发一次的规则。

PASS（L3 数值/状态回放）：

- 双指同向移动时相机目标位置改变，缩放目标保持不变；只有距离变化时缩放符合初始比例并正确钳位。比较目标值或等待平滑稳定，不能用未 settle 的截图判数值。
- 1→2→1→0 不放置、不旋转、不触发部件；被取消的拖放恢复原变换，不留下新有效撤销项，不删除旧历史。
- 任一指从 UI 起步时，画布部件数据、相机目标和撤销计数保持原值；UI 控件仍收到自己的释放/点击。
- 一次有效单指编辑只产生一个有效撤销项；鼠标/中键/双击属性/gizmo/放大镜现有用例不回退。

玩家可见 PASS 另需真实触屏/Android 观察：双指平移/缩放平顺，1→2→1 不跳镜头、不误放置；30/60/120 FPS 与暂停都可用。L3 合成测试不能代替此项。

### T4：测清楚 ECS 调度后才试追赶预算

依据：R04；前置为已有 Anvil 改动验收完成、T0 固定步和时钟回读有效。拟改范围只在 `Parts/PartManager.cs` 的固定步配置/专用 rate manager、可选设置入口和 QA，不把改动塞进物理求解器。

1. 先同时读回 Time.maximumDeltaTime、World.MaximumDeltaTime、group timestep、每帧 fixedSteps 和两个时钟；验证真正生效的限制。
2. 独立比较 N=1/2/4/8 等候选预算，不提前把任何 N 定为默认。不改 fixed timestep，也不自动改用户 timeScale。
3. 明确过载积压保留、恢复和提示策略；不得通过少推进模拟时间把“每帧成本下降”冒充“每步物理提速”。长期追不上时报告实际仿真速度。
4. 保留后端/设置切换和退出时恢复路径，不修改用户存档或全局配置。

PASS：受控每帧实际步数不超过选定 N；每一步 dt 不变且为有限数；正常负载下倍率与推进时间匹配；注入卡顿后无静默丢步，积压策略符合明确契约并可恢复；按相同模拟步数比较，落地、轮/弹簧/关节、断裂、射线和触发回归通过；输入/UI 响应改善超过 A/A 地板。

`AnvilBenchBatch` 手动推进 World 时间，只能验证每步物理成本和基本落地正确性，**不能单独验收渲染帧中的追赶预算**。必须组合 FixedStepBudgetQA 和整链路采样。

### T5：条件回流，不默认扩大范围

| 任务 | 暂缓原因 | 回流条件/验收重点 |
| --- | --- | --- |
| `PartButtonList.CreateButtons` 临时数组/列表复用 | 已有按钮池与 dirty 刷新，当前文件还在改；未证明重建是显著尖峰 | 拓扑断裂基线显示重建成本后再改；预热后不因等规模重建持续分配缓冲区，按钮状态/成员/排序一致，重新模拟不叠按钮 |
| 控制组增量更新、列表虚拟化 | 参考项目没有可搬的实现，接口与复用状态复杂 | 全量重建/可见按钮数被 profiler 证实为瓶颈，再先写等价 oracle |
| 超过 30 个连通块的过滤正确性 | 本地已有显式性能取舍，改变过滤可能增大宽相位成本 | 先建 1/29/30/31/90 块碰撞与断裂 fixture；精确过滤同时通过各后端接触、射线和宽相位耗时门禁，才决定是否替换 |
| 物理后端替换、求解迭代数/休眠参数移植 | AI-BigCar 无可用的同规模/同语义证据，当前已有后端 | 新证据与单独授权/验收口径出现时另立计划，不顺手推进 |

## 4. 现有验证入口与性能门禁

Unity 连通、稳定后使用本项目 MCP：先查 unity_status，取得唯一标签的编辑租约，再执行 compile/playmode/invoke_static，完成即释放。不绕过租约，不抢文件认领，不清日志；不确定写请求先检查状态再确认。Play Mode QA 会改临时对象/选择/设置，先检查副作用，优先隔离测试，不能直接作用于用户正在编辑的载具。

现有入口（均未在本轮运行）：

| 入口 | 用途与完成判据 |
| --- | --- |
| `Bpsim.QA.BuildUiInteractionQA.Run()` / `Status()` | UI 起点归属、拖放与属性输入回归；最终 PASS，pass>0、fail=0 |
| `Bpsim.QA.QuickBuildInteractionQA.Run()` / `Status()` | 短按、长按、拖动与对齐；最终 done:PASS，pass>0、fail=0 |
| `Bpsim.QA.BuildToolbarQA.Run()` / `Status()` | 工具栏、gizmo、放大镜等回归；最终 done:PASS，pass>0、fail=0 |
| `Bpsim.QA.SpinPerfProbe.Run("file=perf/buhai.csv;dur=5;spin=32;mode=rigid;bisect=0")` | 已有全链路自旋/帧时间采样；Status 结束且报告非空，不能把 requires play mode/running 当完成 |
| `Bpsim.QA.AnvilBenchBatch.Run()` | 同一后端固定步基准；日志出现 `=== done ===`、有效样本、belowGround=0，无 init failed/EXCEPTION/native crash；同时检查 Unity 编译日志 |

关闭/协调当前编辑器后，批处理可以复用已核实的项目入口；先确认 UNITY_EXE 指向本项目版本，**不在本轮自动启动**：

```powershell
# 编译 + 退出。桥连接失败不等于编译失败或源码通过。
& .\Tools\unity-batch.bat

# 固定每步路径基准；每次独立运行都另存报告，避免后续覆盖 A1/A2。
$env:ANVIL_BENCH_SPEC = 'bodies=2000;steps=240;warmup=60;backends=UnityPhysics,HavokPhysics,AnvilPhysics;tree=0;contacts=1;rays=10;weld=1;sleep=1'
& .\Tools\unity-batch.bat -executeMethod Bpsim.QA.AnvilBenchBatch.Run
```

退出码 0、调用返回 running 或只打印方法名都不是通过。异步测试等最终状态与输出；基准异常可能被入口捕获，因此必须检查报告。Editor 报告不作为发布包跑分。

性能比较预定规则（门禁，不是已经测得的收益）：

1. **地板**：对每个同配置指标记录两次独立 A/A 的绝对差；帧时间单位 ms、分配单位 bytes/call 或 bytes/frame、步数单位 steps/frame。
2. **边界**：UI 包装层/刷新区段不等于整个 uGUI，物理固定步不等于渲染帧，合成触控不等于真实设备。
3. **形态与尺度**：功能逐字段/事件序列比较，GC 与耗时数值比较，镜头布局另按几何和稳定状态比较。
4. **收益**：目标区段分配减少或耗时改善，且改善超过该指标 A/A 地板才报告“实测优化”；不把局部改善外推为整体 FPS 倍数。
5. **回退**：相同配置整帧 p95 的回退不得超过 `max(A/A 绝对差, 基线 p95 的 5%, 0.5 ms)`；任何功能回归、NaN/异常、失败的状态回读都阻止合入。

## 5. 当前交接状态

**检查与计划已完成；T0–T5 均未实施。** 下一步是连接/协调 Unity，在隔离 fixture 上采集 T0。之后优先 T1 的小范围对象复用，再推进 T2 和 T3；T4/T5 等证据与现有物理工作收尾。

如参考提交、本项目源码/后端、测试数据或输入口径改变，先核对来源清单、更新审查并重新过闸。维护计划时记录日期、实际 F 依据和受影响任务；不删除旧结论来掩盖前提变化。

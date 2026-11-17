# AI-BigCar 可借鉴项审查

审查日期：2026-11-06。参考仓库：[CatBox271/AI-BigCar](https://github.com/CatBox271/AI-BigCar)。

**结论：值得借鉴交互设计和少量低开销更新策略，不适合移植它的物理架构；没有证据表明它的物理求解或 UI 整体性能优于本项目。**

本轮完成源码检查、工作树对照和修改准备，**未修改物理/UI 运行逻辑，未执行参考工程，也未完成 Unity 编译或实机验收**。后续实施见 [修改计划](plans/ai-bigcar-optimization.md)。

## 1. 审查范围与证据边界

| 项目 | 核实结果 |
| --- | --- |
| 参考版本 | `master`，提交 `2c89b88345c5e861135266b160a83e134edbc245`；所有源码链接固定到这个提交 |
| 获取方式 | `git ls-remote --symref` 核实 HEAD，再通过 GitHub codeload 获取固定提交源码；没有导入参考项目资产或执行脚本 |
| 参考工程 | Unity `2022.3.62f3c1`，传统 `MonoBehaviour` / `Rigidbody2D` / `Joint2D` / uGUI |
| 本项目 | Unity `2022.2.23f1c1`，DOTS/ECS、BatchRendererGroup、Unity.Physics，含 Havok/Anvil 后端 |
| 源码扫描范围 | 参考工程 `Assets` 下 175 个 C# 文件；重点阅读相机、建造、按钮、撤销、连接和时间设置。`Packages` 下另有 35 个 C# 文件，主要是 Locus 编辑器工具，不计作游戏性能优化 |
| 性能证据 | 在参考 `Assets` C# 中未找到 `Profiler`、`Stopwatch`、`FrameTiming`、`BurstCompile`、`IJob`、`ObjectPool` 或 NUnit/UnityTest 标记；`Frames.cs` 是简易 FPS 显示，`TimeIntervalRecorder.cs` 记录 K 键间隔，不是物理/UI 基准 |
| 授权边界 | 本次源码中未发现游戏本体的 LICENSE/COPYING/NOTICE。Locus 的第三方许可不等于游戏代码、贴图和音频的许可；只提炼思路并独立实现，不直接复制代码或资源 |
| 本地状态 | 对照的是已有未提交改动的工作树，不只是 `HEAD=9f4ba9da5df425836cc54b571e3ece1df5bef38b`；物理和 UI 文件仍有其他工作在进行 |
| 运行时状态 | 检查时 `http://127.0.0.1:8791` 拒绝连接，Unity MCP 桥不可用。本轮没有物理、GC、帧时间或触控实测数字 |

源码和 SHA256 清单保存在 D 盘忽略目录 `_codex_work/ai-bigcar-reference-20261106/`，清单为 `source-manifest.json`。清单只记录来源与文件摘要，**不是恢复点或工作树备份**。下载的约 58 MB ZIP 已删除，源码留在 Assets 目录外作只读参考。

以下 R01–R08 是**静态审查条目**，不是运行时验收事实。尤其是“可能少分配”“可能改善手感”都必须先补基线与回放证据，再登记到 `docs/facts.md`，不能据此承诺 FPS 倍数。

## 2. 值得采用的思路

### R01：双指缩放与中点平移同时进行——交互价值最高

参考：[LevelCamera.cs:268–325](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/LevelCamera.cs#L268-L325)。

- 双指开始时保存初始间距和缩放量；按当前/初始间距比例更新镜头尺寸。
- 同时计算两指中点，用中点移动量平移镜头。
- 两指变回一指时重置移动基准，避免沿用旧坐标跳变。
- [FollowMouse.cs:75–89](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/FollowMouse.cs#L75-L89) 还会在第二个手指出现时终止拖放。

本项目已有双指缩放，但 `UI/SceneScreen.cs:145–162` 返回的是第一根手指的点/增量，并在多指时令 `IsPressed=false`；`SceneCamera.cs:158–163` 的平移分支又要求连续两帧按住。因此当前数据路径没有显式提供**双指中点平移**。

另一个待复现风险：第二指使 `IsPressed` 从 true 变 false，部件交互可能把它看成正常释放而不是取消；二指退回一指也可能被建造逻辑看成新按下。它们是源码推导出的风险，**尚未确认成实机 bug**。

建议独立实现“相机手势”数据，不把相机平移强塞进部件 `IsPressed`：记录稳定的 touchId 对、初始距离、中点，以及取消/释放原因。第二指接管时取消部件预览，本次触摸会话全抬起前不重新放置/旋转部件。两指都必须起于画布，任何一指起于 UI 都不得偷取 UI 的手势。保留鼠标、中键、gizmo、放大镜预览和暂停下的操作语义。

### R02：低频统计显示、状态变化才刷新——UI 性能优先项

参考：[Frames.cs:12–30](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/Frames.cs#L12-L30)、[BlockManager.cs:607–634](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/BlockManager.cs#L607-L634)、[MV_Manager.cs:12–25](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/MV_Manager.cs#L12-L25)。

可以借鉴的是**降低显示更新频率、比对状态后再写 UI**，不是它的具体时间公式：

- 本项目 `UI/UserInterface.cs:77–80` 每帧 `Find("Text")`、`GetComponent<Text>()` 并格式化 FPS，而 `FPSCounter` 的结果只约每 0.5 秒更新一次。
- `UI/SimulationInterface.cs:97–100` 在展开统计时每帧构造整段 FPS/部件/关节字符串。
- `UI/PartPropertyPanel.cs:206–241` 每帧读属性、格式化数值、更新多段文字，还反复查找按钮子 Text。
- `UI/PartDrawingPanel.cs:71–75` 每帧查找部件名称标签，即使部件类型和停靠状态未变。

建议先缓存组件引用、上次显示值和非分配的属性快照。静态字段在选择/值/布局变化时更新，动态姿态按实时时间节流，输入框处于编辑状态时不覆盖输入。统计显示跟随既有 0.5 秒采样结果更新，而不是另按“60 帧”节流；这样 30/60/120 FPS、暂停和慢放下显示频率一致。

注意：Unity 的某些 setter 对相同值已经会提前返回。这里确认的是**重复查询、格式化和调用**，不是“每次 setter 都必然触发 Canvas 重建”，实际 Canvas 和 GC 成本仍需测量。

### R03：统一命中查询、复用临时对象——参考的正反面都要看

参考：[FollowMouse.cs:114–142](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/FollowMouse.cs#L114-L142)、[LevelCamera.cs:351–379](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/LevelCamera.cs#L351-L379)。

它缓存了 `PointerEventData`，但仍在每次查询时 `new List<RaycastResult>()`，并在多个脚本重复实现命中检测。**不能整段照搬。**

本项目 `UI/UIExtensions.cs:34–42` 已复用结果列表，但每次调用仍新建 `PointerEventData`；`BpsimInputModule.cs:167–188` 又有自己的按 pointerId 缓存和命中流程。

建议第一刀只改统一扩展方法：复用专用于查询的 scratch `PointerEventData`，跟随 EventSystem 生命周期重建，结果列表在异常路径也要清理。**不复用或 Reset 输入模块正在持有的按压事件对象**，以免丢失 pointerPress/pointerEnter。

跨消费者共享“本帧命中结果”是第二阶段：先确认执行顺序、UI 同帧变化与多指输入，再考虑带指针身份和 UI 版本的缓存。不能只用 `Time.frameCount` 缓存一次就给所有坐标/手指返回同一个结果，也不能改变“按下瞬间决定 UI/画布归属”的既有规则。

### R04：限制追赶步数——物理响应性研究项，不是求解器提速

参考：[TimeManager.asset:6–9](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/ProjectSettings/TimeManager.asset#L6-L9)：固定步长和最大允许时间均为 `0.02` 秒。

本项目固定步长为 `0.02`，项目设置最大允许时间为 `0.33333334`；历史诊断 `docs/BPSIM_GAMEPLAY_PERF_FIX.md` 已记录多次固定步追赶放大卡顿的现象。但 ECS 使用 `FixedStepSimulationSystemGroup`，当前 Entities `RateUtils.FixedRateCatchUpManager` 在 `ShouldGroupUpdate` 读取的是 **`group.World.MaximumDeltaTime`**。不能把 Unity 的 `Time.maximumDeltaTime` 和 ECS 的限额当作同一个开关。

建议先统计每个渲染帧的固定步次数、每步成本和积压的模拟时间，再在现有步长不变的前提下 A/B 测试追赶预算。正常负载必须保留 0.25/0.5/1/2 倍速语义；过载时如何保留积压、何时恢复、是否提示降速，都要显式定义。不能靠降低时间倍率或少推进物理时间来报告“性能变好”。

这里的收益可能是**更快回到输入/UI 循环**，不代表单个物理步更快。当前 `anvil-stability.md` 的验证仍未完成，先收尾既有后端改动，再进行这个实验。

## 3. 已有能力或低优先级项

### R05：短按旋转、方向自动连接、整次操作记撤销——已有，不重复做

参考：[OwnCar.cs:269–303](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/OwnCar.cs#L269-L303)、[OwnCar.cs:645–681](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/OwnCar.cs#L645-L681)。

本项目 `Parts/PartDrawer.cs` 已有短按旋转、0.25 秒长按、8 px 拖动门槛、落位自动对齐和取消时恢复自由变换；`UI/BuildToolController.cs:384–399` 已按笔刷手势记录撤销。不要换成参考项目的固定四方向或 0.2 秒判断，也不移植它的整车字符串存档撤销结构。可借鉴的是把“完成”和“取消”验收分开，补第二指/离开 UI/焦点丢失时的边界测试。

参考的引擎功率列表缓存也不是新需求：本项目 `PartSimulation.cs:95–107` 已按连通块聚合质量、引擎功率和功耗，不应退回逐 MonoBehaviour 同步所有引擎。

### R06：模拟按钮布局——保留本项目池，优化重建临时缓冲区

参考：[ButtonManager.cs:10–23](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/ButtonManager.cs#L10-L23) 清空时销毁按钮、添加时实例化并重排；[StarPool.cs:9–18](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/StarPool.cs#L9-L18) 虽名为 Pool，实际仍是直接 Instantiate。

本项目 `UI/PartButtonList.cs:337–350` 已用 dirty 标记合并刷新，`681–709` 已有 trigger/slider 按钮池；这一点现有实现更适合大载具。

尚可优化的是 `CreateButtons():363–418` 重建时的新 `int[]`、多份 `bool[]`、临时 `List<PartButton>` 及两遍部件扫描。**这些不是每个渲染帧都执行**，只有断裂、拓扑或控制组变化时才可能形成尖峰。先测重建频率与耗时，再复用容量型缓冲区；增量分组和可视区域虚拟化留到确有数据支撑时，不把它们说成参考项目已有机制。

### R07：连接内碰撞过滤——已有优化，另补正确性审查

参考：[OwnCar.cs:377–390](https://github.com/CatBox271/AI-BigCar/blob/2c89b88345c5e861135266b160a83e134edbc245/Assets/C%23/OwnCar.cs#L377-L390) 设置 joint collision，并对特定内部部件对调用 IgnoreCollision。

本项目 `PartCollisionFilter.cs` 已隔离 collider blob，按连通块过滤，并在断裂重分组时重新应用；不需要移植逐 Collider2D 的 IgnoreCollision。

另一个本地已知取舍值得记录：`FilterFor():45–58` 以 `componentIndex % 30` 分配类别，索引 0 与 30 等不同连通块会得到同一过滤位。源码注释已明确承认这可能让某些不相连的碎片不互撞。因此以后应补 **1/29/30/31/90 个连通块**的接触测试，而不是继续扩大过滤范围。是否改成精确 GroupIndex/后端过滤，需要同时验证宽相位开销和各后端语义；本轮不决定替换方案。

## 4. 不应移植的实现（R08）

| 实现 | 不采用的原因 |
| --- | --- |
| 整体 Rigidbody2D/GameObject 架构 | 和本项目 ECS、3D 物理中的 2.5D 约束、批量渲染不兼容；没有大部件数性能证据支持替换 |
| `BlockJoint2D.cs:202` 的 `if (!EnableCollison || true)` | 条件恒真，无论开关都忽略连接体碰撞；不能作为正确的碰撞优化 |
| `BlockManager.cs:569–592` 按 FPS 自动写 timeScale | 把仿真时间变化混成性能改善，不能保持用户选择的倍率；每帧还重写 targetFrameRate，和相机 Awake 的 60 FPS 设置不一致 |
| `OwnCar.cs:233` 的 `GC.Collect()` | 显式全局收集不是解决分配根因的办法，是否产生停顿也必须测量 |
| `CorrectUI.cs:47–61` 每隔固定帧数重排布局 | 不是严格的尺寸/内容 dirty 驱动；响应延迟随帧率变化，不适合直接替代本项目布局逻辑 |
| `AirResistance.cs:43–54` | FixedUpdate 未实现空气阻力施力，不是可用的气动模型 |
| `NewComputeShader.compute` | 仍是默认示例核，不能算物理、UI 或 GPU 优化方案 |
| Locus 编辑器包、游戏贴图/音频和整个 Packages 配置 | 不解决本次运行时瓶颈，且版本/许可边界不同；不增加包、不升级 Unity、不复制资源 |

## 5. 建议实施顺序

1. **先造可回读的 UI/手势基线**，复用现有 QA 和 SpinPerf/AnvilBench；至少两次独立 A/A，区分 Editor 与 Player。
2. **统一 UI 命中查询的临时对象复用**：范围小，先解决明确的分配点，不先做跨模块事件缓存。
3. **属性/统计/部件名 UI 差分更新**：先缓存组件与显示值，再决定动态字段节流频率。
4. **双指中点平移与手势接管**：本轮最有价值的体验改进，但必须先过取消、UI 所有权、鼠标回归和实机门禁。
5. **固定步预算、按钮重建缓冲区和多连通块碰撞审查**：等现有物理改动验收，并由性能/正确性证据决定是否进入生产。

这次检查不支持“照搬 AI-BigCar 就能让物理快几倍”的结论。可执行的方向是**保持当前架构，借交互语义和更新节奏解决本项目自己的已定位问题**。

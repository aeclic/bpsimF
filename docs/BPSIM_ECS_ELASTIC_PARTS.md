# ECS 风箱与被动弹簧补全（2026-11-08）

## 当前状态

**实现已落地，完整宿主源码与非 Editor 条件源码编译通过；真实 Unity 物理/显示验收尚未运行，不据此宣称已完成实机对标。**

本轮只补风箱和普通弹簧。拳套继续使用既有行为，不把它的临时径向冲击当作普通弹簧功能。

## 参考证据

- 直接解析 `bpsimarchive.apk` 的 metadata v31 和 ARM64 ELF 方法表，确认 `Spring` 的独立状态、`SpringEndpoint.Owner` 与 `SpringSystem` 的连接、端点、关节、显示、清理入口。
- APK `BellowsForce` 原始指令确认 0..0.5 秒二次推力曲线。普通/外星的回弹分支也能在原始指令中定位。
- 风箱默认推力采用只读 BPSS 预制体数据：普通 30，外星 #6 为 120；#7 不是外星。此来源与 APK 数学证据分开记录，**没有把 BPSS 或 Windows taffy 的行为说成 APK 动态等价**。
- 可重放仪器：`python Tools/BPSim/inspect_archive_elastic_parts.py --out Logs/archive-elastic-parts.json`。两次独立进程读取得到相同符号/指令结果；只证明静态仪器一致，不证明动态物理一致。

## ECS 分层

| 层 | 风箱 | 普通弹簧 |
| --- | --- | --- |
| 部件数据 | `Bellows`：请求、周期时间、压缩、推力、已交付冲量、原显示缩放 | `ElasticSpring`：连接体、关节、自由端点所有权、显示实体、锚点、断裂阈值 |
| 工厂 | 预制体携带状态，实例化/生成共用 | 预制体携带状态，`SpringVisualization` 烘焙为 `SpringVisual` |
| 输入 | 既有 `BellowsBehavior` 只发请求/回读状态 | 被动件，不再通过触摸放出径向冲击 |
| 系统 | `BellowsSystem` 固定步积分推力、压缩/回弹、拒绝冷却期重触发 | `ElasticSpringSystem` 建立软约束、自由端点、拉伸显示和生命周期清理 |

- 弹簧下端原固定连接换为软约束，上端支撑连接保留；没有下端连接时产生受系统管理的独立端点。
- 自由端点不进入部件并查集；其关节 `PartJointInfo.State=1` 避免宿主自动将非部件注册到部件图。
- 含普通弹簧的初始载具停用本次巨构合并，不修改用户的巨构设置。一个单一刚体无法表示弹簧两端的相对运动。
- 新生成普通弹簧可由 ECS 自动初始化；当前生成验收设计覆盖非巨构路径。巨构运行中新增弹簧的拆分/重新连接不在本轮已验证口径内。
- 软关节建立在蛋效果应用之前；断裂阈值缓存避免每帧覆盖异形胶的无限强度效果。
- 风箱显示缩放乘在原 `PostTransformScale` 上，不覆盖原有非等比缩放。碰撞体压缩、原版音效/粒子不在本轮范围。
- 没有新增按部件挂载的 MonoBehaviour、Update 轮询，也没有修改存档格式/PartType 编号。

## 验证与接续

已通过：

1. 两次 APK 元数据/原始指令读取 PASS。
2. 本机 Unity Roslyn 编译全部当前宿主源码 PASS。
3. 去除 `UNITY_EDITOR*` 宏的完整源码编译 PASS（不是独立播放器构建）。
4. 直接编译生产 `BellowsMath` 的离线探针 **9/9 PASS**：边界、二次曲线、普通/外星回弹、4 个步长总冲量、窗口裁剪。容差为 `1E-4 N·s`，正常风箱总冲量为 `10 N·s`。

待运行：Play Mode 内调用 `Bpsim.QA.ElasticPartsEcsQA.Run()`，异步查询 `Status()`；结果写到 `Logs/elastic-parts-ecs-qa.log`。测试包括真实位移差、显示矩阵压缩与恢复、软约束拉伸、禁止普通弹簧触摸冲击、端点停止/重启清理、生成/退休清理，并恢复原建造场景。

当前编辑器处于 `playing=true, compiling=true`，MCP 拒绝调度，包括退出播放（明确未发送写请求）；Computer Use 原生管道不可用。**需要在 Unity 中手动停止播放，让编译完成后再跑以上实机验收。**没有绕过控制锁、删除缓存、终止编辑器或清空控制台。

## 2026-11-08 continuation: live gate satisfied

The continuation completed real Unity validation: `ElasticPartsEcsQA` 26/26 PASS, preserved in `_codex_work/balloon-sandbag-qa/prior-elastic-final-isolated.log`; source fingerprints in `Logs/suspension-verification-final.json`. The earlier unrun status above is historical, not the current acceptance state. See `docs/BPSIM_ECS_BALLOON_SANDBAG.md` for the continuation's implementation and measured boundaries.
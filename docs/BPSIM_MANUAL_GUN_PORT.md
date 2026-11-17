# 黄色外星吸盘枪：手动机炮（2026-10-25）

对应部件为 `GrapplingHook#5`，皮肤 `Part_GrapplingHook_5_2`。BPRC 的
`Part_GrapplingHook_06_SET.prefab` 使用 `ExplodingGrapplingHook`，并引用 `Hook_6.prefab`。
原 bpsim 将该索引交给抓钩行为，要求先连接目标，再在目标处爆炸；现在改为手动发射弹丸。

- 每次点击立即发射一发；不要求前方有目标，也不会切换成持续自动开火。
- 按 BPRC 默认配置，弹丸质量 0.5、发射冲量 20，折算初速 40；不受重力影响，不继承载具速度。
- 碰撞或飞行 1 秒后爆炸。冷却期间忽略重复点击；命中爆炸或冷却 1 秒后可再次点击开火。
- 爆炸半径 8，冲量基数 25，按 `max(distance, 1)^1.5` 衰减并换算目标质量。
- 复用现有 ECS 弹丸的碰撞扫掠、弹丸显示和爆炸闪光；结束仿真时清理该炮持有的弹丸。
- 普通抓钩和索引 6 的自动机炮保留各自行为。

Unity 2022.2.23f1c1 编译成功，Play Mode 检查 5 项通过：

| 场景 | 验证 |
|---|---|
| manualgunhit | 手动单发、重复点击冷却、命中爆炸推动目标、命中后提前重新开火 |
| manualgunfree | 无目标开火、斜向直飞、不继承速度、约 40.8 格后超时自爆、冷却后重新开火 |
| hook | 普通抓钩仍连接并牵引目标 |
| gun | 自动机炮仍持续发射并命中目标 |
| gunfree | 自动机炮无目标时仍持续发射 |

新增检查入口：`Bpsim.QA.PartGameplayQA.Run("manualgun")`。
完整结果：`Logs/manual-gun-qa.log`。

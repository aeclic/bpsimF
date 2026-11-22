# BPIR 兼容 Mod

gate: passed | mode=直接做 | oracle=BPIR b1c3f9c16b8691612342a31d074933ff7252dfe5 PartPhysicsOverride/Contraption + savefile/Level_Sandbox_04_4 | criteria=Bpsim.QA.BpirImportQA | round=1 | status=running | date=2026-11-18

- 位移：以 BPIR 的格坐标加世界偏移为准。样本有 295 个偏移分量，原导入器会截断其中 25 个；例如 `@offX=-1.5` 必须保持 `-1.5`。
- 绘制层：`@layer` 独立于结构层；按 `-layer * 0.01` 叠加视觉深度，保存再读取后不变。
- 远程连接：坐标 `x:y:layer` 用于序列化，场景中用实体标识跟随移动。启动模拟时建立固定关节并计入连通块；删除后不能自动连接同格新部件。

## 验证结果（2026-11-18）

- L3（运行时）︰隔离 Unity 运行 `Bpsim.EditorTools.BpirImportQaEditor.RunBatch`，退出码 0，98 项通过。报告：`Logs/bpsim-bpir-compatibility-qa.log`。真实样本验证 286 件的最终渲染坐标，其中 108 件有绘制层；合成样本验证跨层固定关节去重、位移的模拟初始位姿、移动与删除目标、撤销、选区复制和四种存档格式。
- L3（运行时）︰原 Mod GUID 不变；预编译 DLL 实际加载，启停不删除绘制层或远程连接的存档数据。
- L2（代码）︰建造期合成位置与缩放共用一次数值校验。此前大档导入基线见 `bpir-import-fidelity.md`；本轮未单测帧时或 Player 性能。
- 未移植：对接合体 `@dockMerge`（宿主没有 BPIR 对接运行时）、可选的「按偏移后位置补连」、BPIR 私有成员/自定义皮肤机制。远程连接目前由属性面板坐标编辑，不改变建造占格，也不复刻 BPIR 的鼠标选点与远程放置模式。

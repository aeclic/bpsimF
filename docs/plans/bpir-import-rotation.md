# BPIR 存档导入：旋转方向映射 + 自动识别格式

日期：2026-11-07。目标是让 BPIR（原版 Innovation Rebuild）导出的存档在 bpsim 里读进来时，每个部件的朝向与原版一致；并且选档时不必事先知道文件是哪一种格式。

## 问题定位

BPIR 存档的 rotation 列（第 5 列）不是统一的角度值。原版每个部件在 `SetRotation` 里自己解释这个数，全部证据来自反编译源 `_codex_work/bpir-1041-reference/Assembly-CSharp/`：

| 部件 | 索引 | rotation 语义 | 取模 |
|---|---|---|---|
| 普通部件 | — | 0..3 = 0/90/180/270 | `%4` |
| GrapplingHook | 39 | 真八向 0..7 = 0/90/180/270/45/135/225/315 | `%8` |
| AutoControlLight | 44#6 | 0..4，4 是「斜型号」贴图 | `%5` |
| HingePlate | 26#4..7 | 0..5 六种铰链布局码 | `%6` |
| FixedWirePart | 47#1,#3 | 0..6 七种布线拓扑码 | `%7` |
| InterfacePart | 47#38,#39 | 0..4 接口朝向码 | `%5` |
| PointChargePart | 47#42 | `%2` 电荷极性（0 正 / 1 负） | `%2` |
| MagneticFieldGenerator | 47#44 | 只认正/反向 | 0/180 |
| ResistorPart / CapacitorPart | 47#6..11 / #15..17 | 只允许 0/90 | `%2` |

原来的导入器只有一条规则：非八向部件的 `rotation >= 4` 就把 45° 折进连续角。这对普通部件是对的，但对上面那些「模式码」部件是错的 —— 5 被读成「1 + 45°」，铰链布局、布线拓扑、电荷极性全部丢失，写回存档也不再是原值。

另一处是 flip：BPIR 的 `BasePart.SetFlipped` 没有白名单，任何一件都能被镜像；bpsim 却用 `CanFlip`（编辑器里「能不能翻」的按钮白名单）当作「导入时收不收 flip」的门，导致 `天使座` 里 4 件 SPDT（`47,5`，flipped=1）的镜像被静默丢掉。

第三处是窄格式：BPIR 的读取器在列数 `>= 8` 时把 members 放在第 13 列、layer 放在第 14 列，列数 `< 8` 时 members 落在第 7 列且没有 layer 列。导入器原来对 6..12 列一律返回空 members，所以 7 列的窄存档会丢成员参数。

## 修改内容

1. **模式码部件单独归一化。** `SchematicsBpirCsvLoader` 新增 `UsesRotationAsModeCode` / `NormalizeModeCode`，按上表把 rotation 折回该部件自己的合法范围，不再折进连续角。铰链布局码 2..5 映射成「下移后 0/90/180/270」，写回时保持 BPIR 的原值语义。
2. **其余非八向部件的 45° 折叠保留。** 普通部件的 4..7 仍然是「再来 45°」，折进 `@rotZ` 等价于原版 `PartPhysicsOverride` 的 `Euler(0,0,rotZ) * gameOrientation`（二维绕 z 轴左右乘等价）。
3. **窄格式读 members。** 7 列时取第 7 列；6 列与 8..12 列按 BPIR 语义本来就没有 members。
4. **flip 不再被 `CanFlip` 挡住。** `BasePart.RotateTo` 改成「只要 flipped 就走镜像分支」，`Rotation` 本身不变；八向分支额外把 y 轴 180° 叠上去。`CanFlip` 白名单原样保留，它仍然只决定编辑器旋转键的行为。
5. **自动识别格式。** 新增 `SchematicsFormat.Auto`、`Schematics.DetectFormat`/`DetectTextFormat`：JSON 看 `{`、XML 看 `<`、带 NUL 视为加密 XML、其余按第一条数据行的列数判断（14 列 → BPIR；13 列看第 13 列是整数还是 members；7 列看第 7 列是浮点还是 members；6 列 → BPIR）。`PartManager.LoadScene` 返回实际用到的格式，`SceneLoader` 与 `LevelSession.LoadSceneFile` 在显式格式失败时按 Auto 重试一次并记进会话；对话框下拉补了「自动识别」项，保存时拦住 Auto 并提示先选具体格式。

## 验证

`Bpsim.QA.BpirImportQA` 从 32 项扩到 **56 项，全部通过**（`Logs/bpsim-bpirimportqa.log`，`done:PASS pass=56 fail=0`）。既有断言逐字未变：解析 286 件、跳过 1 行、层直方图 `l1=121 l2=40 l3=38 l4=35 l5=14 l6=38`、`(143,630)` 的 offset/scale/shape、`(113,633)` 的 rotZ=-90、放置 286 件、五组跨层连接场景。

新增覆盖：

| 断言 | 结果 |
|---|---|
| hinge plate layout code kept | 存档 `26,4 @113,631 rot=5` → 布局码 5 → `rot=3 rotZ=0`（此前会变成 `rot=1 rotZ=45`） |
| hinge plate layout 4 -> 2 | `26,4 @105,630 rot=4` → `rot=2` |
| hinge layout 0 stays | 0/1 保持并排那一组 |
| mode code table | 模式码判定表：`Rope#5`/`ElectricalPart#1`/`#42` 为真，`Rope#2`/`MetalFrame#0` 为假 |
| point charge polarity / resistor %2 / capacitor %2 / magnetic field sign | 合成行逐一验证 `%2`、0/180 归一 |
| narrow csv keeps members | 7 列 `26,4,10,10,0,0,@offX=0.5;@offY=-0.25` → offset 0.5,-0.25 |
| 6 column csv still parses | 6 列仍按无 members 读 |
| detect bpir wide / narrow / members / bpsim csv / json / xml / empty | 七类样本判定 |
| detect every real savefile | 四个真实存档全部判为 BPIR：`Level_Sandbox_04_4` 286、`bpsim1号机` 594、`不败荣光` 1648、`天使座` 20405 |
| auto detect picked bpir / placed parts | `SchematicsFormat.Auto` 加载选中 BpirCsv 且放置 286 件 |
| flip flag survives import | 两件同型号同朝向、只差 flip：`Flipped` 分别为 false/true |
| flip changes orientation | 两件 `LocalTransform.Rotation` 夹角 180°（`float4(0,0,0,1)` vs `float4(1,0,0,-4.37e-08)`） |

## 已知遗留

- **flip 没有视觉镜像。** bpsim 渲染链里没有任何系统把 `PartTransform.Flipped` 变成负缩放或镜像贴图（`PartFreeTransform.Compose` 只叠加 Offset/Scale/RotationDegrees）。所以镜像件的**数据**对了、**连接掩码和哈希**对了，但看上去还是没翻。这是独立问题，不在本次导入映射的范围内。
- **HingePlate 没有视觉实现。** `Rope` 索引 4..7 在 bpsim 里是普通动态刚体（`Rope.IsRope` 只认 `<= 3`），布局码只影响存档 round-trip，不影响画面。
- **Unity batch QA 进不来。** 编辑器开着本项目时 `Unity.exe -batchmode` 会以「another Unity instance is running with this project open」中止（exit 1073741845）；MSYS 下 `cmd //c Tools\*.bat` 也不会真的启动 Unity。验证走 `Assets/Editor/DshBridge` 桥接（`Temp/DshBridge/instance.json` 取端口 → `playmode` → `invoke_static`）。

## 2026-11-14 续轮修复

- `PartManager.LoadScene(..., Auto)` 对不可 seek 流先缓冲再检测和解析；空白/注释-only 文本返回 `None`，不再误走加密 XML loader。
- `TrySetPartLayer` 在改动旧层前检查目标锚点与大型部件 footprint，失败保持层表、部件层号和 `LayerCount` 不变。
- 图层面板的添加层操作切换到新层；删除顶层失败时保留原活动层。连接预览复用模拟期的方向/电气端子判定，并同时检查容器件与被容器件。
- Unity DshBridge 实机验证：`Bpsim.QA.BpirImportQA` **58/58 PASS**（含不可 seek Auto 导入与 comments-only 检测），`Bpsim.QA.LayerConnectionQA` **28/28 PASS**（含大型 footprint 改层拒绝）；编译状态无错误、无警告。

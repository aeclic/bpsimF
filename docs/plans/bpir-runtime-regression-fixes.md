# BPIR runtime regression fixes

gate: passed | mode=改造后做 | oracle=Gitee BPIR 本地只读副本 b1c3f9c16b8691612342a31d074933ff7252dfe5、bpsim Unity runtime QA、现有四份只读存档 | criteria=Bpsim.QA.BpirImportQA.Run + Bpsim.QA.PartGameplayQA.Run("all") + 本轮渲染/履带/阵营专项 QA + Windows BuildReport | round=2 | status=running | date=2026-11-21

## 目标

修复 BPIR 部件行为与渲染/朝向差异，清理阵营描边集成，验证铰接履带和炮塔连接稳定性，并尽可能降低 Windows 发行包体积。参考仓库固定在上述提交；不覆盖、不保存用户存档，不撤销工作区既有改动。

`docs/facts.md` 当前含有其他工作的未提交内容，本轮不写入该文件；本计划保留本轮待证 claim 与验收命令。

## 待证 claim 与判据

| ID | 假设（置信度） | 成立判据 | 反例判据 |
|---|---|---|---|
| BPIR-R1 | 高：无燃料容器时，JetEngine 初始化关闭且用户按钮不可见/不可启动；燃料耗尽后关闭。 | 无油场景初态 `Enabled=false`、不可触发且无触发按钮；接油箱场景可触发并产生推力；关阀或耗尽后发动机关闭。 | 任一无油状态显示触发按钮、变为启用、产生推力，或断油后持续启用。 |
| BPIR-R2 | 高：可调电阻 `ElectricalPart#11` 应显示滑块、不显示通用触发按钮，且滑块值更新电路元件阻值。 | HUD 按钮类型只有 Slider；滑块取值 0.1..10 Ω 写回 `CircuitPart.ValueA`，标准电阻仍不出现滑块。 | 出现 toggle 按钮、滑块不回写，或影响其它电阻档位。 |
| BPIR-R3 | 中：旋转/翻转和渲染深度合成与 BPIR 存档语义一致，染色框透明度不遮住框内贴图。 | 风扇、螺旋桨、钩枪方向向量矩阵与 BPIR 八向方向逐项一致；图层近远关系与序列化回读一致；透明框 alpha 像素处于 `(0,1)` 且内部部件可见。 | 任一方向矩阵不一致、图层顺序反转/丢失，或透明框渲染为全透明/全遮挡。 |
| BPIR-R4 | 中：动力轮可以驱动由铰接板组成的履带，炮塔/火炮连接不会在静置或标准发射负载下意外断裂。 | 固定输入物理回放中履带有大于 0.05 m 的净位移；无非有限状态；静置回放无非预期连接断裂。 | 履带位移不达阈值、关节意外消失，或出现非有限状态/无输入断裂。 |
| BPIR-R5 | 中：长时间模拟没有持续增长的每步耗时/卡死。 | 对同一只读大型存档连续跑 1,200 tick；无超过 1 秒的单 tick 停顿；最后 200 tick 的 P95 不超过最初 200 tick P95 的 2 倍；无异常增长的实体/关节数。 | 任一停顿超时、P95 超限或对象数持续无界增长。 |
| BPIR-R6 | 高：移除阵营描边后，不再生成描边渲染器或相关设置/Renderer Feature，阵营碰撞和玩法标记不受影响。 | 静态扫描无项目侧 EPO/阵营描边集成引用；Unity 编译通过；阵营规则 QA 通过；运行截图无描边像素。 | 任一描边入口/资源仍被项目调用，编译失败，或阵营玩法回归。 |
| BPIR-R7 | 中：Windows LZ4HC 发行构建可显著缩小；Android 的约 70 MB 压缩包不等于 Windows 解压目录大小。 | 使用独立 D 盘输出构建成功，BuildReport 无错误，记录 Player 总字节和发行压缩包字节；压缩配置为 LZ4HC。 | 构建失败、误清理已有输出或尺寸数据缺失。70 MB 为尽力目标，不以牺牲必要插件/运行功能或启用未经验证的激进裁剪达成。 |
| BPIR-R8 | 高：普通与斜向虚化灯的检测区域、0.1 秒/外星框即时切换、碰撞锁、同组件框体碰撞禁用与 25% Alpha，以及关灯恢复行为与 BPIR 相同。 | `PartGameplayQA.Run("light")` 和 `Run("lightfull")` 都断言真实行为状态、灯/框过滤器、框体 `MaterialColor` alpha、按钮视觉和关灯后的原值恢复。 | 普通灯无法发现检测区异体、斜向灯未常亮检测、错误禁用外部框、过滤器/alpha 未恢复、碰撞时仍可切换，或任一 QA 判据缺失。 |

### BPIR-R8 验收记录

- 根因：虚化框 Alpha 在固定步写入后，被 `ColoredFrameSystem` 重置；普通 MonoBehaviour 更新无法保证颜色写入晚于该系统。
- 修复：在 `UpdatePresentationSystemGroup` 之后、`SceneRenderSystem` 之前重写虚化框 Alpha，沿用已缓存的原值以支持关灯恢复。
- `PartGameplayQA.Run("light")`：PASS。检测到检测区异体；两个同组件框的 Alpha 均为 0.25、`CollidesWith=0`；灯自身碰撞关闭；关灯后 Alpha 与碰撞过滤均恢复。
- `PartGameplayQA.Run("lightfull")`：PASS。斜向灯常亮检测；相同框体虚化及关灯恢复断言通过。
- Unity 编译通过，无编译错误；Editor Console 无本次新增错误。

## 执行顺序

1. 先跑现有 BPIR 导入、玩法与渲染 QA，确认当前基线；根据可复现结果修根因，不改动无证据支持的路径。
2. 修正燃料触发资格和可调电阻 HUD 滑块；补足按钮存在性与值写回断言。
3. 用 BPIR 方向 oracle 与真实渲染像素验收风扇/螺旋桨/钩枪朝向、自由绘制层、框体 alpha；只修复失败项。
4. 用固定输入、固定 tick 的履带/火炮运行时探针定位关节丢失或耗时增长；基线未能复现时不做猜测性物理参数调整。
5. 删除阵营描边代码、设置和 Renderer Feature；保留仍有 gameplay 调用的阵营逻辑。
6. Windows 构建写入全新 D 盘目录，启用 LZ4HC，保存 BuildReport 体积数据并核对关键平台插件导入配置。
7. 重跑专项 QA、`Bpsim.QA.BpirImportQA.Run`、`Bpsim.QA.PartGameplayQA.Run("all")`，检查 Unity Console，并记录未能以当前存档/场景覆盖的限制。

## 约束

- 用户存档仅只读导入；不保存、不覆盖。
- 不重置、清理或覆盖工作区里已有的用户改动；特别是已有旋转、DrawLayer、履带、Anvil 和 Windows 构建代码。
- 运行编译、Play Mode、静态 QA 或构建前取得 Unity MCP 独占租约，完成即释放。
- 物理行为没有可复现负例前，不调低全局关节强度、不隐藏断裂事件、不降低模拟步数。

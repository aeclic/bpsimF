# quickbuild 建造交互三项（点按旋转 / 长按拖动 / 落位自动对齐）

参考工程 `BPSS[工程]901-17.0` 的 `ConstructionUI` + `Contraption.AutoAlign` + `BasePart.AutoAlignRotation`，
把「点按部件旋转」「建造界面长按拖动部件」「部件放下后自动对齐方向」移植进本工程。

**全部三项都只在 quickbuild 打开时生效**：入口仍然挂在 `PartDrawer.UpdateQuickBuild` 里，
而 `UpdateQuickBuild` 只在 `!IsSimulating && IsQuickBuildEnabled && !HasActiveBuildTool()` 时才被调用
（`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartDrawer.cs`，判据来自 `InteractionSettings.QuickBuild`，
缺省 `true`）。quickbuild 关闭后走的是原来的 `PlaceOrEraseParts`，这三项都不参与。

## 为什么是这套实现（ECS 约束）

原版 BPSS 是 MonoBehaviour + 每帧 `Instantiate`/`transform` 的老写法，直接照搬会在 DOTS 里制造
每帧托管分配与结构性变更。移植时把三件事分别落到已有的 ECS 通道上，**没有新增任何 System、
没有新增任何组件、没有每帧查询**：

| 交互 | 落盘位置 | 合成链路 |
| --- | --- | --- |
| 点按旋转 | `PartTransform.Rotation` / `.Flipped` | `PartTransformSystem`（已有 `IJobChunk`）→ `BasePart.RotateTo` |
| 长按拖动 | `PartFreeTransform.Offset`（拖动中）、`PartTransform.X/Y`（落位时一次） | `PartFreeTransformSystem`（已有 `IJobChunk`）合成到 `LocalTransform` |
| 落位自动对齐 | `PartTransform.Rotation` / `.Flipped`，在 `PlacePartAt` 内一次 | 同上，无额外帧开销 |

关键点：

- **拖动途中不碰网格表**。每帧只 `SetComponentData<PartFreeTransform>` 把「相对按下点的位移」写进 `Offset`，
  由 `PartFreeTransformSystem` 在 `TransformSystemGroup` 里合成到渲染变换。占位表、邻居刷新、
  附件对齐都不在拖动中触发，松手时用一次 `TryMovePartContent` 落盘。
- **`PartTransform` 是朝向的唯一权威**。`PartSimulator.CanConnectTo` 直接读
  `ExtensionData.ConnectionDirection.Rotate(aspect.Rotation)`，所以旋转只写这个组件，
  不去动 `PartFreeTransform.RotationDegrees`（那是玩家叠加的精细角，无网格模式专用）。
- **自动对齐发生在 `PlacePartAt` 内部**，也就是本来就在 `PlacePartsJob`（`[BurstCompile] IJob`）
  里那一条放置路径上，没有任何额外遍历；判定用 O(1) 的 `m_partMap.TryGet` 查四邻域。
- **拖动中的撤销粒度是一次**：进入拖动时 `BuildToolController.Record("拖动部件")`，
  与 `Move`/`Rotate` 工具同一套 `BuildUndoHistory` 快照机制；点按旋转同理 `Record("旋转部件")`。

## 三项交互的具体语义

### 1. 点按部件旋转（短按）

按下落在已有部件上、没有越过拖动阈值就抬手 → 该部件前进一个朝向单位：

- 普通件：`PartTransform.Rotation += 1`（4 向件 90°，`IsEightWay` 的抓钩/探照灯是 45°，
  由 `BasePart.RotateTo` 现成的 `%8` 分支处理）。
- 可翻转件（机翼/尾翼一类，`BasePart.CanFlip`）：改 `Flipped` 镜像而不是转 90°，
  与 BPSS 的 `Contraption.Flip` 一致（原版对 `FlipVertically` 型走翻转）。

入口是 `PartSceneUnmanaged.TryStepPartRotation(Entity)`。

### 2. 建造界面长按拖动

- 长按阈值 `PartLongPressDuration = 0.25f`（秒），与 BPSS `ConstructionUI.PartLongPressDuration` 同值。
- 拖动阈值 `PartDragThresholdPixels = 8f`，按 `Screen.height / 1080` 缩放并 clamp 到 ≥0.5，
  触屏小屏才不会太钝。
- **长按要求「没动」**：`held = !moved && m_partPressTime >= 0.25f`。慢慢拉一条墙不该在满 0.25 秒时
  突然变成搬箱子，这是相对 BPSS 的一处有意收紧。
- 拖动中部件跟着光标走（写 `PartFreeTransform.Offset`），松手时 `TryMovePartContent(旧格 → 新格)`
  一次性落盘：保持实体身份、被包容件一起移动、两侧 `UpdateNeighbours` 刷新附件；
  然后还原自由偏移并按新邻居重新自动对齐。

### 3. 落位自动对齐（AutoAlign）

`PartCreationRequest` 新增 `AutoAlign` 标志，`PlacePartAt` 在放置成功后调 `TryAutoAlignPart`。
**只有玩家手动放置的路径置位**（`PartDrawer` 的 quickbuild 放置请求 `.WithAutoAlign(true)`），
读档/蓝图路径（`PartCreationRequest(Entity, int, Schematics.Unit)`）恒为 `false`，
否则读一个存档就会把所有部件朝向改掉。

对齐策略（`PartAutoAlign.GetMode`，按 BPSS prefab 里 `m_autoAlign` 的实测分布移植：
0=153、1=174、2=17、−1=1）：

- `Rotate`：绝大多数功能性部件（轮子、气囊、瓶、火箭、伞、风扇、推进器、抓钩、弹簧、TNT、
  齿轮箱、南瓜、灯、沙袋、气球、踢击器、喷气发动机、机械件、油箱）。探测顺序**左 → 右 → 上 → 下**
  （与 BPSS `Contraption.AutoAlign` 同序），找到第一个「朝我开着的连接源」邻居，就把自己转到端子指向它。
- `FlipVertically`：机翼 / 尾翼（`Wings`、`Tailplane`、`MetalWing`、`MetalTail`）。
  右侧是机架 → 不镜像；左侧是机架 → 镜像；两侧都没有 → 保持玩家选择。
- `None`：其余（结构件、猪、容器等）不动。

朝向求解 `PartAutoAlign.TrySolveRotation` 没有照抄 BPSS 的 `(target − source + 4) % 4`：
那套依赖每个部件声明单一 `JointConnectionDirection` 枚举，而 bpsim 的扩展表只存**位掩码**
且允许 `Any`。改成「在掩码内按离当前朝向由近到远搜索最小步长」的候选顺序
`0 → +1 → −1 → 2`（八向件 `0 → +2 → −2 → +4`），第一个让
`terminals.Rotate(candidate) & target != 0` 成立的就是结果；全不命中则不动。
对 `Any` 自然退化为「保持原样」，对单端子件则与 BPSS 的结果一致（QA 里有对照断言）。

判定邻居用的是 `PartExtensionData.IsConnectionSource` + `ConnectionDirection.Rotate(Rotation)`，
与 `PartSimulator.CanConnectTo` 的 target 侧条件完全同义，没有另立一套连接规则。

## 与笔刷的冲突（这是移植里唯一需要新设计的地方）

BPSS 放置靠「从工具栏拖一个幽灵件出来」，按在已有部件上不会被理解成落笔，所以它没有这个冲突。
bpsim 的 quickbuild 是**按住就刷**，如果按下即判定「拎起部件」，玩家从已有墙上接着画时会变成把墙拖走。

所以加了一条纯策略 `PartDrawer.DecidePartPress(moved, held, paintable)`：

```
held                        → Drag    （停住满 0.25 秒 = 搬）
moved && !paintable         → Drag    （手里这个件本来就放不到这一格，移动只能是搬）
moved && paintable          → Paint   （能叠着放，说明这一格是笔刷的合法落点 = 接着画）
否则                        → None    （继续等）
```

`paintable = BasePart.IsStructural(选中件)`（框/绳/弹簧可以压在已有部件上）。`Paint` 分支会
`CancelPartInteraction` 并把这一下**还给笔刷**（`UpdatePartInteraction` 返回 `false`），
走原来的 `PlacePartsJob`。

另外两条按下即放行的情形：开了 `PartDrawingSettings.Overlay`（笔刷本来就要压掉已有部件），
以及「空框 + 手里是箱子」（`CanBeContained`，这一下是把箱子塞进框里）。

这条规则被抽成 public static 纯函数正是为了让 QA 能直接断言它的每个分支——它改错了不抛异常，
只表现为手感变差，属于必须锁住的不变式。

## 验收

`Assets/Scripts/Assembly-CSharp/Bpsim/QA/QuickBuildInteractionQA.cs`（`#if UNITY_EDITOR`，Play 模式探针，
与 `LayerConnectionQA` / `FreeTransformQA` / `BuildToolbarQA` 同一套写法）：

```
invoke_static type=Bpsim.QA.QuickBuildInteractionQA method=Run    args=[]
invoke_static type=Bpsim.QA.QuickBuildInteractionQA method=Status args=[]
```

结果写 `Logs/bpsim-quickbuildqa.log`。**当前 54 项全通过（pass=54 fail=0）**，覆盖五层：

1. `PartAutoAlign` 策略表 + 朝向求解（含八向件、负朝向归一、`Any` 退化、就近优先）；
2. `PartCreationRequest.AutoAlign` 的默认值 / `WithAutoAlign` / 链式保持 / Schematics 路径恒 false；
3. `DecidePartPress` 的全部九个分支（含「铁框刷同一格 = 接着画」「轮子刷铁框 = 拖走」对照）；
4. `PlacePartAt` 的落位自动对齐：Rotate 型端子指向左邻、幂等（再对齐一次不抖动）、
   无邻居时保持原朝向；FlipVertically 型的右侧机架不镜像 / 左侧机架镜像 / 无机架保持；
5. `TryStepPartRotation`（普通件 +1、可翻转件只翻镜像不转角度、`Entity.Null` 拒绝）与
   拖动落位（保持实体与网格、落位后重新对齐、目标被占用时拒绝、结构件可叠放）。

断言写的是「端子现在指向邻居」这个不变式（即 `PartSimulator.CanConnectTo` 的 target 侧条件），
而不是求解器自己的中间结果，所以换搜索顺序也不会自证通过。

## 已知未覆盖

本仓库缺关卡二进制（`LevelLoader.Awake()` 在 `LevelLoader.cs:110` 对
`m_data.LoadValue<TextAsset>()` 返回 null → NullReferenceException），**进不了关卡**，
所以没有做真实指针注入的端到端验证。输入层由两部分组合而成：
`DecidePartPress`（纯函数，QA 逐分支覆盖）+ 已覆盖的数据层原语；
`UpdatePartInteraction` 里剩下的只是把它们接起来（阈值比较、`Record`、调用顺序）。
进关卡后应人工复核手感：墙中间起笔是否仍为接着画、从墙上长按后能否拖走。

## 改动清单

| 文件 | 改动 |
| --- | --- |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartAutoAlign.cs` | 新建。`PartAutoAlignMode` 枚举、`GetMode`、`IsChassis`、`TrySolveRotation` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartCreationRequest.cs` | 新增 `AutoAlign` 字段与 14 参主构造、`WithAutoAlign`，链式方法全部透传 |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartSceneUnmanaged.cs` | `PlacePartAt` 末尾按标志调 `TryAutoAlignPart`；新增 public `TryAutoAlignPart(Entity)`、private `TryAutoAlignPart(Entity,int,int,int)`、`TryFindConnectionTarget`、`IsConnectionSourceFacing`×2、`IsChassisAt`、`SetPartFlipped`、`TryStepPartRotation(Entity)` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartDrawer.cs` | 新增 `PartPressAction` 枚举 + `DecidePartPress` 纯函数 + `UpdatePartInteraction` 全套状态机（按下 / 长按 / 拖动 / 短按旋转 / 取消还原）；quickbuild 放置请求加 `.WithAutoAlign(true)` |
| `Assets/Scripts/Assembly-CSharp/Bpsim/QA/QuickBuildInteractionQA.cs` | 新建。54 项验收断言 |

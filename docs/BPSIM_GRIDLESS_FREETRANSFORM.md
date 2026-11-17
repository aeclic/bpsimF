# 无网格建造与自由变换（ECS 移植）

把主项目 BPRC 的「无网格建造（Gridless）」与「移动 / 旋转 / 缩放」系统，按 ECS 数据驱动的方式移植到 bpsim-main。
主项目实现是 MonoBehaviour + 字符串内部参数（`@gridlessX` / `@gridlessY` / `@fineAngle` / `PartScaleX` / `PartScaleY`）；
本项目改为**组件 + 系统 + 11 列存档**，网格占用与连接逻辑保持不变。

## 数据模型

新增 `Bpsim.Parts.PartFreeTransform`（`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartFreeTransform.cs`），挂在部件根实体上：

| 字段 | 含义 |
| --- | --- |
| `float2 Offset` | 叠加在格坐标上的世界空间位移（不改变 `PartTransform.X/Y`，也不改网格表） |
| `float RotationDegrees` | 叠加在离散朝向之上的连续角（精细旋转） |
| `float2 Scale` | 根实体附加缩放，默认 `(1,1)`，钳制 `0.05..20` |

`PartTransform` 仍是唯一的网格身份：整数格 + 4/8 向朝向 + 翻转。
自由变换与网格解耦，因此现有移动、旋转、连接、存档、蓝图逻辑不受影响。

合成由 `PartFreeTransformSystem` 完成（`[UpdateAfter(typeof(PartTransformSystem))]`，`TransformSystemGroup`）：

```
LocalTransform.Position += Offset
LocalTransform.Rotation  = Rotation * RotateZ(RotationDegrees)
PostTransformScale       = Scale(Scale.x, Scale.y, 1)
```

模拟中（`PartManager.IsSimulating`）系统停手，物理继续使用 `PartSimulator` 写出的位姿；
缩放只影响渲染与建造视角，**不会改变 PhysicsCollider**（见「限制」）。

## 存档

`Schematics.Unit` 追加 5 个字段：`OffsetX / OffsetY / RotationDegrees / ScaleX / ScaleY`。

- JSON / XML：旧档缺字段时读成 0（缩放归一化为 1），新档写出完整字段。
  XML 属性名为 BPRC 兼容的 `gridlessX / gridlessY / fineAngle / partScaleX / partScaleY`。
- CSV：由 6 列扩为 11 列（`type,index,x,y,rotation,flipped,offsetX,offsetY,rotationDegrees,scaleX,scaleY`），
  读取端同时接受旧 6 列；浮点用 invariant + `"R"` 保证往返精度。
- `BuildUndoHistory` 的整场快照走同一套 `SaveParts / PlaceParts`，因此撤销天然覆盖自由变换。
- 运行时 `PartCreationRequest` 携带这 5 个值，`Submit` 时补齐 / 写入 `PartFreeTransform` 与 `PostTransformScale`。

## 建造操作

顶部工具栏新增：

- **部件变换**：单击选中部件后显示 gizmo。中心自由拖动、红/绿箭头单轴平移、外圈圆环旋转。
  - `Shift`：平移吸附 0.5 格、旋转吸附 15°。
- **自由变换**：在部件变换基础上增加四角 / 四边手柄。
  - 拖动角：以对角为锚点双向缩放；`Shift` 保持比例。
  - 拖动边：单轴缩放。
- **载具变换**：以全场包围盒中心为枢轴的整车自由变换。中心平移、圆环旋转、角手柄等比缩放。
  - 平移按世界增量；旋转绕包围盒中心；缩放以中心为枢轴按距离比。
- **无网格**：开关当前场景的 `PartScene.GridlessEnabled`。开启后单击放置不再吸附到格心，
  指针与格心的差值（钳制 ±0.49）写入 `PartFreeTransform.Offset`。

所有 gizmo 都只改 `PartFreeTransform`，不改网格表；90° 整转仍由原来的
`TryRotateAllParts` 负责（它会重排网格），自由旋转不会。

选择是工具级的：切到「载具变换」或其它非部件工具会清空当前选中件（gizmo 由全场包围盒驱动，不需要选择）；
切回部件工具后点击部件重新选中。`BuildToolController.SelectPart(Entity)` 是给 QA / 外部选择面板用的入口。

## 改动文件

| 文件 | 内容 |
| --- | --- |
| `Bpsim/Parts/PartFreeTransform.cs` | 新组件、`Identity`、逐轴 `Sanitize`、`MinScale/MaxScale` |
| `Bpsim/Parts/PartFreeTransformSystem.cs` | 在 `PartTransformSystem` 之后合成位置 / 连续角 / `PostTransformScale` |
| `Bpsim/Parts/PartSceneUnmanaged.cs` | 自由变换读写 + 整车平移 / 旋转 / 缩放 + 视觉包围盒 |
| `Bpsim/Parts/PartCreationRequest.cs` | 请求携带 5 字段，`Submit` 补组件与缩放矩阵，并给根实体补 `PropagateLocalToWorld` |
| `Bpsim/Parts/PartEntityFactory.cs` | 模板实体（所有部件的唯一来源）根节点挂 `PropagateLocalToWorld` |
| `Bpsim/Parts/Schematics.cs` / `Schematics*Loader.cs` | 11 字段模型、JSON/XML/CSV 兼容读写、旧档归一化 |
| `Bpsim/Parts/PartSceneJobs.cs` / `PartManager.cs` | 快照保存读取 5 字段 |
| `Bpsim/Parts/PartDrawer.cs` | 无网格单击放置写指针偏移（±0.49）；长按拖动按下格取部件自身格坐标 |
| `Bpsim/Parts/PartPicking.cs` | 视觉命中几何：视觉中心 / 视觉角度 / 局部格矩形 / 世界点逆变换判定 |
| `Bpsim/Parts/PartSceneUnmanaged.cs`（拾取） | `TryFindPartAtWorld` 按视觉形状拾取 |
| `Bpsim/Parts/PartScene.cs` | `GridlessEnabled` |
| `Bpsim/UI/BuildTool.cs` | `TransformPart / FreeTransformPart / TransformMachine` |
| `Bpsim/UI/PartGizmoMath.cs` | 屏幕空间命中、gizmo 框 / 轴 / 角 / 边 / 锚点数学 |
| `Bpsim/UI/BuildToolController.cs` | 工具状态机、选中、拖动平移/旋转/缩放、Shift 吸附、懒记录撤销 |
| `Bpsim/UI/PartTransformGizmo.cs` | 世界空间 LineRenderer gizmo（箭头 / 圆环 / 包围盒 / 手柄） |
| `Bpsim/UI/BuildToolbar.cs` | 三个工具按钮 + 无网格开关 + 按工具映射刷新图标 |
| `Assets/Resources/BuildToolbar/Icon_{TransformPart,FreeTransformPart,TransformMachine,Gridless}.png` | 新图标与 Sprite meta |
| `Bpsim/QA/FreeTransformQA.cs` + `Assets/Editor/FreeTransformQaEditor.cs` | 67 项验收与 batch 驱动 |
| `Bpsim/QA/FreeTransformDemo.cs` | Play 模式视觉演示探针 |

## 运行时 API（`PartSceneUnmanaged`）

| 方法 | 作用 |
| --- | --- |
| `GetPartFreeTransform(Entity)` | 读取（缺组件返回 Identity） |
| `TrySetPartFree(Entity, in PartFreeTransform)` | 整体写入（自动 Sanitize；缺 `PostTransformScale` 时补上，保证系统查询覆盖） |
| `TryMovePartFree(Entity, float2)` | 设置偏移（绝对） |
| `TryRotatePartFree(Entity, float degrees)` | 设置连续角（叠加在离散朝向上） |
| `TryScalePartFree(Entity, float2)` | 设置缩放（钳制） |
| `TryTranslateAllPartsFree(float2)` | 全场按世界增量平移 |
| `TryRotateAllPartsFree(degrees, pivot)` | 全场绕世界枢轴旋转，并累加各件连续角 |
| `TryScaleAllPartsFree(factor, pivot)` | 全场绕枢轴等比缩放 |
| `TryGetPartsBounds(out min, out max)` | 视觉包围盒（含偏移与缩放） |
| `GetPartsCenter()` | 视觉包围盒中心 |
| `TryFindPartAtWorld(float2 world[, int layer], out Entity)` | 按视觉形状拾取（点选 / 拖动 / 双击都用它） |

## 按视觉位置拾取

无网格平移只写 `PartFreeTransform`，`PartTransform.X/Y`（整数格）不变。以前所有建造拾取都走
`FindPartGridAt(round(指针))`，于是「部件看起来挪走了，却还得点原来的格子」才能选中/拖动/开参数面板。
现在拾取改成把世界点按 `PartFreeTransformSystem` 同一套变换**逆回去**再判定：

```text
pivot   = (X, Y) + Offset
degrees = 90°·Rotation（八向件按 RotateTo 的 45° 偏移）+ RotationDegrees
local   = Rotate(world - pivot, -degrees) / Scale
hit     = |local - boundsCenter| <= boundsHalf        // bounds 来自 BasePart.GetGridRect
```

- `Bpsim/Parts/PartPicking.cs`：上面这套纯函数（`GetVisualCenter` / `GetVisualRotationDegrees` /
  `GetLocalBounds` / `TryGetLocalPoint` / `Contains`），不依赖 ECS，可单独断言。
- `PartSceneUnmanaged.TryFindPartAtWorld`：遍历当前层部件，命中者取视觉中心最近的一个；
  容器（框）加一点惩罚，避免外框抢走框里的件。**不回落网格查询** —— 旧格必须真的点不到。
- 调用点：`PartDrawer.UpdatePartInteraction`（快速建造的长按拖动 / 短按旋转，按下格取部件自身
  `PartTransform`，松手按「按下格 + 指针位移」折算目标格）、`BuildToolController.BeginDrag /
  SelectPartAt`（拖动 / 部件工具选中）、`PartOperator.OnScreenDoubleClicked`（双击开参数面板）。
- 框选（`PartDrawer.UpdateSelectionState` → `SelectPartsJob`）仍是格坐标矩形，未改：它是区域语义，
  与「点这个部件」不同。

验收：`Bpsim.QA.FreeTransformQA` 里 `CheckPartPicking` 覆盖单格 / 偏移 / 缩放 / 3×2 大件 / 90° 自由旋转
的命中与不命中，并在真实场景里断言「点视觉中心能选中、点旧格选不到」。

## 缩放必须传到子实体（拳套 / TNT 在建造界面不缩）

自由缩放写在**部件根实体**的 `PostTransformScale` 上（`float3x3`，可以是非等比）。
Unity.Transforms 的默认子物体路径 `ChildLocalToWorldFromTransformData` 只认父实体的
`WorldTransform`，而 `WorldTransform.Scale` 是**单个等比 float** —— 根的非等比缩放会被整条丢掉。
只有父实体带 `PropagateLocalToWorld` 时，子节点才改走
`ChildLocalToWorldFromTransformMatrix`（`parentLocalToWorld * localTransform`），把根的非等比缩放带下去。

- 部件模板的根物体是 `ManagedPart` 所在的那个物体（`Part_TNT_0`、`Part_SpringBoxingGlove_0` …，
  由 `PartFactory.BuildParts` 从 `PartGroup_*` 的子物体取），外观挂在它**子物体**上的部件
  （拳套的 `Visualization` / `SpringVisualization`、TNT 的 `Visualization`、车轮的 `WheelSprite`）
  以前只在**模拟期**拿到这个标签（`PartPhysics.EnableGraphicalInterpolation`），
  所以「运行界面正常、建造界面不缩」。铁框这类渲染器直接在根上的部件本来就没事，
  这就是用户说的「部分部件」。
- 重复添加是安全的：`EntityManager.AddComponent` 在组件已存在时返回 `false`，不会抛异常
  （`EntityDataAccess.AddComponentDuringStructuralChange` 先 `HasComponent` 早退），
  所以模拟期的 `EnableGraphicalInterpolation` 不需要改成幂等。
- 修法：在建造期也加上。`PartEntityFactory.Create`（唯一模板创建点，`PartEntitySpawner` 调用）
  给根实体 `AddComponent<PropagateLocalToWorld>`，所有 `Instantiate` / 模拟克隆自动继承；
  `PartCreationRequest.Submit` 与 `PartSceneUnmanaged.TrySetPartFree` 再各补一次 `HasComponent` 兜底
  （覆盖玩法脚本直接 `Instantiate` 的件）。
- 性能代价：带子物体的根走矩阵路径（略慢于 `WorldTransform` 路径），部件数量级下可忽略；
  无子物体的根加了标签也只多一次 `chunk.Has` 判断。

验收：`FreeTransformQA.CheckChildVisualScale` 放一个 TNT，断言建造期（未进模拟）子渲染实体的
`LocalToWorld` 轴长从 `(1,1)` 随 `free.Scale=(2,1)` 变成 `(2,1)`。

## 验收

Play 模式探针 `Bpsim.QA.FreeTransformQA`（`Assets/Scripts/Assembly-CSharp/Bpsim/QA/FreeTransformQA.cs`）：

- 默认值 / 非法值归一化；
- 自由平移 / 旋转 / 缩放，且网格表不变；
- `PartFreeTransformSystem` 的 ECS 合成（`LocalTransform.Position/Rotation` 与 `PostTransformScale`）；
- 整车平移、绕枢轴旋转、绕枢轴缩放；
- 存档 11 列、CSV/JSON 往返、旧 6 列 CSV 与旧 JSON 归一化；
- 撤销恢复偏移与缩放；
- gizmo 屏幕命中（中心 / 角 / 边 / 箭头 / 圆环）；
- 视觉拾取（单格 / 偏移 / 缩放 / 大件 / 旋转），以及场景内「视觉中心命中、旧格不命中」；
- 整车缩放传到子渲染实体（TNT 的子物体 `LocalToWorld` 轴长跟随 `free.Scale`）。

运行方式（DSH 桥接 8791）：

```text
compile
playmode {"action":"enter"}
invoke_static {"type":"Bpsim.QA.FreeTransformQA","method":"Run"}
invoke_static {"type":"Bpsim.QA.FreeTransformQA","method":"Status"}
```

也可以走 batchmode 驱动（不需要打开编辑器；结束自动退出）：

```text
env -u NO_PROXY -u no_proxy -u HTTP_PROXY -u http_proxy -u HTTPS_PROXY -u https_proxy \
    -u NODE_USE_ENV_PROXY "<Unity>/Editor/Unity.exe" -batchmode \
    -projectPath "<project>" \
    -executeMethod Bpsim.EditorTools.FreeTransformQaEditor.RunBatch \
    -logFile "Logs/freetransformqa-editor.log"
```

结果（`Logs/bpsim-freetransformqa.log`）：`done:PASS pass=67 fail=0`。
报告覆盖了偏移/角度/缩放的 ECS 合成（位置 `(0.37,-0.24,-0.1)`、角度 33°、`PostTransformScale` 对角 `(1.5,0.75,1)`），
整车平移后首件 offset `(1.62,-0.74)`、绕原点转 90° 后 `(0.74,1.62)` 且连续角 123°、再放大 2 倍后 `(1.48,3.24)` / `Scale=(3,1.5)`。

视觉验收用 `Bpsim.QA.FreeTransformDemo`（`Assets/Scripts/Assembly-CSharp/Bpsim/QA/FreeTransformDemo.cs`）：
进入 Play 后调用 `Setup` 会建一个空场景、摆两个铁框、打开无网格并选中首件；再用 `Tool(6/7/8)`
切换部件变换 / 自由变换 / 载具变换即可截图核对 gizmo 与工具栏状态；`Transform(...)` 可直接给选中件
设置偏移/角度/缩放。该探针只编译进编辑器（`#if UNITY_EDITOR`）。
既有工具栏 QA（`BuildToolbarQA`，已扩到 105 项）在同一份代码上全绿，新增的三个工具按钮与无网格开关没有破坏原布局/提示。

视觉证据（交互式 Play 截图）：

| 图 | 内容 |
| --- | --- |
| `_codex_work/qa/freetransform/v2_part.png` | 部件变换：中心十字 + 红/绿箭头 + 圆环 |
| `_codex_work/qa/freetransform/v2_free.png` | 自由变换：在部件变换上追加四角/四边手柄 |
| `_codex_work/qa/freetransform/v2_machine.png` | 载具变换：全场包围盒 + 中心箭头 + 圆环 + 角手柄 |
| `_codex_work/qa/freetransform/v2_transformed.png` | 自由偏移 0.4/-0.3、旋转 25°、缩放 1.3/0.8 后的合成结果 |

## 限制 / 后续

- **物理不缩放**：碰撞体来自 prefab，`PostTransformScale` 只影响渲染；模拟中撞墙用的是原尺寸。
  若要物理缩放，需要在 `PartSimulator` 里按 `Scale` 重建 collider（开销大，且 compound collider 需要重烘焙）。
- **网格仍按整数格登记**：无网格位移后，占格表与视觉位置脱钩。这是 BPRC 的既有语义，
  也是拖拽/放置/碰撞查询的基础；整格移动仍走原 `TryMovePartContent`。
  建造模式的**拾取**已经不再依赖这张表（见上文「按视觉位置拾取」）：点选 / 拖动 / 双击都按合成后的
  视觉形状命中，只有放置、连接查询和框选仍以整数格为准。
- **假定 prefab 根缩放为 1**：`PartFreeTransformSystem` 每帧写 `PostTransformScale` 承载自由缩放，
  会覆盖根实体上已有的非均匀基础缩放。当前所有部件 prefab 根都是 `(1,1,1)`（视觉缩放都在子物体）；
  若将来引入非均匀根缩放的 prefab，需要把基础矩阵并进 `PartFreeTransform.Scale`。
- **未移植** 主项目的无网格分组（`@machineGroup` / `@machineLattice`）、组吸附、组间合并
  （`GridlessGroupMerge`）、无网格重叠碰撞（`GridlessOverlapCollision`）与选择提取。
  它们依赖 BPRC 的载具层级与连接系统，需要一个独立的组组件 + 组系统，当前版本只保留整车级变换。
- `PartScene.GridlessEnabled` 是场景级运行时开关，不写入存档；重新载入场景回到默认关闭。

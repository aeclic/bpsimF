# 安卓触控失效（按键/按钮全部点不动）修复记录

**症状.** 打包到安卓后，**所有 UI 按钮和按键都点不动**：顶部工具栏、左侧部件栏、
HUD 的播放键、模拟里的部件按钮、设置面板……鼠标在桌面完全正常。
反而是「长按 0.5 秒出提示条」这类效果有反应，所以容易被误判成"触控没坏，是按钮逻辑坏了"。

## 根因（两层，第二层是主因）

### 1. 主因：`BpsimInputModule` 只认鼠标

`Assets/Scripts/BpsimCompat/BpsimInputModule.cs` 是本工程从 release 包反编译出来的
`InputSystemUIInputModule` 的替代品。它出现的原因见文件头注释：本工程的 uGUI 是反编译的
`Assets/Plugins/UnityEngine.UI.dll`，没有 `com.unity.ugui` 包，于是 Input System 包的 asmdef
不会定义 `UNITY_INPUT_SYSTEM_ENABLE_UI`（该 define 来自
`Library/PackageCache/com.unity.inputsystem@1.7.0/InputSystem/Unity.InputSystem.asmdef` 里
`name: com.unity.ugui` 那条 versionDefine），`InputSystemUIInputModule` 这个类在本工程
**根本不会被编译**，EventSystem 就会缺输入模块。

替代品的第一版长这样：

```csharp
public override void Process()
{
    Mouse mouse = Mouse.current;
    if (mouse == null) return;   // ← 安卓上这里直接返回
    ...
}
```

安卓上没有鼠标设备，`Mouse.current == null`，`Process()` 第一句就返回 —— EventSystem
一个指针事件都收不到，整块 UI 对触摸全哑。而长按提示条、画布拖动这些代码是直接读
`Touchscreen.current` / `EnhancedTouch` 的，绕过了 EventSystem，所以它们还能动，这正好
造成"只有按钮不灵"的错觉。

### 2. 致命细节：`Touchscreen.touches` 里**没有** primaryTouch

补触屏时如果只遍历 `Touchscreen.current.touches`，仍然点不动按钮：
`Touchscreen.FinishSetup()` 明确把 `primaryTouch` 从数组里剔掉
（注释原文 *"Keep primaryTouch out of array"*）。单指点击 UI 时那根手指**就是**
`primaryTouch`，数组里是另外 9 个备用槽位。必须单独处理 `primaryTouch`。

第一次按"只遍历 touches"实现时，QA 报的就是 `TOUCH CLICK LOST`
（`primaryTouch in touches = False`），补上后才 `TOUCH CLICK OK`。

### 3. 顺带修掉的 EnhancedTouch 开关

`SceneScreen.Awake` 原来只在 `Touchscreen.current != null` 时才
`EnhancedTouchSupport.Enable()`，而 `SimulationInterface` 从来没开过（它却每帧读
`Touch.activeTouches`）。设备注册稍晚 / Awake 顺序变化，触屏上就会表现为"画布拖不动、
部件点不了"。现在两边都改成**需要时补开**（`EnsureEnhancedTouch()`，已开时只是一次 bool 检查）。

## 修复内容（3 个文件 + 1 个 QA 场景）

### `Assets/Scripts/BpsimCompat/BpsimInputModule.cs`（重写 `Process`）

- 引入 per-pointer 状态 `PointerState`（mouse 用 `pointerId = -1`，手指用 `touchId`），
  一条 `ProcessPointer()` 同时服务鼠标与每个手指，支持**多指**（每根手指一个 uGUI 指针）。
- `Process()` 每帧：先处理 `touchscreen.primaryTouch`，再遍历 `touchscreen.touches`；
  手指按着时不再喂鼠标（部分设备会把触摸再合成一份鼠标事件，两边都处理＝一次点击触发两次）。
- **不再调用 `PointerEventData.Reset()`**：它会清掉 `pointerPress`/`pointerEnter`，导致
  release 那帧找不到按下的对象（click 永远发不出去）、悬停的 `PointerExit` 也不会补发。
  现在只逐帧刷新 `position`/`delta`/`scrollDelta`，按下/抬起字段完全由 `Press()`/`Release()` 管理。
- `Press()` 先走 `ExecuteHierarchy(pointerDownHandler)`（Slider / Toggle / InputField 需要），
  取不到再退回 `GetEventHandler<IPointerClickHandler>`；`pointerDrag` + `initializePotentialDrag` 齐全。
- `Release()` 依次 pointerUp → pointerClick（同目标且 eligibleForClick）→ endDrag。
- 抬起判定除了 `wasReleasedThisFrame` 还认「上一帧跟踪过这个 touchId」，避免手动
  `InputSystem.Update()`（QA 脚本）时 click 被吞。
- **系统取消触摸**（`TouchPhase.Canceled`，如下划通知栏、边缘返回手势）走 `allowClick: false`：
  照常收尾 pointerUp/endDrag，但**不发 click**，避免误触。
- 触摸设备消失（热插拔 / 切后台）时 `PruneTouchStates()` 清掉残留手指状态，
  避免回来后有一个"一直按着"的幽灵指针挡住所有点击。
- 保留 `BpsimInputGuard`：仅当 EventSystem 上一个 `BaseInputModule` 都没有时才挂本模块，
  将来若把工程迁到 `com.unity.ugui` 包、恢复真正的 `InputSystemUIInputModule`，这里自动让位。

### `Assets/Scripts/Assembly-CSharp/Bpsim/UI/SceneScreen.cs`

`Awake` 无条件 `EnsureEnhancedTouch()`；`GetPointer()` 的触屏分支开头也补一次。

### `Assets/Scripts/Assembly-CSharp/Bpsim/UI/SimulationInterface.cs`

新增 `EnsureEnhancedTouch()`，在 `Awake`、`IsPointerPressed()`、`GetPointer()` 三处调用。

### `Assets/Scripts/Assembly-CSharp/Bpsim/QA/BpsimPlayQA.cs`（验收探针）

新增 `touch` 场景：桌面没有触摸屏，所以临时 `InputSystem.AddDevice<Touchscreen>()`，
用 `InputSystem.QueueStateEvent(touchscreen, new TouchState { phase = Began/Ended, position = 按钮中心 })`
模拟一次手指点击，断言 `PartOperator.Mode` 真的变了；结束后 `RemoveDevice`。

```
invoke_static type=Bpsim.QA.BpsimPlayQA method=Run    args=["touch"]
invoke_static type=Bpsim.QA.BpsimPlayQA method=Status args=[]
```

## 验收证据（Unity 2022.2.23f1c1，Play 模式，DshBridge 8791）

```
touch: touchscreen = Touchscreen/Touchscreen (synthetic=True)
touch: primaryTouch in touches = False
touch: tap PlaceButton at (225.43, 1078.39)
touch: after Began -> press=True phase=Began pos=(225.43, 1078.39) current=True
touch: module frames=5 presses=1 releases=1 module=EventSystem -> BpsimInputModule
touch: PlaceButton at (225.43, 1078.39) mode View -> Place  [TOUCH CLICK OK]
```

鼠标回归（同一模块，`input` 场景）：

```
input: mode after synthetic PlaceButton click = Place
input: selector=Pig/0 mode=Place parts before=7 after canvas click=8 (wanted WoodenFrame)
```

编译：`error CS` 0 条，`*** Tundra build success`, `CopyFiles Library/ScriptAssemblies/Assembly-CSharp.dll`；
`missing-scripts: objects=2314 behaviours=2661 missing=0`。

## 改这块代码时的注意事项

- **不要**在 `Process()` 里只读 `Mouse.current`；也**不要**只遍历 `Touchscreen.current.touches` ——
  `primaryTouch` 不在里面（见上）。
- **不要**给 `PointerEventData` 调 `Reset()` 后再依赖上一帧的 `pointerPress`；按住期间需要跨帧保存的
  字段一律自己管。
- 诊断用静态成员：`BpsimInputModule.DebugTouch`（开日志）、`DebugPressCount` / `DebugReleaseCount` /
  `DebugTouchFrameCount`（QA 区分「事件没进来」和「进来了但没命中」）。
- 触摸是**按下边缘**语义：游戏里部件按钮（`PartSimulation.ProcessTouch`）只在
  `pointer.IsPressed && !m_pointerDown` 那一帧触发，别改成每帧转发，否则按住机炮会一帧触发一次。

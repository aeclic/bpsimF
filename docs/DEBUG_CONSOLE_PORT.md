# Debug 控制台移植记录（从父工程 BPRC 2026.3.2 proj）

把父工程里的 **UnityIngameDebugConsole v1.9.0**（含它自己的接线层）搬进 bpsim-main，
并按「设置里开关、关掉就一点开销都没有」的要求重新接线。

验证结果：`Logs/debugconsole-probe.txt` → `FAILURES: 0`（约 40 项 PASS，含两次截图）。

---

## 1. 三个工程的差异（为什么不能直接拷）

| | 父工程 `BPRC 2026.3.2 proj` | bpsim-main |
|---|---|---|
| uGUI 来源 | `com.unity.ugui` 包（源码 guid） | `Assets/Plugins/UnityEngine.UI.dll`（precompiled） |
| TMP | DLL `Unity.TextMeshPro.dll`（2021.3.45） | DLL 同名但 2022.2.23 版；**运行时资产全缺** |
| 渲染管线 | Built-in | **URP 2D** |
| `activeInputHandler` | 2（Both） | **1（Input System only）** |
| 事件系统 | 场景里自带 | **运行时由 `BpsimInputModule` 建** |

由此产生四处必须改的地方，见下面第 3 节。

## 2. 搬过来的东西

### 2.1 插件本体（整目录 + 全部 `.meta`）

`Assets/Plugins/IngameDebugConsole/`（1.3 MB，guid 与父工程一致，prefab 里的
`m_Script` 才能正确解析）：

```
IngameDebugConsole.Runtime.asmdef   IngameDebugConsole.prefab   README.txt
Editor/    DebugLogManagerEditor.cs + IngameDebugConsole.Editor.asmdef
Prefabs/   DebugLogItem.prefab  CommandSuggestion.prefab
Scripts/   13 个 .cs + Attributes/3 个
Sprites/   15 个 .psd + IngameDebugConsoleSpriteAtlas.spriteatlas
Android/   DebugLogLogcatListener.cs + IngameDebugConsole.aar
WebGL/     IngameDebugConsole.jslib
```

### 2.2 TMP 运行时资产（bpsim 原本一份都没有）

| 路径 | guid（必须保持） |
|---|---|
| `Assets/Resources/TMP Settings.asset` | `2207355e69e24674a8d46b36350b1a46` |
| `Assets/Resources/fonts & materials/LiberationSans SDF.asset` | `a3db0803fb2e0c4479d6378e35af2bd7` |
| `…/LiberationSans SDF Material.mat` | `aa2af50c568f25c43ac068741a0f8a6c` |
| `…/LiberationSans SDF Material_0.mat` | `25b12bdbb0d91234b84c78c0c501f322` |
| `…/LiberationSans SDF Atlas.png` | `e0566bc8674312e408a80c07065ef80e` |
| `…/LiberationSans SDF Atlas_0.png` | `b7a658e0bbe497b4ca74862f472e84bc` |
| `Assets/Shader/TMPro*.cginc` ×4 | 见文件 .meta |
| `Assets/Shader/TextMeshPro_*.shader` ×6 | 见文件 .meta |

**复制时必须清掉的悬空引用**（父工程里那些资产 bpsim 没有，照抄会变成空引用）：

- `TMP Settings.asset`：`m_fallbackFontAssets: []`、`m_defaultSpriteAsset: {fileID: 0}`、
  `m_defaultStyleSheet: {fileID: 0}`、`m_leadingCharacters: {fileID: 0}`、`m_followingCharacters: {fileID: 0}`。
- `LiberationSans SDF.asset` 的 `m_FallbackFontAssetTable: []`（指向没搬的 `LiberationSans SDF - Fallback.asset`）。

`UnityUI.cginc` 不需要拷——TMP shader 里 `#include "UnityUI.cginc"` 由编辑器安装目录提供
（`D:\folder\2022.2.23f1\Editor\Data\CGIncludes\UnityUI.cginc`）。

## 3. 四处必须改（否则编译不过 / 跑不起来）

### 3.1 `IngameDebugConsole.Runtime.asmdef`：加 InputSystem 引用

```json
"references": ["Unity.InputSystem"]
```

父工程是 `[]`，因为它是 `Both` 模式走 legacy `Input.GetKeyDown`。bpsim 是
`activeInputHandler: 1`（无 `ENABLE_LEGACY_INPUT_MANAGER`），插件走的是
`#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER` 分支，用
`InputAction`/`Keyboard`，必须引用 InputSystem 程序集。

TMP 与 uGUI 是 `Assets/Plugins` 的 precompiled DLL，**不能写进 `references`**；
asmdef 程序集默认会自动引用它们（`PluginImporter` 的 `Any` 平台启用即生效），实测编译通过。

### 3.2 `Scripts/EventSystemHandler.cs`：`#if` 追加 `UNITY_INPUT_SYSTEM_ENABLE_UI`

```csharp
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER && UNITY_INPUT_SYSTEM_ENABLE_UI
```

`InputSystemUIInputModule` 类由 `com.unity.ugui` 包的 versionDefine
（`com.unity.ugui >= 1.0.0` → `UNITY_INPUT_SYSTEM_ENABLE_UI`）解锁。bpsim 没有这个包
（uGUI 是 DLL），定义不存在、类就不存在，不改这里直接 `error CS0246`。
bpsim 的 `Assets/Scripts/BpsimCompat/BpsimInputModule.cs` 就是为同一个原因存在的。

### 3.3 prefab 的 `m_Script` 重链接（uGUI 源码 guid → DLL localId）

父工程的 `Assets/Resources/INDebugConsole.prefab` 已经是「uGUI 源码 guid」形式，直接拷进
bpsim 会变成 161 个 Missing Script。用 `Tools/stage_debugconsole_prefab.py` 生成：

```bash
py Tools/stage_debugconsole_prefab.py
```

它做两件事（规则在 `Tools/relink_debugconsole.py`）：

1. `{fileID: 11500000, guid: <uGUI 源码 guid>}` → `{fileID: <名字哈希>, guid: d3e719b59ab71ba3f6b398058c866280, type: 3}`
   （`d3e719b5…` = bpsim 的 `UnityEngine.UI.dll`）。
2. `8f586378b4e144a9851e7b34d9b748ee`（TMP 包内字体 guid，本工程缺失）→ `a3db0803…`（LiberationSans SDF）。

名字哈希 = Unity 的 DLL MonoScript fileID：
`fileID = int32_le(MD4("s\0\0\0" + namespace + className)[0:4])`。
因为现代 `hashlib` 已移除 MD4，脚本里自带一份实现，并用 17 个已知值断言自检
（`Image`/`Text`/`Button`/…/`TMPro.TMP_Settings`）。

> **`Assets/Resources/INDebugConsole.prefab` 是重链接过的产物，不要拿父工程的
> `IngameDebugConsole.prefab` 或 `Assets/Plugins/IngameDebugConsole/IngameDebugConsole.prefab`
> 覆盖它。** 覆盖后重新跑上面的脚本即可恢复。

### 3.4 输入键

prefab 里 `toggleWithKey: 1`、`toggleKey: 96`（backquote）。legacy 分支在 bpsim 不编译，
实际生效的是 InputAction `<Keyboard>/backquote`，行为一致。

## 4. 开关的实现：「组件在不在」

### 4.1 为什么不是「隐藏窗口」

`DebugLogManager` 只要存在就有固定开销，和窗口是否可见无关：

- `OnEnable()` 订阅 `Application.logMessageReceivedThreaded` —— 每条日志都要过一次
  `ReceivedLog`（入队、判重、格式化）；
- `LateUpdate()` 每帧出队（`queuedLogLimit: 256`）、按 `maxLogCount: 2048` 裁剪；
  源码里那条 `if( !isLogWindowVisible && !PopupEnabled ) return;` 只跳过**刷新**，不跳过捕获与裁剪。

所以开关语义定为「实例化 / 销毁」，关掉后 `DebugLogManager` 对象根本不存在。

### 4.2 文件与职责

| 文件 | 职责 |
|---|---|
| `Bpsim/DebugConsoleSettings.cs` | `SettingsBase` 子类，只有一个 `Enabled`（默认 false） |
| `Bpsim/UserSettings.cs` | 挂 `DebugConsole` 节；旧存档没有该节时保持默认（关闭） |
| `Bpsim/UI/DebugConsoleHost.cs` | 按开关 `Instantiate`/`Destroy` prefab；`Describe()` 诊断串 |
| `Bpsim/UI/UserSettingsPanel.cs` | 设置面板「调试」分组的开关行 |
| `Bpsim/QA/DebugConsoleProbe.cs` | 运行时自测探针（写 `Logs/debugconsole-probe.txt`） |
| `Editor/DebugConsoleAutoRun.cs` | `[InitializeOnLoad]`，读 `Logs/_autorun.flag` 自动跑一遍探针 |
| `Editor/DebugConsoleRelink.cs` | `VerifyAll()` / `VerifyTmpAssets()` / `CaptureGameView()` |

`DebugConsoleHost.Watchdog` 用「先每帧等、接上后 `enabled = false`」的方式接线：
`UserSettings.Load()` 在 `CoreManager.Awake` 里，可能晚于 `AfterSceneLoad`，等拿到
`DebugConsoleSettings` 后订阅 `PropertyChanged` 并立刻停掉自己的 `Update`，
之后开关变化不产生任何每帧调用。

### 4.3 设置面板行

`UserSettingsPanel.BindDebugConsoleSection()`：`AddSectionHeader("调试")` + 一行开关，
标题「启用 debug 控制台」。

- 没用 `AddSettingItems<T>()` 的自动行，因为自动行把属性名 `Enabled` 当标题，
  看不出是哪个功能。
- 标题必须短：该行是 legacy `Text` + 单行，长标题会被裁掉（初版写成
  「启用 debug 控制台（\` 键开关，关闭时零开销）」，截图上只剩前半句）。
- `OnDestroy()` 里只 `m_debugConsoleProxy.Unregister()`，**不**调
  `m_debugConsoleProxy.Source.Unregister()` —— 后者会清掉 `DebugConsoleHost` 订阅的
  `PropertyChanged`，之后开关就再也不生效。

## 5. 开启时的开销控制（prefab 序列化值）

| 字段 | 值 | 作用 |
|---|---|---|
| `receiveInfoLogs` | 0 | 只收 Warning/Error/Exception（父工程同款） |
| `startMinimized` | 1 | 窗口默认收起，只剩悬浮球 |
| `queuedLogLimit` | 256 | 窗口隐藏时队列上限，超出即丢 |
| `maxLogCount` | 2048 | 内存封顶 |
| `logsToRemoveAfterMaxLogCount` | 128 | 一次性裁掉一批，避免每帧裁 |
| `receiveLogsWhileInactive` | 0 | 失活时不收 |
| `popupVisibility` | 0 (Always) | 用 `Never` 的话玩家点一次 X 之后悬浮球再也不出现 |

## 6. 验证方式

编辑器开着时 batchmode 不可用（`It looks like another Unity instance is running`），
而本机的 DshBridge 端口会被 `AssetImportWorker` 抢（详见下节），所以验证走「标志文件 + 自启动」：

```powershell
Set-Content -Path "Logs\_autorun.flag" -Value "run" -Encoding ASCII
& "Logs\_launch_editor.bat"      # 清 proxy 变量后启动编辑器
```

编辑器自己开 `Assets/Scenes/MainScene.unity` → 进 Play → 跑探针 → 退出 → 写
`Logs/debugconsole-autorun.txt` 与 `Logs/debugconsole-probe.txt`，截图在
`Logs/debugconsole-window.png`、`Logs/debugconsole-settings.png`。

探针覆盖的六段：startup（默认关闭、无残留 manager）、enable（建起来、窗口收起、悬浮球在）、
logs（Show 能开、能收到 warning/error、Hide 能关）、disable（销毁、静态 Instance 清空）、
reenable（能重建、不会重复）、visual（截图 + 洋红像素检测 + 设置面板行存在）。

编译验证仍用 batchmode（编辑器必须已退出）：

```bash
cmd //c "Tools\unity-batch.bat"
grep -c "error CS" Logs/unity-batch.log    # 必须 0
```

## 7. 踩过的坑

- **TMP 在 URP 2D 下能画**：`TextMeshPro/Mobile/Distance Field` 是 Built-in CG pass、
  无 `LightMode` tag，但 UI 走 Screen Space Overlay 不经过相机，实测正常渲染、
  洋红像素 0/1252804。字体 material 用的是 `LiberationSans SDF Material_0`。
- **`invoke_static` 只能调 `public static`**：`DebugConsoleHost` 一开始写成 `internal static`，
  桥找不到类型。现已 `public`。
- **`try/catch` 不能包 `yield`**（`CS1626`）：探针第一版直接在外层包 try/catch，batchmode 报
  15 条该错误。改成手工驱动 `IEnumerator.MoveNext()`，异常在单步粒度捕获。
- **进 Play Mode 会 domain reload**：`[InitializeOnLoad]` 的静态字段与
  `EditorApplication.update` 订阅全部丢失。`DebugConsoleAutoRun` 的进度存
  `SessionState`（跨 reload 存活），构造函数每次都重新挂 `update`。
- **本机 DshBridge 的 8791 端口不可靠**：`AssetImportWorker1/2` 也会起
  `[InitializeOnLoad]` 并 listen 同一端口，请求会随机落到 worker 上，而 worker 里没有主线程
  循环驱动待处理队列 → 一律超时；死掉的旧编辑器进程还会让句柄一直被 worker 继承。
  不要依赖桥验证，用上面的标志文件路线。
- 设置行标题过长会被裁切（见 4.3）。

## 8. 已知遗留

- 悬浮球位置存 `PlayerPrefs "IDGPPos"`（归一化坐标），与父工程行为一致。
- `Assets/Plugins/IngameDebugConsole/IngameDebugConsole.prefab` 是素材原版，
  里面 `embeddedEventSystem` 指向一个自带的 EventSystem，在 bpsim 会和
  `BpsimInputModule` 冲突——**不要实例化它**，只用 `Assets/Resources/INDebugConsole.prefab`
  （staged 那份的 `embeddedEventSystem` 是 `{fileID: 0}`）。
- `DebugConsoleRelink.cs` 与 `DebugConsoleAutoRun.cs`、`Bpsim/QA/DebugConsoleProbe.cs`
  是移植与验证用的工具，不参与游戏运行时逻辑，可随时删除（删了不影响控制台本身）。

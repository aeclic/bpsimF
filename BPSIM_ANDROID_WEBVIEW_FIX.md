# 安卓端 WebView 报错修复（模组网页面板）

## 症状

Android 真机/模拟器上打开带 `<Panel>` 的模组时，模组面板直接异常，日志形如：

```
[3D WebView] System info (used by Vuplex support):
...
Vuplex.WebView.WebViewUnavailableException: The build platform is set to Android, but
3D WebView for Android isn't installed in the project. ...
```

桌面（Windows/Mac）以及编辑器里同一份面板正常，只有 Android 包崩。

## 根因

本工程引入的 Vuplex 只有 **Core + Standalone(Windows/Mac)** 两个包，**没有 Android 后端**。

`Assets/Vuplex/WebView/Core/Scripts/Internal/WebPluginFactory.cs:139` 在 `#elif UNITY_ANDROID` 分支里找不到
`3D WebView for Android` 插件时会直接抛 `WebViewUnavailableException`；`IgnoreMissingPluginInEditor`
只在编辑器里把后端降级成 Mock，**Player 上无效**。所以只要面板代码调用
`CanvasWebViewPrefab.Instantiate()`，安卓包必然抛异常。

### 漏网之鱼：运行时加载的可选 Mod 绕过门面

首轮把 `ModPanelService`（`<Panel>` 模组面板）收进门面后，Android 上仍复现白板，日志栈依旧
命中 `WebPluginFactory.cs:151` / `CanvasWebViewPrefab.cs:524`。取证（对比 APK 内
`Assembly-CSharp.dll` 的字符串与源码 mtime）确认：`HtmlPanelView` 在 Android 包里编译的是
**移动分支**（`CreateMobileBackend` + `LoadHTMLString`），桌面分支 `CanvasWebViewPrefab.Instantiate()`
已被条件编译裁掉，门面自身不会抛异常。

真正来源是**运行时加载的可选 Mod**：关卡编辑器 Mod 的 `Code/EditorWindow.cs`（源码在独立仓库
`aeclic/badpiggies-editor-sim-mod` 的 `Mods/LevelEditor/` 下）里的关卡编辑器窗口
（1820×1000、`sortingOrder=32500` 的全屏框）里仍直接 `CanvasWebViewPrefab.Instantiate()`，
完全绕过门面。该 Mod 由 `Mod.xml` 描述、`BpsimLevelEditor.dll` 承载，点「打开关卡编辑器」快捷按钮
时才创建 WebView；`Start()` 的异步初始化异常被 Vuplex 的 try/catch 吞掉（`Debug.LogException`），
prefab 停在白色初始纹理，于是盖出一整块白板。`Builds/Mods/BpsimLevelEditor-*.zip` 分发的 DLL 与源码
也全是裸 Vuplex。此外 `Bpsim/QA/WebPanelProbe.cs` 的 `KeyboardProbeRunner` 是另一处可被
`invoke_static` 触发的裸 Vuplex。

## 方案

把「选后端」的职责收进一个新的平台门面 `HtmlPanelView`，业务代码不再直接碰 Vuplex：

| 平台 | 后端 | 说明 |
| --- | --- | --- |
| Windows / macOS / 编辑器 | Vuplex `CanvasWebViewPrefab` | 行为与改动前一致；编辑器里仍设 `WebPluginFactory.IgnoreMissingPluginInEditor = true` |
| Android / iOS（真机） | UniWebView 6（原生 WebView） | 从 bprc 工程移植，`Assets/UniWebView/` + `Assets/Plugins/Android/UniWebView.aar` |
| 其它 | 无 | 面板显示「网页面板不可用 + 原因」，不再抛异常 |

### 页面 → 宿主消息怎么走

现有页面脚本（如 `Assets/StreamingAssets/Mods/netease-music/panel.js`）统一用
`window.vuplex.postMessage(...)`，UniWebView 没有这个 JS API。做法是在移动端把页面 HTML
**注入一段 shim**（`HtmlPanelView.InjectMobileShim`，插在 `<head>` 之后，幂等）：

```js
window.vuplex = {
  postMessage: function (text) {
    // 非字符串先 JSON.stringify
    location.href = 'uniwebview://message?data=' + encodeURIComponent(String(text)) + '&seq=' + (++seq);
  },
  addEventListener: ..., removeEventListener: ..., __bpsimDispatch: ...
};
```

`uniwebview://` 是 UniWebView 默认注册的 scheme，宿主侧
`UniWebView.OnMessageReceived` 读 `message.Args["data"]`（`OnMessage` 里再退化为 `RawMessage`）。
于是所有模组页面**零改动**兼容移动端。

### 宿主 → 页面消息

仍是 `window.__bpsimHost(json)`，移动端走 `UniWebView.EvaluateJavaScript`，桌面走
`IWebView.ExecuteJavaScript`。JSON 里的 `\u2028/\u2029` 依旧转义后再拼表达式。

## 变更清单

新增：

- `Assets/Scripts/Assembly-CSharp/Bpsim/UI/HtmlPanelView.cs` —— 平台门面
  （`Attach / LoadHtml / ExecuteJavaScript / SetVisible / SyncFrame`，事件
  `Ready / MessageReceived / PageLoaded / Failed`）。
- `Assets/UniWebView/` —— UniWebView 6 运行时代码 + 编辑器 PostBuild（去掉了 Demo）。
- `Assets/Plugins/Android/UniWebView.aar` —— Android 原生实现（仅 Android 平台启用）。
- `Assets/Scripts/Assembly-CSharp/Bpsim/QA/WebViewSmokeTest.cs` —— 真机冒烟测试（默认不跑，见下）。
- 本文件。

修改：

- `Assets/Scripts/Assembly-CSharp/Bpsim/Mods/ModPanelService.cs`
  - `ModPanelWindow.Build()` 不再 `CanvasWebViewPrefab.Instantiate()`，改为
    `HtmlPanelView.Attach(gameObject, host, true, ...)`；后端就绪（`Ready`）后才 `LoadHtml`，
    后端失败（`Failed`）则在面板里显示错误文案而不是抛异常。
  - `WindowDrag` 新增 `Moved` 回调，拖动窗口时同步原生视图矩形。
- `Assets/UniWebView/UniWebView-CSharp.asmdef`：`references` 加 `Unity.InputSystem`。
  本工程只启用新输入系统（`ENABLE_LEGACY_INPUT_MANAGER` 未定义），UniWebView 的 Esc 处理
  在 `#if ENABLE_INPUT_SYSTEM` 分支里会用到 `UnityEngine.InputSystem`；bprc 因为还开着旧
  Input，走了另一条分支才没暴露。

### 第二轮（运行时 Mod 白板）

修改：

- `Code/EditorWindow.cs` —— 整文件重写：删 `using Vuplex.WebView;` 与
  `CanvasWebViewPrefab` 字段，改 `HtmlPanelView.Attach(gameObject, host, true, ...)`；订阅
  `Ready/MessageReceived/PageLoaded/Failed`，`Ready` 后 `LoadUrl(EditorUrl)`，
  `Failed` 显示错误文案；`OnMessage(string)` 用 `JObject.Parse` 解析 `bpsim.capabilities`；
  删 `SetRenderingEnabled/SetFocused`，`SetVisible` 走门面；`OnDestroy` 退订门面事件。
- `Assets/Scripts/Assembly-CSharp/Bpsim/UI/HtmlPanelView.cs` —— 新增 `LoadUrl(string url)`：
  移动端 `UniWebView.Load(url, true)`（`skipEncoding` 保留 `#bpsim-token` fragment）+ 注入
  `MobileShimJs`（`AddJavaScript` 前置 + `OnPageFinished` 里 `EvaluateJavaScript` 幂等补注入，
  保证 HTTP 页面也有 `window.vuplex`）；桌面端 `IWebView.LoadUrl(url)`；两个 `#else` 桩补
  `LoadMobileUrl/LoadDesktopUrl`。原因：关卡编辑器只有本地 HTTP 服务 URL，没有整页 HTML 字符串。
- `Assets/Scripts/Assembly-CSharp/Bpsim/QA/WebPanelProbe.cs` —— `using Vuplex.WebView;` 与
  `KeyboardProbeRunner` 整类用 `#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX`
  包裹，Android Player 不再编译任何可实例化的裸 Vuplex。
- `Assets/Editor/UniWebView/settings.asset` —— `usesCleartextTraffic: 0 → 1`。UniWebView 的
  `PostBuildProcessor` 按这个开关决定是否给最终 manifest 写
  `android:usesCleartextTraffic="true"`（`UniWebViewPostBuildProcessor.Android.cs:112-114` →
  `AndroidManifest.cs:116-123`）。关卡编辑器页面走 `http://127.0.0.1:<port>` 明文，Android
  9+/targetSdk≥28 默认拦 cleartext，loopback 不一定豁免；打开后本地 HTTP 页面必定可加载。
  **改此设置后必须重出 APK**（manifest 是构建期生成的，已出的包不含这个属性）。
- `Assets/Vuplex/WebView/Core/Scripts/CanvasWebViewPrefab.cs` —— **平台兜底**：`Start()` 在
  `#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR` 里直接 `gameObject.SetActive(false)` + return，
  任何漏网的裸 Vuplex（含未来第三方 Mod）都走不到 `Web.CreateWebView()`，从源头杜绝异常；
  `_initCanvasPrefab` 的 catch 里同样 `SetActive(false)`，避免白色纹理残留。
  **不降级 MockWebPlugin**：`MockViewportMaterial.mat` 在 Editor 目录、Player 不打包，降级会让它
  `new Material(null)` 空引用换个地方崩。

重建产物：

- `BpsimLevelEditor.dll` —— 经 `Bpsim.EditorTools.LevelEditorModBuild.BuildMod()`
  用 mcs 重编（依赖新门面 `LoadUrl`）；验证 DLL 含 `HtmlPanelView/LoadUrl/OnReady/OnMessage`、
  **不含** `CanvasWebViewPrefab`/`Vuplex.WebView`。
- `Builds/Mods/BpsimLevelEditor-0.3.0.zip` —— 替换包内 `BpsimLevelEditor.dll` 与
  `Code/EditorWindow.cs`（其余 165 个条目原样保留），与源码/DLL 保持一致。

> 门面在移动端不创建 `WebViewKeyboardBridge`，`HtmlPanelView.Keyboard` 为 null；EditorWindow
> 里 `if (m_view.Keyboard != null)` 保护，桌面才设 `ForwardEditorShortcuts`。

## 移动端实现要点

- **创建**：`gameObject.AddComponent<UniWebView>()`，`ReferenceRectTransform = WebHost`，
  原生视图按该 RectTransform 的屏幕矩形定位（每帧转换经由 Canvas）。
- **加载**：`LoadHTMLString(html, baseUrl)`，`baseUrl` 为
  `file:///<persistentDataPath>/Mods/<id>/`，让页面里的相对资源能定位到模组目录。
  Android 11+ 默认屏蔽 file 访问，因此打开
  `SetAllowFileAccess(true)` + `SetAllowFileAccessFromFileURLs(true)`。
- **可见性**：`Show()/Hide()`；页面加载完成前不 `Show`，避免白屏闪烁。
  关闭面板时先 `Hide` 再 `Destroy`（UniWebView 的 `OnDestroy` 会销毁原生视图）。
- **拖动同步**：UniWebView 只在屏幕方向/尺寸变化时重算 frame，窗口拖动后必须手动
  `UpdateFrame()`；门面用 `SyncFrame()` 暴露，`ModPanelWindow.WindowDrag.Moved` 里调用。
- **键盘**：移动端是系统原生输入框，不需要 `WebViewKeyboardBridge`（那是给 Vuplex
  的 `IWithIme` 用的），门面在移动端不创建它。
- **返回键**：`SetBackButtonEnabled(false)`，保持原来的面板交互（关闭由面板自己的 × 负责）。

## 构建与验证

> **前提**：关卡编辑器是运行时加载的可选 Mod，不在 APK 内。修复后必须同时把
> `Builds/Mods/BpsimLevelEditor-0.3.0.zip` 重新解压到设备
> `<persistentDataPath>/Mods/BpsimLevelEditor/` 覆盖旧 `BpsimLevelEditor.dll`；
> 并且必须重出 APK —— 新 Mod 依赖门面新增的 `LoadUrl`，旧 APK 的
> `Assembly-CSharp.dll` 没有该方法会 `MissingMethodException`。两者缺一不可。

```bat
:: 1) 构建（编辑器未开时用 batch；编辑器开着就用 Tools/dsh.py 的 invoke_static 触发）
"D:\folder\2022.2.23f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main" ^
  -executeMethod Bpsim.EditorTools.AndroidBuild.Build ^
  -logFile "Logs\build_batch.log"

:: 2) 安装 + 启动（LDPlayer 自带 adb）
"D:\leidian\LDPlayer14\adb.exe" install -r Builds\Android\bpsimF_android.apk
"D:\leidian\LDPlayer14\adb.exe" shell am start -n com.bprc.bpsimF/com.unity3d.player.UnityPlayerActivity
```

真机冒烟测试（不需要点 UI，自动跑一遍「创建 → 加载 → 双向消息」）：

```bat
adb shell "mkdir -p /data/data/com.bprc.bpsimF/files"
adb shell "touch /data/data/com.bprc.bpsimF/files/webview_smoke.on"
adb logcat -s Unity:* | findstr WebViewSmoke
```

跑完结果同时写 `<persistentDataPath>/webview_smoke.log`，随后删掉开关文件即恢复原行为。

> batch 构建如果报 `Internal build system error ... 'NO_PROXY' Key being added: 'no_proxy'`，
> 是 Bee 在大小写不敏感的环境变量字典里撞键了，启动 Unity 前先清掉代理变量：
> `env -u NO_PROXY -u no_proxy -u HTTP_PROXY -u http_proxy -u HTTPS_PROXY -u https_proxy Unity.exe ...`

## 已知限制

- 移动端页面是**原生视图**，绘制在 Unity 画面之上：Unity 的 UI 无法盖在它上面，也无法裁剪
  （圆角/遮罩对页面本体无效）。所以页面区域留了内边距，标题栏在页面矩形之外。
- 页面内 `location.href = 'uniwebview://...'` 的消息带宽受 URL 长度限制，消息本身应保持小体积
  （现有音频/关闭/日志协议都是短 JSON）。
- iOS 分支代码已写好（同样是 UniWebView），但本次只在 Android 上验证过。
- APK 体积会因 UniWebView 的 aar 略微增大。
- 关卡编辑器走 `http://127.0.0.1:<port>` 本地 HTTP 服务。已在
  `Assets/Editor/UniWebView/settings.asset` 打开 `usesCleartextTraffic: 1`，UniWebView 的
  `PostBuildProcessor` 会在构建期给最终 manifest 写 `android:usesCleartextTraffic="true"`
  （A/B 取证：关闭时 `Library/Bee/Android/.../AndroidManifest.xml` 全无该属性），明文页面
  确定可加载。此开关只影响构建产物，改后必须重出 APK。

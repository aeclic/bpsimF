# BepInEx 与游戏内 Mod 框架

本文件说明 bpsim 的 Mod 体系：内建的「游戏内 Mod 管理器」、网页内容包（panel）、托管插件包（plugin/mixed），
以及**可选的** BepInEx 宿主胶水层。

> 设计目标：**没有 BepInEx 也能用 Mod**。BepInEx 只是让「别的 BepInEx 插件」能和本项目协作的额外通道。

---

## 1. 目录结构

```
persistentDataPath/
  Mods/                         ← 玩家安装/拖入的 Mod（可写）
    <mod-id>/
      manifest.json
      panel.html  panel.css  panel.js
      (可选) MyMod.dll
  ModWorkshop/
    cache/index.json            ← 工坊索引缓存
    downloads/                  ← 工坊下载的 zip
    config.json                 ← 可选：{"indexUrl":"..."} 覆盖默认索引

streamingAssetsPath/
  Mods/                         ← 随游戏分发的内置 Mod（只读）
    netease-music/              ← 样板：网易云音乐网页面板
```

`persistentDataPath` 在 Windows 上是 `%USERPROFILE%\AppData\LocalLow\<公司>\<产品>\`
（本项目实测 `C:/Users/<user>/AppData/LocalLow/bput/bpsim`）。

源码位置（本仓库）：

| 角色 | 路径 |
| --- | --- |
| 公开 API / 清单模型 | `Assets/Scripts/Assembly-CSharp/Bpsim/Modding/` |
| 运行时（管理器、UI、ECS、工坊） | `Assets/Scripts/Assembly-CSharp/Bpsim/Mods/` |
| 样板 Mod（内置） | `Assets/StreamingAssets/Mods/netease-music/` |
| 自测 | `Assets/Editor/BpsimModSelfTest.cs` |
| BepInEx 宿主 API | `Assets/Scripts/Assembly-CSharp/Bpsim/Mods/BepInExGlue.cs` |
| BepInEx 插件 / 部署脚本 | `Mods/FrameShapes/FrameShapesPlugin.cs` / `Mods/FrameShapes/deploy.ps1` |

---

## 2. 清单 `manifest.json`

```jsonc
{
  "schema": 1,
  "id": "my-mod",              // 全局唯一、小写连字符；同时是安装目录名
  "name": "我的 Mod",           // 显示名
  "version": "1.0.0",
  "author": "someone",
  "description": "一句话简介",
  "minGameVersion": "",        // 可选
  "type": "panel",             // panel | plugin | mixed
  "entryAssembly": "",         // plugin/mixed：入口 dll 文件名（相对 Mod 目录）
  "entryType": "",             // plugin/mixed：实现 IBpsimMod 的完整类型名
  "affectsSimulation": false,  // 联机提示用
  "multiplayerSafe": true,
  "panels": [
    {
      "id": "main",
      "title": "窗口标题",
      "html": "panel.html",
      "css": "panel.css",      // 可选，会被内联进 <style>
      "js": "panel.js",        // 可选，会被内联进 <script>
      "width": 460,
      "height": 680,
      "anchor": "bottom-right",// bottom-right|bottom-left|top-right|top-left|center
      "icon": "",              // 可选
      "autoOpen": false
    }
  ],
  "tags": ["样板"]
}
```

三种类型：

- **panel** —— 纯网页内容，无 DLL。宿主用 Vuplex 3D WebView 渲染成可拖动窗口。
- **plugin** —— 含托管程序集，`entryType` 指向实现 `IBpsimMod` 的类（无参构造）。
- **mixed** —— 既有面板又有 DLL。

---

## 3. 网页面板

面板就是一个 HTML 页面。宿主把 `panel.html` 读出来，把 `css`/`js` 内联进去，用 Vuplex `LoadHtml` 加载。

### 与宿主通信

页面里直接用 Vuplex 的 JS 桥：

```js
// 页面 → C#
window.vuplex.postMessage(JSON.stringify({ type: "ready", panel: "main" }));
// C# → 页面
window.vuplex.addEventListener("message", (e) => {
  const data = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
});
```

> 注意：`HtmlPanel.Publish` 一类的宿主方法必须传**对象**，传字符串会被二次序列化。

### 样板：网易云音乐

`Assets/StreamingAssets/Mods/netease-music/` 是一个完整可用的 panel 型 Mod：

- 网易云红 `#C20C0C` 主题、唱片旋转、进度条、播放列表、搜索过滤、键盘快捷键（空格/左右）。
- 纯本地 mock 数据（不联网），用于演示「一个 Mod 面板长什么样、怎么和宿主说话」。
- 作为**模板**：复制这个目录，改 `manifest.json` 的 `id/name` 和 HTML/CSS/JS 即可做出自己的面板 Mod。

---

## 4. 托管插件（plugin / mixed）

1. 用任意 .NET（Mono 兼容 netstandard2.1）工程编译一个 dll，引用本项目的 `Bpsim.Modding` 类型
   （`ModManifest`、`IBpsimMod`、`IModContext` 等，全在 `Assets/Scripts/Assembly-CSharp/Bpsim/Modding/`）。
2. 实现：

```csharp
using Bpsim.Modding;

public sealed class MyMod : IBpsimMod
{
    private IModContext _ctx;
    public void OnLoad(IModContext context)
    {
        _ctx = context;
        context.Log.Info("hello from " + context.Manifest.name);
        context.Ui.AddQuickButton("我的功能", () => context.Log.Info("clicked"));
        context.Ui.Toast("Mod 已加载");
    }
    public void OnUnload() { }
}
```

3. dll 放进 Mod 目录，`manifest.json` 写 `"type":"plugin"`、`"entryAssembly":"MyMod.dll"`、
   `"entryType":"MyMod"`（完整类型名，含命名空间）。

### 上下文能力（`IModContext`）

| 成员 | 说明 |
| --- | --- |
| `ModDirectory` | 本 Mod 安装目录（绝对路径） |
| `Manifest` | 解析后的清单 |
| `Log` | `Info/Warn/Error`，带 `[Mod:<id>]` 前缀 |
| `Ui.AddQuickButton(label, onClick)` | 在 Mod 管理器里加一个快捷按钮 |
| `Ui.OpenPanel(panelId)` / `ClosePanel` | 开关本 Mod 的网页面板 |
| `Ui.Toast(msg)` | 右下角气泡 |

默认启用策略：**panel 默认开，plugin/mixed 默认关**（可在管理器里手动开启，状态存 `mods.settings.json`）。

---

## 5. 游戏内 Mod 管理器

- 运行时自动启动（`ModBootstrap` 的 `RuntimeInitializeOnLoadMethod`，或 BepInEx 插件 `Awake`，二者幂等）。
- **最直接：打开游戏「设置」面板 → 最底部「Mod」分组 → 点「打开 Mod 管理器」按钮。**
- 也支持按 **F10** 打开/关闭；或左下角「MOD」悬浮按钮。
- 页签：**已安装** / **创意工坊**。
- 已安装页：每个 Mod 的启用开关、删除（仅玩家安装的）、面板入口、插件快捷按钮。
- 创意工坊页：拉取 `index.json`，按版本显示「安装 / 更新 / 已安装」，点击即下载 → sha256 校验 → 安装。
- 底部：打开 Mods 文件夹 / 安装剪贴板里的 zip / 刷新扫描。

---

## 6. 创意工坊

索引格式与 BPSS 工坊一致（`index.json`）：

```jsonc
{
  "schema": 1,
  "generatedAt": "2026-10-24T00:00:00Z",
  "baseUrl": "https://example.com/ws/",
  "packs": [
    { "id":"demo", "name":"演示", "version":"2.0.0", "url":"demo.zip",
      "sha256":"…", "size":12345, "contentType":"addon",
      "tags":["a"], "mirrors":["https://mirror/demo.zip"] }
  ]
}
```

- `url` 可以是绝对地址，也可以是相对 `baseUrl` 的路径。
- 默认索引地址 `https://gitee.com/bpsim/workshop/raw/master/index.json`，
  可被 `persistentDataPath/ModWorkshop/config.json` 的 `{"indexUrl":"..."}` 覆盖。
- 安装流程：缓存优先读取索引 → 下载（支持镜像逐个回退）→ sha256 校验 → `ModPackageStore.InstallZip`。

---

## 7. BepInEx

Windows x64 构建通过 `Assets/Editor/Bpsim/FrameShapesBuildPostprocessor.cs` 自动部署 BepInEx 5.4.23.2 及变形框插件。
插件入口位于独立的 `BepInEx/plugins/Bpsim.FrameShapes/Bpsim.FrameShapes.dll`，由 chainloader 正常发现。
`Assembly-CSharp` 中的 `BepInExGlue.cs` 只提供无 BepInEx 依赖的 `BpsimModHost` 门面；无需 `BEPINEX` 编译符号。
部署脚本将 BepInEx 默认的 `Application` 加载入口调整为 `Camera`，避免插件对象在 bpsim 启动场景清理时被销毁。

第三方 BepInEx 插件在首场景加载后调用 `BpsimModHost.Register(manifest, instance, directory, defaultEnabled)`，
其中 instance 实现 `IBpsimMod`。宿主接管 `OnLoad/OnUnload`，把状态同步到 Mod 列表、设置文件与 ECS 注册表。
注册流程不再次扫描或加载 DLL；刷新 Mod 列表保留已注册的插件实例。销毁时调用 `Unregister(id, instance)`。
注册后仅新增该 Mod 的 ECS 镜像实体；开关和加载结果更新同一实体。

BepInEx 插件在列表中显示来源「BepInEx」。默认启停由注册者指定，玩家明确保存的禁用选择优先。
普通磁盘 plugin/mixed 包仍然默认关闭。

内置「BPIR 变形框」默认开启，支持方框、直角三角框、半圆框和圆框。
玩法数据、碰撞缓存和更新留在 ECS，外观沿用 DOTS 批量渲染；BepInEx 仅处理生命周期。
完整使用、部署和验证说明见 [Mods/FrameShapes/README.md](Mods/FrameShapes/README.md)。

---

## 8. 自测

`Assets/Editor/BpsimModSelfTest.cs`（菜单 `Tools/BPSim Mod/Self Test`）验证：

- 路径解析、内置样板 manifest、zip 安装/卸载往返、工坊索引反序列化、版本比较、设置持久化。

报告写到 `Builds/QA/mods-selftest.txt`，末行 `FAILURES: n`。

---

## 9. 已知限制 / 坑

- **Vuplex `LoadHtml` 只有单参重载**（无 baseUrl 版本）；相对资源需内联或改绝对路径。
- 面板 HTML 里的 `<meta viewport>` 与 CSS 尺寸请以 1920×1080 画布为基准缩放。
- 修改运行时 `.cs` 后，若用外部工具直接写盘，Unity 可能不重编译；请用桥的 `write_script`（会触发导入），
  或重启编辑器 / `Tools/unity-batch.bat`。
- 本仓库存在多个 AI 会话并发编辑；`Assembly-CSharp` 偶发被他人在途改动打断（如缺类型），
  那不是 Mod 框架的问题，等对方落盘后重编即可。

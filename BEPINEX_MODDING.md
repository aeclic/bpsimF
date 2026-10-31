# BepInEx 通道已移除

> 本文原描述「内建 Mod 体系 + 可选 BepInEx 宿主胶水层」。**BepInEx 通道已整体删除**，
> 现行规范见 [`BPSIM_MODDING.md`](BPSIM_MODDING.md)。

## 删了什么

| 文件 / 符号 | 状态 | 说明 |
|---|---|---|
| `Assets/Scripts/Assembly-CSharp/Bpsim/Mods/BepInExGlue.cs` | 已删除 | `BpsimModHost` 门面，供插件调 `Register/Unregister` |
| `Assets/Editor/Bpsim/FrameShapesBuildPostprocessor.cs` | 已删除 | 构建后跑 `Mods/FrameShapes/deploy.ps1` 部署插件 |
| `ModManager.RegisterExternal` / `UnregisterExternal` | 已删除 | 外部注册通道 |
| `ModPackage.ExternalInstance` / `ModSource.BepInEx` | 已删除 | 「等待插件注册」的状态 |
| `Assets/StreamingAssets/Mods/bpir-frame-shapes/` | 已删除 | 只是等 BepInEx 插件注册的元数据壳 |

## 为什么删

1. BepInEx 5 只能跑 Mono，而它提供的「Harmony 改已有方法」对 bpsim 用处有限 ——
   核心模拟是 Burst 编译过的 job，补丁打不进去。
2. 它**不能新增内容**（新部件/新系统），而这正是 Mod 体系要解决的问题。
3. 双通道（目录发现 + 外部注册）让状态机变复杂：要处理「等注册」「重复注册」「注册丢失后恢复占位」。
   单通道后 `ModManager` 只剩「发现 → 加载 → 卸载」三态。

## 换成了什么

Besiege 式官方加载器：`Mod.xml` 清单 + `ModEntryPoint` 钩子 + 静态扫描 + GUID 存档绑定。
细节见 [`BPSIM_MODDING.md`](BPSIM_MODDING.md)。

## 如果你之前在用 BepInEx 插件

- 插件里调 `BpsimModHost.Register(...)` 的代码删掉即可 —— 现在把同一个 DLL 放进
  `<persistentDataPath>/Mods/<你的 Mod>/`，配一个 `Mod.xml`，就会走正常加载。
- 插件用 Harmony 打的补丁要重写成 `ModEntryPoint` 的钩子（`OnWorldCreated` / `OnPartRegistered` / `OnMachineLoaded`）。
- `Mods/FrameShapes/` 目录保留着源码，但不再被任何构建步骤部署；要用请自行转成 Mod 包。

## 目录结构（现状）

```
persistentDataPath/
  Mods/
    Modding.xml                 ← 全局配置（禁用列表 / 维护版本 / 严格扫描）
    <任意目录>/
      Mod.xml                   ← 清单（推荐）
      panel.html  panel.css  panel.js
      (可选) MyMod.dll
  ModWorkshop/
    installed/<工坊 id>/Mod.xml
```

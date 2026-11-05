# bpsim Mod 系统（Besiege 式）

本文件是 Mod 体系的**权威说明**。设计直接对齐 Besiege 的官方加载器：清单 + 程序集 + 静态扫描 + 入口钩子。
**BepInEx 通道已移除**（旧文档见 `BEPINEX_MODDING.md` 的迁移指引）。

> 一句话：Mod 是一个目录，里面有 `Mod.xml`（清单）和可选的一个 `.dll`（代码）。
> 宿主扫目录 → 解析清单 → 扫描程序集 → 反射找入口 → 调 `OnLoad`。

---

## 1. 与旧版的区别

| | 旧（manifest.json + BepInEx） | 新（Mod.xml） |
|---|---|---|
| 清单 | `manifest.json` | `Mod.xml`（JSON 仍兼容） |
| 身份 | 字符串 id | **GUID**（存档只认它） |
| 代码入口 | `IBpsimMod` + `Assembly.LoadFrom` | `ModEntryPoint` + 钩子（`IBpsimMod` 仍兼容） |
| 外部注册 | `RegisterExternal`（BepInEx 插件注册进来） | **已删除**，只有目录发现一条路 |
| 配置 | `mods.settings.json` | `Modding.xml`（Besiege 同款键名） |
| 安全 | 无 | 引用/签名/PInvoke 硬拒 + IL 启发式告警 |
| 游戏更新 | 不动 | **全部禁用**，玩家逐个确认 |

---

## 2. 目录与扫描顺序

```
<persistentDataPath>/Mods/            ← 玩家安装（可写，优先级最高）
<persistentDataPath>/ModWorkshop/installed/  ← 工坊下载
<Modding.xml 里的 additional-dirs>    ← 额外目录（Mod 开发者挂工作区）
<streamingAssetsPath>/Mods/           ← 随包内置（只读，优先级最低）
```

同一个 GUID 只保留**先扫到**的那个。每个根目录支持「一层子目录」或「两层子目录」，
所以 `Mods/作者名/Mod名/Mod.xml` 也能认。

---

## 3. `Mod.xml`

```xml
<Mod>
  <Name>示例插件</Name>
  <Author>bpsim</Author>
  <Version>1.0.0</Version>
  <Description>一句话简介</Description>
  <Icon name="icon" path="icon.png" />
  <MultiplayerCompatible>true</MultiplayerCompatible>
  <MinGameVersion>0.0.12</MinGameVersion>

  <!-- 稳定标识；不写就由文件夹名派生（老 Mod 自动升级也靠这个） -->
  <ID>5f0c1f0e-9b2a-4d3e-8a71-6c4b2d9e0f13</ID>

  <!-- 有代码才需要这两项 -->
  <EntryPoint type="MyMod.MyEntryPoint" />
  <Assemblies>
    <Assembly path="MyMod.dll" />
  </Assemblies>

  <Resources>
    <Texture name="icon" path="icon.png" />
    <Panel id="player" title="播放器" html="panel.html" css="panel.css" js="panel.js"
           width="460" height="680" anchor="bottom-right" autoOpen="false" />
  </Resources>
</Mod>
```

| 元素 | 必填 | 说明 |
|---|---|---|
| `Name` | 否 | 显示名；缺省用文件夹名 |
| `Author` / `Version` / `Description` | 否 | 展示用 |
| `Icon` | 否 | `path` 指向包内图片 |
| `MultiplayerCompatible` | 否 | 默认 `true`；`false` 时联机界面会标红 |
| `MinGameVersion` | 否 | 低于此版本直接判 `Incompatible` |
| `ID` | **建议** | GUID。不写则从文件夹名派生，改文件夹名会让老存档失联 |
| `EntryPoint` | 有代码时 | 入口类型全名，必须是 `ModEntryPoint` 子类 |
| `Assemblies/Assembly` | 有代码时 | 第一个就是入口程序集 |
| `Resources/Panel` | 否 | 网页面板，字段与旧 `ModPanelSpec` 一致 |

> 清单解析是**宽松**的：认不得的标签忽略，缺字段用目录名兜底，不会因为一个拼错就整包失败。

---

## 4. `Mods/Modding.xml`

```xml
<Configuration version="2">
  <String key="maintenance-lastGameVersion">0.0.12</String>
  <StringArray key="maintenance-lastMods">
    <String>5F0C1F0E-9B2A-4D3E-8A71-6C4B2D9E0F13~B~~1.0.0~示例插件</String>
  </StringArray>
  <StringArray key="disabled-mods">
    <String>7062BAEE-484E-4CDD-8750-B4BAA7B964E5</String>
  </StringArray>
  <Bool key="strict-scan">false</Bool>
  <StringArray key="additional-dirs" />
</Configuration>
```

- `disabled-mods`：禁用列表（GUID）。**不在列表里 = 启用**，和 Besiege 一致。
- `maintenance-lastGameVersion`：和当前 `Application.version` 不一致时，**全部 Mod 自动禁用**并弹提示。
- `strict-scan`：把 IL 启发式告警升级为拒绝（默认关，见第 6 节）。
- `additional-dirs`：额外扫描目录，相对路径基于 `persistentDataPath`。
- 首次运行会从旧的 `mods.settings.json` 自动迁移一次：老配置里没被显式启用的 Mod 保持禁用。

---

## 5. 写代码 Mod

```csharp
using Bpsim.Modding;
using Unity.Entities;
using UnityEngine;

namespace MyMod
{
    public sealed class MyEntryPoint : ModEntryPoint
    {
        public override void OnLoad()
        {
            Log?.Info("载入自 " + ModDirectory);
        }

        public override void OnWorldCreated(World world)
        {
            // 在这里注册系统、拿 EntityManager
        }

        public override void OnPartRegistered(int partTypeId, string partName)
        {
            // 改部件参数、换图标
        }

        public override void OnMachineLoaded(string machinePath)
        {
            // 载具载入后的处理
        }

        public override bool IsRequiredForMachine(string machineJson)
        {
            return false; // 返回 true 会把本 Mod 的 GUID 记进存档
        }
    }
}
```

**规则**

1. 一个程序集里**只能有一个** `ModEntryPoint` 子类，多了直接判失败。
2. 所有钩子都有空实现，只覆写需要的。
3. 钩子里抛异常会被宿主吞掉并记日志，不会打挂游戏。
4. 程序集引用 `Assembly-CSharp.dll` 即可（和旧 Mod 一样）。
5. 老 Mod 不用改：实现 `IBpsimMod` 会被自动包进适配器，行为与以前一致。

### 5.1 源码 Mod（编辑器里方便，出包必须预编译）

把 `.cs` 放进 `Code/` 目录，清单里**不写** `<Assemblies>`，宿主会在加载时编译：

```
bpir-frame-shapes/
  Mod.xml                  ← 只写 <EntryPoint type="…"/>
  Code/BpirFrameShapes.cs
```

- 产物落在 `<persistentDataPath>/Mods/.compiled/<id>.dll`，按源码时间戳缓存，没改就不重编。
- 依赖 Mono 运行时的 CodeDom（`Microsoft.CSharp` / `System.CodeDom`）与 `mcs.exe`。
  取不到时会给出明确报错，不会让游戏编译失败。
- 编译是**主线程同步**的，第一次加载会卡一下（通常 < 1s）。

> **出包必读：玩家包里没有编译器。**
> Standalone Mono 构建只带 `MonoBleedingEdge/EmbedRuntime` 和 `etc/mono` 配置，
> **不带 `mcs.exe`，也不带 `Microsoft.CSharp.dll` / `System.CodeDom.dll`**。
> 因此 `Code/*.cs` 的运行时编译在 exe 里**必然失败**，Player.log 里是：
>
> ```
> [Mods] bpreboot-specials 加载失败：源码编译异常：
>   Error running ...\mono\mini\mono.exe: 系统找不到指定的文件。
> ```
>
> 所以**要随包发布的 Mod 必须先预编译成 DLL**，并在 `Mod.xml` 里用 `<Assemblies>` 声明：
>
> ```
> my-mod/
>   Mod.xml                  ← <EntryPoint …/> + <Assemblies><Assembly path="MyMod.dll"/></Assemblies>
>   MyMod.dll                ← 预编译产物
>   Code/MyMod.cs            ← 源码留着备查，有 DLL 后不再被编译
> ```
>
> 仓库里的 `Tools/Mods/build-mod-dll.bat` 就是干这个的：
>
> ```
> Tools\Mods\build-mod-dll.bat bpreboot-specials BprebootSpecials.dll
> ```
>
> 它用 Unity 编辑器的 `mcs` 编译，引用取自 `Builds/ModSpecials/bpsim_Data/Managed/`
> （`mscorlib` / `netstandard` / `System` / `System.Core` / `Unity.Entities` /
> `UnityEngine.CoreModule` / `UnityEngine` / `Assembly-CSharp`）。
> **出包顺序**：先跑一次构建拿到 Managed 程序集 → 编译各 Mod 的 DLL → 再构建一次
> （第二次会把新 DLL 一起带进 StreamingAssets）。改了宿主 API 后 DLL 要重编。
>
> 有 `<Assemblies>` 时 `HasCode` 为真，宿主直接 `Assembly.LoadFrom` 那个 DLL，
> `Code/` 目录会被完全忽略。

### 5.2 加部件：`ModParts`

**先想清楚要不要加部件。** 把已有部件换一种外观/行为，绝大多数时候不该做成新部件 ——
新部件会挤进建造列表、和原版图标对不上、还要补一堆按索引区间写死的判断。
`bpir-frame-shapes` 最初就是做成三个变体的，结果在建造列表里多出三格、放下后颜色也不对，
最后还是退回「不加部件，只开特性」。要做形状，直接看 5.3。

确实需要新部件时才用：

```csharp
// 克隆 MetalFrame#12，登记成 MetalFrame#9001，名字叫「直角三角框」
ModParts.TryAddVariant(PartType.MetalFrame, 12, 9001, "直角三角框（BPIR）", out string error);
```

- 索引从 `ModParts.CustomIndexBase`（9000）起，避开原版占用区间。
- 卸载用 `ModParts.RemoveVariant(type, index)`。
- **变体默认共用基部件的贴图与图标**；要换皮肤/图标见 5.5（`ModSprites` + `TryAddSkinVariant`）。
- 形状本身仍走 `FrameShapeSystem`：`FrameShape` 只有 4 个值（长方形/直角三角/半圆/圆），
  因为着色器按 `_BlendFactor.z` 分支裁剪，**加新形状需要改 shader**，Mod 只能在这 4 种里选。
- 变体的颜色/图标一律继承基部件：`ColoredFrame.GetColor` 会经 `PartVariants.Resolve` 取基部件色，
  所以克隆 `MetalFrame#12` 得到的变体就是 `#12` 的红色，而不是「中性色的框」。
  想要白色/灰色的框，请克隆 `MetalFrame#132`（透明框）或 `#120` 之后的灰度框。

**一次 `TryAddVariant` 在宿主里做四件事**（缺任何一件，变体都会变成「选择器里看得见、场上放不出来」）：

1. 克隆基部件的 `GameObject`，写 `PartIndex`，加进 `PartFactory.PartCollection` —— 选择器靠它列出变体；
2. 在 `PartVariants` 登记 `变体索引 → 基部件索引`。原版代码里到处是 `index` 区间判断
   （`BasePart.ColoredFrames`、`IsContainer`、`ColoredFrame.GetColor` 的 `index - 12` 配色、
   `PartEntityFactory.GetPartComponentTypes` 决定加不加 `ColoredFrame`），
   变体索引落在区间外就会被当成「另一种部件」。登记后这些判断会先顺着映射找回基部件；
3. 立刻为新变体建 ECS 预制体，塞进 `PartEntitySpawner` 的预制体表。
   这张表是 `PartManager.Start()` 一次性铺好的，而 Mod 在 `AfterSceneLoad` 之后才登记，
   宿主必须支持增量补登记 —— 否则拖到场上时 `FindPrefab` 返回 `Entity.Null`，放置直接被跳过，读档同理；
4. 补齐 `PartFactory.PartExtensionMap` 的变体项（质量等参数）。
   模拟里的 `ApplyPartsJob` 是 Burst，直接索引原生表，查不到就抛异常 —— 所以不能只在托管侧做回退。

`TryAddVariant` 失败会回滚（`PartVariants` / 预制体表 / `PartCollection` 三处一起撤），不会留下半成品索引。

> 如果只是想让变体不出现在建造列表里，宿主目前没有过滤开关 —— `CustomPartSelector.Initialize`
> 是遍历整个 `PartCollection` 建列表的。真需要的话得自己改那一处。

### 5.3 形状特性开关

`FrameShapeSystem` 默认是**关**的：原版建造用不到，开着会让所有框按形状重建碰撞体。
Mod 要用形状就得自己开：

```csharp
if (!ModParts.FrameShapesEnabled)
{
    ModParts.SetFrameShapesEnabled(true);
}
```

开关关着时 `FrameShapeSystem.SetShape` 第一行就返回，`PartPropertyPanel` 的「形状」按钮也隐藏；
打开后建造期的框可以逐个切换形状，`ApplyPending` 会按形状重建凸包碰撞体并把形状值写进
`_BlendFactor.z` 供着色器裁剪。注意 `SetEnabled` 会**中断正在进行的模拟**。

**打开时机很重要**：`SetFrameShapesEnabled` 内部要 `World.DefaultGameObjectInjectionWorld`
（见 `FrameShapeSystem.SetEnabled` → `GetOrCreateSystemManaged<FrameShapeSystem>()`），
而 Mod 是在 `AfterSceneLoad` 之后才加载的。稳妥写法是每帧/每 0.5 秒重试到成功为止：

```csharp
private void Update()
{
    if (m_applied) return;
    if (Time.unscaledTime < m_nextTry) return;
    m_nextTry = Time.unscaledTime + 0.5f;
    if (ModParts.FrameShapesEnabled) { m_applied = true; return; }
    try { ModParts.SetFrameShapesEnabled(true); } catch (Exception) { return; }
    m_applied = ModParts.FrameShapesEnabled;
}
```

`bpir-frame-shapes` 的 `ShapeFeatureEnabler` 就是这么写的（见第 10 节）。

### 5.4 部件钩子

`OnPartRegistered` 由 `ModHooks.NotifyPartsReady()` 触发，两个时机：
部件工厂建完时、以及每个 Mod 加载完成后补发一次（Mod 通常晚于工厂初始化）。

### 5.5 换皮肤与图标：`ModSprites`

部件贴图不是 per-instance 的 —— 整场景的部件都由 `SceneRenderSystem` 用 BatchRendererGroup
画同一张 `Part_Texture_0` 图集。要让变体用别的贴图，得走「额外图集」这条通道：

```csharp
// 1. 注册一张 Mod 自带图集（PNG 放 Mod 目录里，用 ModAssets.LoadTexture 读）
Texture2D texture = ModAssets.LoadTexture(ModDirectory, "Atlas/mine.png");
ModSprites.RegisterAtlas("My_Skins", texture, 1024, 512, 0.01302083f, out string error);

// 2. 往图集里登记精灵条目（像素坐标，格式与 Assets/TextAsset/Part_SpriteMap_*.txt 一致）
var entry = new SpriteMap.MapEntry(x: 2, y: 2, width: 105, height: 105,
                                   pivotX: 0, pivotY: 0, scaleX: 0.4f, scaleY: 0.4f);
ModSprites.RegisterSprite("my/AlienWoodenFrame", "My_Skins", entry, out error);

// 3. 建变体并换皮肤 —— 换皮必须发生在建 ECS 预制体之前，所以用带 skins 的重载
ModParts.TryAddSkinVariant(PartType.WoodenFrame, 0, 9000, "异形木框",
                           spriteKey: null,
                           skins: new Dictionary<string, string> {
                               { "异形木框", "my/AlienWoodenFrame" },   // 键可以是子物体名，也可以是原 SpriteName
                           },
                           iconSpriteKey: "Part_5_9000_Rendered",
                           out string variantError);
```

要点：

- **`RegisterAtlas` 的 `scale` 决定整个世界尺寸。** 它对应精灵表首行的 `Scale`，
  `SpriteManager.GetSpriteRect` 用它算 `VertexW/VertexH`。要跟原版图集一致的尺寸，
  图集 scale 用 `0.01302083`、每条 `scaleX/scaleY` 用 `0.4`。
  图标是 UGUI 的 `RawImage`，`VertexW/VertexH` 会直接进 `sizeDelta`，
  所以图标图集要用大一点的 scale（`0.4` 左右），否则图标只有几像素。
- **皮肤必须在建 ECS 预制体之前改。** `PartEntityFactory.CreateSprite` 在 `spawner.AddPrefab()`
  那一刻就把 `SpriteName` 烤成 `SpriteID` + `PartAtlasIndex`；先建预制体再改 `m_name` 只改到了
  托管侧对象，ECS 实体还停在基部件图集上。`TryAddSkinVariant` 为此加了 `prepare` 回调。
- **`skins` 的键是「子物体名」或「原 SpriteName」**，不是新键名。部件上的精灵组件是
  `MaterialSprite`（写 `_Rect` / `_UVRect` 的那种），不是 `MeshSprite`。
  引擎/弹簧/拳套箱/TNT 的贴图挂在 `EngineVisualization` / `SpringVisualization` /
  `Visualization` 这类子物体上，只改根节点什么都不会变。
- **抓钩这类多段精灵不要用单键换皮。** `GrapplingHook` 每个变体有 10 个子精灵
  （8 个 `XxxAttachment` + `SpringVisualization` + `Visualization`），单键会把它们全叠成一张图。
  用 `skins` 精确指定要换的那个，其余保持在基部件键上。
- 图标键名固定是 `Part_{PartType}_{index}_Rendered`，`PartSelector` 命中即用，不需要额外注册。
  `ModParts.RegisterVariantIcon(info, spriteKey)` 可以把某个皮肤条目直接注册成变体图标。
- 图集材质由 `PartAtlasRegistry` 统一管理（每张图集一份 `Material`，
  从 `Part_Texture_0` 复制并换 `mainTexture`），`SceneRenderSystem` 按实体的 `PartAtlasIndex`
  分成不同的 `BatchDrawCommand`。原版部件没有这个组件，走的仍是 0 号图集，不受影响。

> 这套通道是给 `bpreboot-specials` 加进来的，之前宿主只能「变体共用基部件贴图」。


---

## 6. 静态扫描

`ModAssemblyScanner` 在 `Assembly.LoadFrom` 之后、实例化入口之前跑。不过的包**不会注册任何东西**。

**硬拒（直接不加载）**

- 引用禁止程序集：`Steamworks` / `ModIO` / `PlayFab` / `Mono.Cecil` / `Mono.CSharp` / `Microsoft.CodeAnalysis` / `System.Reflection.Emit` / `IKVM`
- 使用禁止命名空间：`System.Reflection.Emit`、`System.Runtime.InteropServices`、`System.Diagnostics.Process`、`System.Net.Sockets`、`System.Net.Http`、`System.Net.NetworkInformation`、`Microsoft.Win32`、`Mono.Cecil`、`Mono.CSharp`、`Microsoft.CodeAnalysis`
- 任何 `PInvoke` / `DllImport`

**告警（默认放行）**

- `System.IO` / `System.Net` / `System.Reflection` / `System.Diagnostics` / `System.Security` / `System.Xml`
- 白名单：`System.IO.Path`、`Stream`、`MemoryStream`、`FileStream`、`TextReader/Writer`、`BinaryReader/Writer`、`Compression`、`Stopwatch`、`Cryptography`
- IL 里扫到的禁止成员引用（启发式，有误报可能，所以只告警）

`Modding.xml` 的 `strict-scan=true` 会把告警升级成拒绝。

---

## 7. GUID 与存档

- 每个 Mod 有 GUID，`ModRegistry` 维护 `GUID → ModPackage`。
- 存档只写 GUID 列表，不写 Mod 名字或路径。
- 载入存档时用 `ModHooks.TryResolveMissing(required, missing)` 检查缺了哪些。
- 老 Mod（没有 `<ID>`）的 GUID 由 `MD5("bpsim:" + id)` 派生，**稳定且可预测**，所以老存档升级后照样能认。

---

## 8. 迁移

**清单**：`manifest.json` 可以不动（加载器会回退读取），但建议换成 `Mod.xml` 并把 id 写进 `<ID>`。

**代码**：`IBpsimMod` 继续可用；想用钩子就改成 `ModEntryPoint`。

**BepInEx**：整条通道已删。
- `Bpsim.Mods.BepInExHost.BpsimModHost` 不存在了，插件里调它的代码要删掉。
- `Mods/FrameShapes/` 里的插件工程不再被部署，`FrameShapesBuildPostprocessor` 已删除。
- 内置占位包 `bpir-frame-shapes` 已移除（它原本只是等 BepInEx 插件注册的壳）。

---

## 9. 已知限制

1. **源码编译依赖 CodeDom**。Mono 下可用，但若某平台的运行时裁掉了 `Microsoft.CSharp`，
   源码 Mod 会加载失败（报错明确）。预编译 DLL 永远可用。
2. **程序集不可卸载**。Mono 的限制，禁用 Mod 只调 `OnUnload` 并停止派发钩子，代码仍在内存里。
3. **扫描是「先加载再扫描」**。同样因为不能卸载，扫描不过的包其程序集已经进内存了 —— 它不会被执行，但不是零痕迹。
4. **工坊没有验签**。`Modding.xml` 只记录，不校验；工坊分发要做完整性校验得另外加。
5. **加不了新形状**。`FrameShape` 的 4 个值写死在着色器的裁剪分支里（`_BlendFactor.z`），
   Mod 只能在这 4 种里选；要加梯形/六边形得先改 shader。
6. **变体部件默认共用基部件的贴图与图标**，但宿主提供了「换皮肤 / 换图标」的 API（见 5.6）。
   不调那些 API 时，变体的颜色与图标都继承基部件：图标走 `Part_{type}_{index}_Rendered`，
   Mod 索引没有这张图，选择器里会回退到基部件那一格
   （`PartSelector.SetRenderedIcon(PartTypeInfo)` 的解析链就是 自己 → 基部件 → 皮肤占位图）；
   `ColoredFrame.GetColor` 经 `PartVariants.Resolve` 取基部件色，克隆红色框得到的变体就是红的。
7. **建造列表没有过滤机制**。`CustomPartSelector.Initialize` 遍历整个 `PartCollection` 建列表，
   宿主没有给「某个索引不显示」的开关键；不想让变体出现在列表里，只能不注册变体（见 5.2 开头）。
8. **源码 Mod 的编译引用集不能显式挂 `mscorlib`**。宿主用的是 Mono 的 `mcs`，它总是隐式引用
   自己的 `lib/mono/4.5/mscorlib.dll`；再显式挂一份（Unity 的 `unityjit-win32/mscorlib.dll` 或
   `unity-4.8-api/mscorlib.dll`）会炸 `CS0433: The imported type ... is defined multiple times`。
   正确做法是**不引用 `mscorlib` + 显式引用 `netstandard` 门面**
   （`unityjit-win32/Facades/netstandard.dll` 或 `unity-4.8-api/Facades/netstandard.dll` 都行；
   不能用 `NetStandard/ref/2.1.0/netstandard.dll`，那会带出 `CS0433` + `CS0315` + `CS1674`）。
   少了 `netstandard` 就会回到原始报错 `CS0012: The type 'System.Object' is defined in an assembly
   that is not referenced ... 'netstandard, Version=2.1.0.0'`。

## 10. 样例：BPIR 变形框

`Assets/StreamingAssets/Mods/bpir-frame-shapes/` 是一个**能直接跑**的源码 Mod：

```
bpir-frame-shapes/
  Mod.xml
  Code/BpirFrameShapes.cs
```

它做的事：

1. 建一个 `ShapeFeatureEnabler` 常驻对象，每 0.5s 重试 `ModParts.SetFrameShapesEnabled(true)` 直到成功 ——
   打开之后**任何容器框**（木框 / 铁框 / 彩色框 / 透明框）的部件属性面板里都会多出「形状」一行，
   点它循环 长方形 → 直角三角 → 半圆 → 圆；
2. `OnUnload` 把形状特性关掉（所有框恢复方框外观与碰撞，存档里的形状值保留）；
3. `IsRequiredForMachine` 在存档 JSON 里出现 `"FrameShape":1|2|3` 时返回 true —— 宿主会把 GUID 记进载具。

**它不新增任何部件**：建造列表和原版完全一致，没有 9000+ 索引，也没有多余图标。
最初版本是「每形状一个变体」，实测下来有三个副作用（列表多出三格、图标只能是基部件那张图、
放下后颜色等于基部件色），所以改成了现在这种「只开特性」的形态。

顺便一个容易踩的点：`MetalFrame#12` 这个彩色框**本来就是红色**（`ColoredFrame.GetColor` 里
`num = 12-12 = 0` → HSV(0°, 0.7, 0.9) → RGB `(0.9, 0.27, 0.27)`），图集里那格也是纯红方块。
所以「放下变红」不是染色出错，是选错了基部件色。想要白色/灰色的框，用透明框 `#132` 或灰度框。

验收用 `Bpsim.QA.ModFrameShapesQA`（编辑器 Play 模式下 `invoke_static type=Bpsim.QA.ModFrameShapesQA
method=Run`，再 `method=Status` 取报告）：覆盖「建造列表里没有 9000+ 索引」「变体登记表为空」
「Mod 把形状特性打开了」「属性面板出现形状行」「点四次循环 4 种形状 + 凸包碰撞体 + `_BlendFactor.z`」
「切形状不改颜色（红框与白框各验一遍）」「存档往返保留 FrameShape」「模拟能启动」共 47 项；
`method=Focus` / `method=Capture` 可以把相机挪过去并存一张图到 `Logs/bpsim-frameshapes.png`。

形状与凸包生成沿用 bpsim 已有的 `FrameShapeSystem`，几何来自 BPIR 的逐件框形状
（<https://gitee.com/yctf123123/bpir>）。

## 11. 样例：bpreboot 特殊部件

`Assets/StreamingAssets/Mods/bpreboot-specials/` 是另一个源码 Mod，演示 5.5 的换皮肤通道：

```
bpreboot-specials/
  Mod.xml
  Code/BprebootSpecials.cs
  Atlas/bpreboot_skins.png      # 1024x318，21 张皮肤
  Atlas/bpreboot_skins.txt      # 皮肤图集条目表
  Atlas/bpreboot_icons.txt      # 图标图集条目表（共用同一张 PNG）
  Tools/pack_atlas.py           # 从 bp-rebooted 上游图集裁切打包的脚本
```

它把 <https://github.com/Goggs77/bp-rebooted> 里 BPLE 没有的皮肤变体做成**额外**部件
（索引 9000 起，原版索引一个不动），17 件：木框 2（异形木框 / 玻璃框）、铁框 4
（半高护栏 / 60° / 45° 三角 / 钨方块）、中型发动机 1、大型发动机 1、拳套箱 1（活塞）、
吸盘轮 1、抓钩 5（异形速射枪 / 加长速射枪 / 闪电钩 / 磁轨炮 / 加特林）、螺旋桨 1、
弹簧 1、TNT 1。物理参数（质量 / 引擎功率）按上游 `part-params.csv` 写进 `PartExtensionMap`。

验收用 `Bpsim.QA.ModBprebootSpecialsQA`（`method=Run` → `method=Status`，23 项全过）：
覆盖「17 件都进 PartCollection」「预制体都在 spawner 里」「图集材质有独立贴图」
「变体精灵实体带 Mod 图集序号」「皮肤/图标键可查」「物理参数覆写生效」
「原版各家族索引数一个没变」「17 件都能真的放到场上」。
`method=DumpSkins` 打印每件变体每个子精灵的键名，用来核对皮肤落在哪个节点；
`method=ShowAll` + `method=Capture` 把 17 件摆成网格并截图到 `Logs/bpsim-bprebootspecials.png`。


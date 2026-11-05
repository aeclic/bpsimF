# BPSIM 存档格式评审：savefile/ 样本 × BPRC BPIR CSV × Besiege .bsg

> 评审对象：`savefile/` 下 4 个真实存档、仓库根目录的 3 个 perf 测试载具 CSV、BPRC（父工程）的 BPIR CSV 规范、Besiege `.bsg`（`D:\SteamLibrary\steamapps\common\Besiege\Besiege_Data\SavedMachines\` 共 9 个）。
> 关注问题：**哪种格式更适合 bpsim 现在和以后**、**哪种最适合让 AI 生成 QA 用例并回放**、**能优化什么**。
> 本文只做只读分析，未改动任何代码；所有数字均由脚本在样本上实测。

---

## 0. 结论摘要

1. **`savefile/Level_Sandbox_04_4` 目前在 bpsim 里 100% 加载失败**（287/287 行抛异常）。它不是损坏文件，而是 BPRC BPIR 的 **14 列**导出；bpsim 的 13 列读取器把第 12 列（`@offX=0.5;@layer=-50` 这类注解串）当成 `FrameShape` 去做 `int.Parse("")`。这是**同一扩展名、不同列语义、没有任何格式判别**导致的误读，不是数据问题——用 BPRC 真正的第 13 列图层重放，287/287 全部能放下。
2. **bpsim CSV 与 BPRC BPIR CSV 双向都不兼容**（两个方向都实测跑过）：bpsim 写出的 13 列喂给 BPRC `ReadCsv` 会炸，BPRC 写出的 7 列喂给 bpsim 虽然不炸但**静默丢掉全部自由变换和图层**。二者共享同样的 6 列前缀，尾部完全冲突，且**没有任何版本/魔数可以区分**。
3. **格式选择上，CSV 是对的，不要换成 XML 或 JSON**。同 20 405 件：6 列 CSV 397 776 B，bpsim 现在强制写的 13 列 683 446 B（×1.72），紧凑 JSON 3 478 938 B（×8.75），13 属性 XML 4 070 809 B（×10.2）。CSV 比 JSON 小 8.75 倍，gzip 后仍小 1.16–1.22 倍。`.bsg` 每件 0.64–0.83 KB，是 bpsim 13 列的 ~20 倍。
4. **但 `.bsg` 有 4 个设计要素值得抄，而不是抄它的编码**：文件头自描述（版本 + 类型）、每件一条实例 `guid`、`requiredMods` 依赖清单、key 命名空间隔离（`bmt-*` / `lel-*` / 无前缀）。这四个恰好是 bpsim 存档现在缺的全部东西。
5. **对「AI 写 QA 并加载」而言，当前 CSV 是 QA 目录 21 个 harness 里最可自动化的一种，但读取器有 9 个会让 AI 直接翻车的硬失败点**（表头行、注释行、纯空白行、`43.0` 这种 float 写进 int 列、分号分隔、`false`、制表符……）。好消息是这些全部是**读取器容错问题，不是格式问题**，改读取器比换格式便宜一个数量级。
6. **最该修的不是格式，是静默失败**：未知部件（`PartSceneJobs.cs:78-88` 没有 else）、放置被拒（`PlacePartAt` 返回 false 但调用方不看）、`LevelQa.LoadSceneFile` 硬编码 `SchematicsFormat.Xml`、XML 路径把 `SkinPart` 映射成 `Unknown`。这些让「加载成功但场景不对」成为最难查的一类 QA 事故。

---

## 1. 样本盘点（实测）

### 1.1 `savefile/` 四个存档

| 文件 | 字节 | 行数 | 列数 | 换行 | BOM | bpsim 读取结果 |
| --- | ---: | ---: | --- | --- | --- | --- |
| `Level_Sandbox_04_4` | 19 611 | 287 | **14** | LF | 无 | **0 成功 / 287 失败** |
| `bpsim1号机` | 9 407 | 594 | 6 | CRLF | 无 | 594 / 0 |
| `不败荣光` | 28 850 | 1 648 | 6 | LF | 无 | 1 648 / 0 |
| `天使座` | 397 776 | 20 405 | 6 | LF | 无 | 20 405 / 0 |

- `savefile/Level_Sandbox_04_4` 第 1 行：`6,10,107,633,0,0,-1,-1,-1,-1,-1,-1,,2`
  bpsim 读取器在 `int.Parse(array[12])` 处抛 `FormatException: The input string '' was not in a correct format`（C# 侧），脚本侧对应 `invalid literal for int() with base 10: ''`。
- `savefile/` 目录本身 **git 未跟踪**（`git status --porcelain savefile` → `?? savefile/`），也没有任何代码/文档/脚本引用它（在 `.cs` / `.md` / `.sh` 内 `grep savefile` → 0 命中）。它是游离的参考数据。
- 另外 `perf/` 下的 `angel.csv`（= 天使座）、`buhai.csv`（= 不败荣光）以及 `angel_2k/5k/10k.csv` 是 `perf/run.sh` 真正驱动的输入，`README.md:76` 把这几个叫作「仓库根目录的测试载具 CSV」。

### 1.2 `.bsg` 九个存档

| 文件 | 字节 | Block 数 | B/件 |
| --- | ---: | ---: | ---: |
| LA1 | 32 869 | 47 | 699 |
| LA3 | 33 809 | 46 | 735 |
| LA5 | 26 901 | 38 | 708 |
| LA7 | 19 480 | 30 | 649 |
| LA12 | 27 647 | 40 | 691 |
| LAODA | 35 756 | 56 | 638 |
| S | 30 481 | 44 | 693 |
| S1 | 24 852 | 30 | 828 |
| LA | 33 685 | 46 | 732 |

`.bsg` 结构（实测 LA7 头尾）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--Besiege machine save file.-->
<Machine version="1" bsgVersion="1.3" name="LA7">
    <Global><Position x="0" y="4.458098" z="0" /><Rotation x="0" y="0" z="0" w="1" /></Global>
    <Data><StringArray key="requiredMods" /></Data>
    <Blocks>
        <Block id="1" guid="abdde4a6-…">
            <Transform><Position …/><Rotation …/><Scale …/></Transform>
            <Data><Integer key="length">2</Integer></Data>
        </Block>
    </Blocks>
</Machine>
```

`<Data>` 是**带类型的键值表**，元素名恰好 9 种：`Boolean / Color / Integer / IntegerArray / Single / SingleArray / String / StringArray / Vector3`。key 分三个命名空间：`bmt-*`（mapper）、`lel-*`（关卡逻辑）、无前缀（Block 自身字段）。缺 key → 回落到 mapper 缓存的默认值（`SaveableDataHolder.cs:401-414`），这是它的前向兼容机制。

---

## 2. 三种格式逐项比对

| 维度 | bpsim CSV（现状） | BPRC BPIR CSV | Besiege `.bsg` |
| --- | --- | --- | --- |
| 容器 | 纯文本，一行一件 | 纯文本，一行一件 | XML |
| 列/字段 | 固定 13 列，无表头 | 6–14 列，尾列含义版本相关 | 每件一个 `<Block>`，字段在 `<Data>` 键值表 |
| 自描述 | **无** | **无** | **有**（`version` / `bsgVersion` / 类型标签） |
| 版本字段 | **无** | 注解串里有 `@bpir20220110:2` 转换标记，但**只在自定义成员里**，不是文件头 | `version="1" bsgVersion="1.3"` |
| 格式判别（魔数/嗅探） | **无** | **无** | 有：`XmlSaver.IsXmlFormat` / `IsBsgFormat` 读首行判断 |
| 实例 ID | **无**（位置即身份） | **无** | 每件 `guid` |
| 类型 ID | `type,index` 数字对 | 同左 | `id`（数字）+ `guid` |
| 部件私有属性 | **无法表达** | `@offX=…;Foo:bar=…` 字符串 | 带类型的 `<Data>` 键值表 |
| 依赖清单 | **无** | **无**（只有转换标记） | `requiredMods` StringArray |
| 浮点精度 | `"R"`（往返无损） | `ToString("R", Invariant)` | G7（`-5.960464E-08`、`0.7071068`），**不往返** |
| 键顺序确定 | 是（按行） | 是 | **否**（HashSet，同一场景两次保存不一致） |
| 编码/换行 | UTF-8 无 BOM / LF | 无 BOM / `AppendLine()` 在 Windows 上是 CRLF | UTF-8 / 缩进 4 空格 |
| 压缩/校验 | 无 | 无 | 无 |
| 每件体积 | 16 B（6 列）/ 38 B（13 列） | ~45 B | 638–828 B |
| 20 405 件实测 | 397 776 / 683 446 B | — | ~13 MB（外推） |
| gzip 后 | 60 187 / 62 695 B | — | — |
| 人类可读 | 高 | 中（注解串糊成一片） | 中（冗长但结构清楚） |
| `diff` 可用 | **是**（一行一件） | 是（但尾列噪声大） | **否**（顺序不稳定） |
| AI 直接生成 | 最容易，但容错差 | 中，要背注解语法 | 中，XML 冗长，模型易漏闭合 |

### 2.1 三个格式的语义并不等价

| 能力 | bpsim CSV/JSON/XML | BPRC BPIR | `.bsg` |
| --- | --- | --- | --- |
| 网格位置/朝向 | ✅ | ✅ | ✅（连续 Transform） |
| 自由变换（偏移/精细角/缩放） | ✅ 仅 CSV/JSON | ✅ `@offX/@rotZ/@stretchX` | ✅ `<Transform>` 连续值 |
| 建造图层 | ✅ 1..16 | ✅ 列 13 = 1..6，`@layer` = -50..50 Z 序 | ❌ 无该概念 |
| 部件私有状态（齿轮箱开关、轮子力、燃料、颜色…） | ❌ **完全无法存** | ✅ `Component:Member=value` | ✅ 每件 `<Data>` |
| 皮肤几何（`SkinSheet`） | ❌ | ❌ | ❌ |
| Mod 变体身份 | ❌（9000+ 索引不记录来源） | ❌ | ✅ `requiredMods` |

`Schematics.Unit`（`Schematics.cs:9-65`）只有 13 个字段，全是布局信息。在 `Schematics*.cs` / `ISchematicsLoader.cs` 里搜 `Settings|CustomParams|Extension` **0 命中**；`PartExtensionData`（`PartExtensionData.cs:5-30`）存在但 Schematics 完全够不到。也就是说：**bpsim 的存档是「布局存档」，不是「载具存档」**——这是与 `.bsg` 最本质的差距，比列数问题重要得多。

---

## 3. 关键发现

### 3.1 【P0】`Level_Sandbox_04_4` 加载必然失败

bpsim 读取顺序（`SchematicsCsvLoader.cs:18-57`）：

```csharp
if (array.Length >= 11) { offsetX = float.Parse(a[6]); … scaleY = float.Parse(a[10]); }
if (array.Length >= 12) { layer = int.Parse(a[11]); }
if (array.Length >= 13) { frameShape = int.Parse(a[12]); }   // ← 这里
```

而 BPRC 的 14 列定义（`BpirSaveConverter.cs:120-141` / `docs/BpirCompatibility20261002.md:17-21`）是：

```
0 Type | 1 Index | 2 X | 3 Y | 4 Rotation | 5 Flipped | 6 Mass | 7 Gravity | 8 Drag
9 Size | 10 Power | 11 Suction | 12 Members(@注解) | 13 Layer
```

同一个 `array.Length >= 13` 在两边指的不是同一件事：

| 列 | bpsim 认为 | BPRC 认为 | 后果 |
| --- | --- | --- | --- |
| 6 | OffsetX (float) | Mass (float) | 值域巧合时静默错位 |
| 8 | RotationDegrees | Drag (int) | 同上 |
| 11 | Layer (1..16) | Suction (float) | 样本里恒为 -1 → 被 clamp 成图层 1，图层信息全丢 |
| 12 | FrameShape (int 0..3) | Members 注解串 | **抛异常**（`int.Parse("")` / `int.Parse("@offX=0.5")`） |
| 13 | 不存在 | Layer (1..6) | 被完全忽略 |

**同一个 14 列文件，如果只修第 12 列的类型问题，仍然会把 287 件全部压到图层 1、丢掉 BPRC 的 6 个图层。** 实测两种读法的放置结果：

| 读法 | 放置成功 | 被丢弃 |
| --- | ---: | ---: |
| 现状（layer 取第 11 列 = -1 → clamp 1） | 151 | **136（47.4%）** |
| 按 BPRC 真实列（layer 取第 13 列） | **287** | **0** |

即：**丢的 47.4% 是纯粹的列偏移，数据一行没少。** 第 13 列的图层分布是 `{1:122, 2:40, 3:38, 4:35, 5:14, 6:38}`，50 个格子叠了最多 10 件——BPRC 的分层建筑在 bpsim 的单一平面上必然重叠，所以「修好列映射」之后还得接受「部分堆叠会被容器规则吸收或拒绝」。

### 3.2 【P0】双向都不兼容（实测）

| 输入 | 目标 | 结果 |
| --- | --- | --- |
| bpsim 13 列 `6,10,107,633,0,0,0.5,0.25,30,1.5,2,2,1` | BPRC `ReadCsv` | ❌ `invalid literal for int(): '0.25'`（第 7 列 gravity 要 int） |
| bpsim 13 列恒等行 `…,0,0,0,1,1,2,0` | BPRC `ReadCsv` | ❌ `无效的成员参数 0`（第 12 列被当 members 的 `key=value`） |
| BPRC 7 列 `6,10,107,633,0,0,m_mass:1.5;@offX:0.5;@rotZ:30;@layer:4` | bpsim | ⚠️ 不报错，但**静默丢光**：读成 `offset=0, angle=0, scale=1, layer=1, shape=0`（7 列 < 11，尾部整段被忽略） |

两边共享同样的 6 列前缀，尾部语义对立，**而且没有任何版本号或魔数能让读取器知道自己在读哪一种**。这正是 `BPSIM_BUILD_PAGE_LAYOUT.md:88` 记录过的事故的放大版：那次是 12 列 → 13 列，已经让别的会话的断言失效过一次；下一次改动照样会静默错位。

### 3.3 【P1】XML / EncryptedXml 路径会丢部件

`SchematicsXmlLoader.cs:57-70` 走 `ConvertExtensions.EnumConverter`，它**按枚举成员名**映射（`Enum.TryParse`），不是按数值。`PartType.MetalFrame = 6`，`LegacyPartType.MetalFrame = 18`——同一个数字在 CSV 和 XML 里不是同一个部件：

| 文件里写的 | CSV 读成 | XML 读成 |
| --- | --- | --- |
| `6` | MetalFrame | Bellows |
| `1` | Pig | Balloon |
| `10` | MotorWheel | Pig |

更糟的是 `PartType.SkinPart = 49` 在 `LegacyPartType` 里**没有对应成员**（已确认：全文件只有 `Basket=8` 和 `ObsoleteWheel=38`），所以 XML / EncryptedXml 写出一个皮肤件会写成 `LegacyPartType.Unknown(0)`，读回来变成 `PartType.Unknown(0)`，**部件直接消失且无任何日志**。CSV 和 JSON 没有这个问题。

### 3.4 【P1】静默失败链

| 位置 | 行为 |
| --- | --- |
| `PartSceneJobs.cs:78-88` | `if (PrefabCollection.TryGetValue(key, out var item)) { … }` —— **没有 else、没有日志**，未知 (Type,Index) 直接消失 |
| `PartSceneUnmanaged.PlacePartAt` | 返回 `bool`，但 `PlacePartsJob` 不检查，`CanPlacePartAt` 失败即丢弃 |
| `LevelSession.LoadSceneFile` :168-207 | `catch (Exception) { error = exception.Message; }` —— 无行号、无列号、无堆栈 |
| `SchematicsJsonLoader.Validate` | `Units == null` 时 `throw new NullReferenceException()` —— 空消息 |
| `SchematicsEncryptedXmlLoader.Read` | **不调用** `Normalize()/Normalized()`（CSV/JSON/XML 都调）—— 加密档的 NA/Inf 缩放不会被归一 |
| `LevelQa.cs:461-470` | `LoadSceneFile(path)` 硬编码 `SchematicsFormat.Xml` —— 传 CSV 进去会被当 XML 解析 |
| `LevelManager.cs:178` / `LevelSession.SceneFormat` | 关卡起始载具、会话默认格式都硬编码 Xml |

**「加载成功但场景不对」是 QA 里最难查的故障类型**，而 bpsim 目前有 4 条独立的静默丢弃路径。

### 3.5 AI 生成 QA 文件的失败矩阵（按 bpsim 真实解析循环实测）

| AI 可能写出的变体 | 结果 |
| --- | --- |
| `6,28,43,18,0,0` | ✅ 成功 |
| ` 6 , 28 , 43 , 18 , 0 , 0`（带空格） | ✅ 成功（`int.Parse` 容忍空白） |
| `6,28,43,18,0,0,`（多一个逗号） | ✅ 成功（7 列 < 11 门槛） |
| `type,index,x,y,rotation,flipped`（表头行） | ❌ `int.Parse("type")` 失败 |
| `# 我的测试车`（注释行） | ❌ `int.Parse("# 我的测试车")` 失败 |
| 纯空白行 | ❌ `int.Parse("   ")` 失败（只判了 `IsNullOrEmpty`，没判 `IsNullOrWhiteSpace`） |
| `43.0`（int 列写了 float） | ❌ `int.Parse("43.0")` 失败 |
| `false` 代替 `0`（flipped 列） | ❌ 失败 |
| 分号分隔 | ❌ 失败 |
| 制表符分隔 | ❌ 失败（BPRC 接受 `,` 和 `\t`，bpsim 只认 `,`） |

首行一旦是表头/注释，那个文件里**只有那一行失败**（其余行照常加载）——比整文件失败更危险，因为零件数看起来"差不多对"。

### 3.6 体积实测（同一场景 20 405 件）

| 编码 | 原始 | gzip -9 | 相对 6 列 |
| --- | ---: | ---: | ---: |
| 6 列 CSV | 397 776 B | 60 187 B | 1.00× |
| 13 列 CSV（bpsim 现在强制写的） | 683 446 B | 62 695 B | 1.72×（gzip 1.04×） |
| 紧凑 JSON（同 13 字段） | 3 478 938 B | 73 533 B | 8.75×（gzip 1.22×） |
| 13 属性 XML | 4 070 809 B | — | 10.2× |
| `.bsg` 外推 | ~13 MB | — | ~33× |

每件成本：6 列 14.8–19.5 B，13 列 28.8–33.5 B，JSON 165–170 B，`.bsg` 638–828 B。

**关键点：把尾部恒等列裁掉，能精确还原原始字节。** 天使座 397 776 → 裁剪后 397 776（一字节不差），不败荣光 28 850 → 28 850，bpsim1号机 9 407 → 8 813（差 594 B 正是 CRLF→LF）。即：**「只在非默认时才写尾部列」可以让往返零膨胀**，而现在的强制 13 列凭空多花 72%。

---

## 4. 哪种格式更适合 bpsim

分层结论，不是单一答案：

### 4.1 存档本体：**继续用 CSV**，不要换 XML/JSON

理由按重要性排序：

1. **体积**：JSON 是 CSV 的 8.75 倍、`.bsg` 是 33 倍。天使座这种 2 万件的载具，JSON 会到 3.5 MB，`.bsg` 到 13 MB。
2. **`diff` 友好**：一行一件，git 上加减零件是干净的 ±1 行；`.bsg` 因为 HashSet 顺序不定，**同样内容两次保存字节不同**，无法做 golden file 比对；BPRC 的注解串把十几个键糊在一列里，diff 粒度也差。
3. **精度**：bpsim 用 `"R"` 往返无损；`.bsg` 用 G7，`-5.960464E-08` 这种值写进去再读出来就不是原值了。
4. **现有投资**：QA 目录 21 个 harness（`FreeTransformQA`、`LayerConnectionQA`、`BpsimPlayQA`、`ModFrameShapesQA`、`SkinSheetQA` …）、`perf/run.sh`、`SpinPerfProbe`、`BpsimPlayQA.SaveLoad` 全都已经跑在 CSV 上，切换成本高、收益为负。

### 4.2 借 `.bsg` 的四个"骨架"要素，别借它的编码

| 抄什么 | 为什么 | bpsim 现状 |
| --- | --- | --- |
| **文件头自描述** | 让读取器自己知道版本/列数，杜绝 3.1/3.2 那类静默错位 | 无 |
| **每件实例 ID** | 稳定引用「第 N 个零件」，QA 断言和 bug 报告才能指名道姓 | 无（位置即身份，挪一下就变） |
| **依赖清单** | Mod 变体（`PartVariants` 9000+ 索引）缺失时能报错而不是静默消失 | 无 |
| **key 命名空间隔离** | `bmt-*` / `lel-*` / 无前缀 三段式，扩展字段永不撞名 | 无扩展字段 |

加上第 5 条 bpsim 自己就该有的：**部件私有状态的扩展位**（BPRC 的 `Component:Member=value` 或 `.bsg` 的 `<Data>`）。现在的 `Schematics.Unit` 只能存布局，齿轮箱开关、轮子力、燃料量、颜色全部不进存档——这是比列数更根本的缺口。

### 4.3 具体形态建议

**保持 CSV 作为主格式，但升级成「带可选表头的版本化 CSV」：**

```
#bpsim,1,cols=13
# 我的测试车 —— AI 生成的 QA 夹具
6,28,43,18,0,0
6,28,44,18,0,0
43,0,50,20,3,0,0.5,0.25,30,1.5,2,2,1
```

- 第 0 行 `#bpsim,<版本>,cols=<列数>` 是可选的；有就校验，没有就按旧规则（6/11/12/13）。
- `#` 开头的行是注释，跳过。
- 尾部恒等列不写（省 72% 体积，实测精确还原原字节）。
- 14 列（BPRC）文件**显式拒绝**并提示「这是 BPIR 导出，请走 `bpir` 导入模式」，而不是现在的 `int.Parse` 崩在中间。

这个格式同时满足：AI 好写（一行一件 + 可注释 + 可跳过表头）、读取器好校验（列数自证）、体积最小、`diff` 友好、且旧文件 100% 兼容。

### 4.4 关于「AI 写 QA 加载哪个更好」

需要把两个场景分开：

| 场景 | 数据量 | 推荐 | 理由 |
| --- | --- | --- | --- |
| 性能/回归夹具（`perf/angel.csv` 这类） | 2k–20k 件 | **CSV** | 体积、生成速度、`diff` 都是 CSV 赢 |
| 手写/小规模 QA 场景（10–100 件） | 很小 | **CSV 优先，JSON 可用** | 小规模下体积无所谓；JSON 的括号结构对模型更"防呆"（不会数错列），而且 bpsim 已经支持 |

但无论选哪个，**当前读取器的 9 个硬失败点必须先修**——否则 AI 写出来的 CSV 有一半概率首行就崩，而 JSON 路径失败只有一个裸 `NullReferenceException`，同样没法用。

---

## 5. 优化清单（按优先级）

### P0 —— 不修就会继续出错

| # | 位置 | 改动 |
| --- | --- | --- |
| 1 | `SchematicsCsvLoader.Read` | 用 `IsNullOrWhiteSpace` 跳空行；`#` 开头当注释跳过；解析首行 `#bpsim,<ver>,cols=<n>` 并校验列数 |
| 2 | `SchematicsCsvLoader.Read` | `int.Parse` → `int.TryParse`，失败时先试 `float` 再 `Mathf.RoundToInt`（容忍 `43.0`），仍失败则抛出**带行号列号**的 `FormatException` |
| 3 | `SchematicsCsvLoader.Read` | 14 列检测：`array.Length == 14` 且第 12 列非纯整数 → 抛「疑似 BPIR 导出」专用错误，而不是 `int.Parse("")` |
| 4 | `LevelSession.LoadSceneFile` | `error` 里带上 `exception.Message` + 行号；保留堆栈到 `Debug.LogException` |
| 5 | `PartSceneJobs.PlacePartsJob` | 加 `else { Debug.LogWarning($"未解析的部件 {key} × {count}"); }`，并把计数回传给调用方 |
| 6 | `PartSceneUnmanaged.PlacePartAt` 调用点 | 收集 `false` 返回，最后汇总一条「N 件放置被拒（格子已占用/容器规则）」 |

### P1 —— 格式演进

| # | 改动 |
| --- | --- |
| 7 | 写侧「尾部恒等列裁剪」：`offset==0 && angle==0 && scale==(1,1) && layer==1 && shape==0` 时只写 6 列。实测可精确还原原始字节 |
| 8 | 写侧输出首行 `#bpsim,1,cols=N` |
| 9 | 扩展一列 `k=v;k=v` 的尾部字段（BPRC 风格），承载部件私有状态；与 `@` 命名空间对齐，避免和 BPRC 第 12 列撞车 |
| 10 | `Schematics.Unit` 增加 `Guid` 或稳定序号，写进第 14 列（可选） |
| 11 | 文件头记录 `mods=<name>@<ver>;…`，加载时若 `PartVariants` 里找不到对应变体 → 明确报错 |

### P2 —— 一致性与 QA

| # | 位置 | 改动 |
| --- | --- | --- |
| 12 | `SchematicsXmlLoader` | `SkinPart` 无 legacy 对应 → 要么加 `LegacyPartType.SkinPart`，要么 XML 路径直接拒绝序列化皮肤件 |
| 13 | `SchematicsEncryptedXmlLoader.Read` | 补 `.Normalize()`（现在唯一漏掉的路径） |
| 14 | `SchematicsJsonLoader.Validate` | 裸 `NullReferenceException` → `FormatException("schematics.units 缺失")` |
| 15 | `LevelQa.LoadSceneFile` | 硬编码 `SchematicsFormat.Xml` → 按首行/扩展名嗅探，或加 format 参数 |
| 16 | `FreeTransformQA.cs:216` | `csv 12 columns` 断言与写入器（13 列）矛盾，**当前是假断言**；两处 `CsvColumnCount` 辅助函数都读 `Split('\n')[0]`，空场景会 `IndexOutOfRange` |
| 17 | 新增 `SchematicsFormatQA` | golden file：四个 `savefile/` 样本 + 三类边界（空文件、单行、纯注释）走 全格式 × 全读法 矩阵，断言「件数 / 逐件字段 / 无静默丢弃」 |
| 18 | `README.md:199` | 「11 列 CSV」已过期，与代码的 13 列不符 |

### 建议的错误提示模板

```
场景加载失败：第 7 行第 13 列「FrameShape」= "0.5" 不是整数
  文件：Level_Sandbox_04_4（检测到 14 列，疑似 BPIR 导出）
  提示：BPIR 的第 12 列是注解串、第 13 列是图层；bpsim 的 CSV 只接受 6/11/12/13 列。
```

---

## 6. 一句话回答

**格式本身 CSV 是对的，问题不在格式而在「没有版本、没有表头、没有容错、没有诊断」。** 换成 `.bsg` 那种 XML 会让文件大 33 倍且不可 diff，换成 JSON 大 8.75 倍；正确做法是给 CSV 加一个可选的自描述首行、把尾部恒等列裁掉、把读取器的 9 个硬失败点改成「带行号的明确报错」，并把 `.bsg` 的四个骨架要素（版本头、实例 ID、依赖清单、命名空间）搬过来。对 AI 写 QA 来说，改造后的 CSV 是三者里唯一同时满足「一行一件好写、`diff` 可查、体积可接受、旧档全兼容」的选择。

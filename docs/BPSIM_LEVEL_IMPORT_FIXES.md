# 关卡导入三项修复（碰撞 / 贴图 / 启动恢复）

对应三个问题：

1. 导入关卡后地形与载具之间没有物理碰撞；
2. 导入关卡后部分贴图缺失（背景一大片纯白）；
3. 每次重启都要重新点「加载场景」和「关卡」，希望自动恢复上次的状态。

三项都改在 `Assets/Scripts/Assembly-CSharp/Bpsim/Levels/` 与 `Tools/BPSim/export_levels.py`，
数据侧（`Assets/StreamingAssets/BpsimLevels/`）由导出脚本重新生成。

---

## 一、地形碰撞：零厚度三角面片 → 有厚度的挤出棱柱

### 根因

`LevelImporter.AddTerrainCollider` 原本直接把地形的填充三角形交给 `MeshCollider.Create`：

```csharp
Vector3 point = transform.TransformPoint(fill.Vertices[i]);   // z 恒等于地形物体的世界 z
```

而 `LevelImporter.DepthOffset = 6f` 会把整个关卡（含地形）推到 `z = +6`，载具零件却在 `z = 0`
（`PartFreeTransformSystem` 只按建造层做 `-0.02` 级偏移）。于是碰撞面停在 `z ≈ 6` 的平面上，
和零件平面相差 6 个单位，**永远不可能接触**。注释里写的“triangle colliders 双面接触”没错，
错的是它默认两者在同一个 z。

### 修法

照主工程 `e2dConstants.COLLISION_MESH_Z_DEPTH = 10f` 与 `LevelLoader.CreateCollider` 的做法，
把填充多边形沿 z 挤出成实体棱柱：

- 顶点先经 `transform.TransformPoint` 转到世界坐标，再 `Snap()` 量化到 1e-3 后焊接
  （地形网格里同一个位置经常有多个索引）；
- 用 `HashSet<long>` 收集三角形的**有向边**，反向边不存在的才是轮廓边
  （内部共享边正反各出现一次，互相抵消），这样保留原始绕序、墙面法线朝外；
- 每条轮廓边生成 4 个顶点 `(pa,-5) (pb,-5) (pb,+5) (pa,+5)` 与 2 个三角形，
  即 `z ∈ [-5, +5]` 的竖直墙面，**跨越零件所在的 z = 0**。

`ColliderHalfDepth = 5f` 对应主工程的 `COLLISION_MESH_Z_DEPTH / 2`。

### 验证

`LevelQa` 里加了三个探针（都在 Play Mode 里通过桥接调用）：

| 方法 | Level_05 实测 |
| --- | --- |
| `ColliderBounds()` | `z=-5..5`，`straddles-part-plane=yes` |
| `RaycastTerrain(39.6,-48.9)` | `hit[0] at y=-7.71 z=0`，`hits=1` |
| `DropProbe(39.6,-20)` + `DropReport()` | 动态球从 `y=40` 落到 `y=-7.21` 停住，随后沿坡面滚走（`x` 39.6→49.25，速度 −24.5→−2.2） |

`DropProbe` 造的是真正的动态刚体（`PhysicsMass.CreateDynamic` + `PhysicsGravityFactor = 1`），
所以它停在 `y ≈ -7.21` 就是“载具会撞到地形”的端到端证据，而不只是几何包含关系。

---

## 二、贴图缺失：复合 prefab 被压成单个 quad

### 根因

`Tools/BPSim/export_levels.py` 的 `describe_prefab()` 只读 **根物体上第一个命中的脚本**
（`e2dTerrain` → `UnmanagedSprite` → `Sprite`），然后 `return`。但主工程的背景是一个
**复合 prefab**：`background_jungle_01_set.prefab` 有 110 个 GameObject / 102 个 MeshRenderer
（天空、沙滩层、远近两层丛林、海浪与浪花），导出后只剩根上的一个 128×128 天空渐变 quad。

所以“贴图缺失”不是文件找不到——`audit_level_textures.py` 早就证明 46 张 PNG 与引用完全对得上，
运行期日志里也没有一条 `missing texture`。真实表现是**几何整体丢失**：画面里大片纯白，
只有一个 3.33×3.33（×关卡 scale 8 = 26.7 单位）的小矩形。

### 修法

1. **导出整棵节点树**。新增 `kind = "compound"`：`prefab_nodes()` 遍历 prefab 的
   GameObject / Transform / MeshRenderer / MonoBehaviour，按 `parent` 下标展平成数组
   （父恒排在子之前，因为 JsonUtility 不能把 JSON object 反序列化成字典），每个节点带
   `pos / rot / scale`，可绘制的节点再带 `atlas / uv / size / pivot / flipX / offset`。
   只用到 `kind = "sprite"` 的旧形态仍然保留（唯一可绘制节点就在根上、TRS 为单位时）。

2. **裸 MeshRenderer 也能画**。GoalArea 的旗子 `GoalSprite` 没有 `UnmanagedSprite` 脚本，
   用的是 `Assets/Mesh/Plane.asset`；导出时用网格 `m_LocalAABB` 的 centre/extent 当
   `offset` 与 `size`。注意 `m_Center: {x: .., y: .., z: ..}` 是**单行内联**写法，
   多行正则取不到（`vec3_inline()` 就是为此加的）。

3. **纯色节点也能画**。视差背景的 `Background_Near_fill` / `Background_Far_fill` 用
   `_Custom/Unlit_Monochrome`，只有 `_Color` 没有 `_MainTex`，原先被当成不可绘制丢掉——
   画面里就是背景中的大片空洞。现在导出 `color`，C# 侧 `LevelCatalog.GetColorMaterial()`
   造一个不设贴图的材质（unlit sprite shader 会把 `_MainTex` 乘上 `_Color`，
   贴图留白即得纯色）。

4. **导入侧按同样顺序建 GameObject**。`LevelImporter.BuildCompound()` 逐个节点
   `new GameObject` + `SetParent` + 设 `localPosition / localRotation / localScale`，
   TRS 交给 Unity 自己复合（不做 2D TRS 手推）；可绘制节点走 `BuildSpriteQuad()`。

### 结果

`prefabs.json` 的 kind 分布从 `sprite 321 / none 22 / terrain 21` 变成
`sprite 273 / compound 49 / none 21 / terrain 21`，贴图从 46 张增到 56 张
（补上了 `Background_Jungle_Sheet_01/02`、`Background_Maya_Sheet_01/02`、
`Background_Morning_Sheet_01/02`、`Background_Night_Sheet_01`、
`Background_Plateaus_Sheet_01`、`Halloween_Sky_Texture`、`Props_Goal_Area_01`）。

| 关卡 | renderers（修复前 → 后） |
| --- | --- |
| Level_05 | 28 → 132 |
| Level_34 | 51 → 147 |
| Level_Race_01 | 107 → 512 |

截图见 `Temp/DshBridge/after_Level_05.png`、`after_Level_34.png`、`after_Level_Race_01.png`。

### 已知未做

主工程靠 `ParallaxManager` 按 Tag（`ParallaxLayerSky` / `ParallaxLayerFar` / `ParallaxLayerNear` /
`ParallaxLayerForeground` …）让各层随相机位移不同倍数做视差。现在所有层的 Tag 没有导出，
所以背景是静态的——构图对了，但不会跟着相机移动。

---

## 三、启动自动恢复上次的场景与关卡

两个入口互相独立，所以分开记：

- 「关卡」按钮（`LevelMenu`）→ `LevelManager.Load()` → 记录关卡 `scene` 名；
- 「加载场景」对话框（`SceneLoader`）→ `SceneLoader.Load()` → 记录文件路径 + `SchematicsFormat`。

### 实现

新增 `Assets/Scripts/Assembly-CSharp/Bpsim/Levels/LevelSession.cs`（`internal static`），
用 `PlayerPrefs` 存三个键（工程里原本没有任何 `PlayerPrefs` 用法，但它是 Unity 内置、
跨平台且无需文件 IO 的最省事载体）：

```
Bpsim.Session.Level       关卡 scene 名
Bpsim.Session.ScenePath   上次加载的场景文件绝对路径
Bpsim.Session.SceneFormat SchematicsFormat 的枚举值（注意 Xml=3、EncryptedXml=4，
                          与对话框下拉的 0..3 顺序不同，所以存枚举值而不是下拉索引）
```

- `LevelManager.Load()` 成功后 `RecordLevel(entry.scene)`；`Unload()` 时 `ClearLevel()`。
- `SceneLoader.Load()` 成功后 `RecordScene(path, format)`；路径为空（空场景）时 `ClearScene()`。
  为了能从 QA 侧走同一条代码路径，`Load()` 的内容抽成了 `internal static LoadFromPath()`。
- `LevelManager.Start()` 里启动协程 `RestoreSession()`：等 `PartManager.IsReady`
  （`Boot()` 跑在 `AfterSceneLoad`，那时 ECS world 还没建），再调 `LevelSession.Restore()`。
- `Restore()` 先按记录重放关卡，再重放场景文件并 `SelectScene()`；场景文件已被删除时
  只警告并清掉记录。为了让「重放的关卡」和「用户自己的场景」都留下，
  关卡自己的 `Level_XX_contraption.txt` 照常加载——这正好复现用户关掉游戏时的场景列表。
- 用户手动关掉场景标签页时，`LevelManager.OnSceneUnloaded` 会比对场景名并清掉记录，
  不会下次又把它拉回来。

### 验证

关掉 Play Mode 再进（等价于重启应用），控制台只有：

```
[BpsimLevels] ready: 274 BPLE levels available
[BpsimLevels] '竞速 · 赛道 01' 已导入：地形 7 块，道具 94 个
[BpsimLevels] restore: level 'Level_Race_01' scene 'course_02_contraption'
```

`LevelQa.Session()` / `RestoreSession()` / `ForgetSession()` 可以随时查看、手动重放、清空记录。

---

## 复现步骤

```bash
# 1. 重新导出关卡数据（主工程 BPRC 根目录下的 levels/*_loader.prefab 是数据源）
python Tools/BPSim/export_levels.py --write

# 2. 编译（编辑器在运行时走桥接，否则用批处理）
#    POST http://127.0.0.1:8791/api {"method":"compile"}
#    或 Tools\unity-batch-bpsim.bat

# 3. 验证（Play Mode）
#    LevelQa.Load("Level_05") / Stats / ColliderBounds / RaycastTerrain / DropProbe / Capture
```

注意：**Play Mode 里调 `compile` 会一直卡在 `compiling:true`**，必须先退出 Play Mode 再编译。

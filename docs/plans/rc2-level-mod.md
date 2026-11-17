gate: passed | mode=改造后做 | oracle=指定 APK 序列化关卡/Location/背景指针与当前 Unity 原生加载错误及场景回读；不授权未观测动态帧等价 | criteria=原生 bundle 加载探针、源背景关联/层级对账、游戏内场景/背景/材质/碰撞回读、顺序换关、同一 WebView 内页模式往返、画笔/水域笔触运行时回读与统一关卡编辑器包门禁 | round=7 | status=done | date=2026-11-17

# RC2关卡mod（已并入关卡编辑器 Mod）

> 2026-11-16 起，本文件记录的工作并入「关卡编辑器」Mod：RC2 不再单独分发，`Mods/RC2Levels` 已删除，
> RC2 关卡数据落在 `Mods/LevelEditor/Rc2/`，编辑入口是关卡编辑器同一张页面里的同页模式。
> 归档条目保留原样以维持审计链；新增任务见第 13 条。

## 验收口径

- 不沿用 `_codex_work/rc2/build_level.py` 的单关简化路线，不使用 BPLE 的关卡贴图替换 RC2 素材。
- 从指定 APK 独立扫描全部关卡，以 APK hash 和每个关卡源资源定位作为清单 oracle。
- 导出原始层级、局部位置/四元数/缩放、网格全部顶点/UV/索引/子网格、原始材质参数与原始贴图。
- 网格不做 upper-envelope、抽稀、重三角化或相似素材替换。骨骼网格、特殊 Shader 和动画必须单独核验；不支持的效果明确报告，不冒充 APK 效果。
- 关卡可在现有编辑器内打开；支持对象/预制体/网格修改、保存、重新打开和 Unity 内载入。
- 原始关卡只读；编辑保存为可恢复的独立文档，不覆盖玩家数据。打包产物并入关卡编辑器 Mod，不再单独出包。

## 任务

1. 建立只读 APK 清单仪器，查清全关卡、渲染组件、Shader、依赖与碰撞数据。（依据：F5001）
2. 逐项验证原始网格/贴图/材质与完整世界变换的无损导出，不以旧脚本的近似结果作 oracle。（依据：F5002）
3. 通过闸门后再制作关卡包、通用关卡包编辑接口及 RC2 原始资源渲染器。
4. 进行全量数据对账、编辑往返、Unity 加载、代表关卡画面核验；如视觉 oracle 不可执行，单独说明未证边界。
5. 修复重复脚本归属并以源组件集合回归，核验 6 个瀑布层；保留原版空 SpriteRenderer，不用替代贴图填空。（依据：F5004）
6. 扩展对账覆盖背景、粒子、动画、原始材质参数和资源解码；区分独立源数量与 17 个文档重复嵌入数量。（依据：F5005）
7. 校验原图实际 GPU 上传方向，保存翻转负例并修复 ImageBitmap 路径；不以 PNG/hash 对账代替画面检查。（依据：F5008）
8. 增加骨骼数组/有限变换验证与 JSON 事务提交；保留所有源原生组件字节及只读视图。（依据：F5009、F5010）
9. 游戏载入回复必须确认原始场景构建状态；未通过 Unity 整体验收禁止生成最终发布 ZIP。（依据：F5006、F5012）
10. 修复用户游戏内 native bundle compatibility 错误与背景错误绑定；先复现原生报错、确定 bundle 兼容门禁，再按 APK 指针而非 Mission 名称绑定背景。（依据：F5013、F5014）
11. 修复 TMP 版本初始化、旧场景/视角继承和大型存档内部美化 JSON 的重开失败；运行定义平移载具锚点，不覆盖保存数据。（依据：F5015、F5017）
12. 将 RC2 编辑器接入关卡编辑器 Mod 的模式选择，在同一游戏内 WebView 往返；切换前拒绝未保存的 BPLE 文档，统一两种模式的工具栏/面板样式，RC2 关卡数据继续由独立关卡包 Mod 提供。（依据：F5006、F5007、F5020）
    → 2026-11-16 修订：F5020 中「RC2 保留为单独关卡包 Mod」被用户要求推翻。既改为同一个 HtmlPanelView 内的同页模式，RC2 关卡包整体并入关卡编辑器 Mod，原独立包删除。（依据：F_RC2_INLINE_20261116_01、F_RC2_INLINE_20261116_05）
13. 同页 RC2 模式内补齐「预制体」编辑：预制体清单、引用实例计数、网格/贴图覆盖、共享定义 JSON 编辑，全部在既有属性面板内以宿主样式呈现，不新增独立窗口、不改动既有控件。（依据：F_RC2_INLINE_20261116_04）
14. 合并后的存档兼容：`Rc2Pack` 先读新 owner 目录，再只读兜底旧 owner 目录 `ModData/683178f54b605fd78ab4f885409b48ca/Levels`，写入只落新目录，旧文件不删不改。（依据：F_RC2_INLINE_20261116_03）
15. 合并后重新建立原生门禁：17 关 sweep、Shader 租约探针、相机探针全部在合并包上复跑并通过；分发件改为 `Builds/Mods/BpsimLevelEditor-0.4.0.zip`。（依据：F_RC2_INLINE_20261116_02）

## 本轮完成与未完成边界

- 源数据：17 个 Mission 本体 9632 节点；加两份独立背景为 9689 源节点，17 个文档嵌入后为 10085 节点。20592 个原生组件、5052 条脚本、433 网格、129 PNG、22 材质均逐项源对账，两次独立采集报告相等。
- 几何：10085 个矩阵与 12657 个顶点样本对独立 SciPy/NumPy，9 个 JS 负例；实际 C# DLL 通过 17 文档/433 网格解析与 8 个控制。
- 编辑器：已验证实际网页打开/修改/保存/重开、独立/共享网格、顶点选择标定、撤销/重做与原始文件不变；宿主 HtmlPanelView 尚未验收。（依据：F5007）
- 原生进展：实际主 Unity/URP 游戏中 17 关顺序加载、源码节点/渲染器计数、原生 Shader、FINISH 字库、静态碰撞体与物理世界、相机平移缩放绑定均已通过。旧 AssetBundle 构建遗漏模块造成清单对象缺失，已原生 A/B 复现并重建；前一隔离反射工程不作为通过依据。
- 已输出 Windows 0.1.1 试玩修复包，并更新当前安装目录；玩家 ModData 文件哈希保持不变。内部重开改用紧凑 JSON，避免大型原始组件存档被美化展开后超过输入上限。
- 未完成：精确碰撞/原 APK 物理与脚本、任务通关与检查点/收集等原始逻辑、动画/全粒子模块、原版视差更新时相、瀑布时相/滚动机制、FluidObject 原算法、同瞬时态 APK 帧对照及 Android bundle。不得标成已完整还原。
- 2026-11-16 追加边界：同页 RC2 模式的验收只覆盖首关 `Mission_Sand_1` 的进入/退出/导入/保存/载入往返与 17 关的原生构建；不声称上游 Dioxus 编辑器的内部状态与 RC2 图层互不干扰，也不声称编辑后的文档在 APK 内逐帧等价。预制体编辑改的是 `scene.prefabs` 共享定义，影响所有引用实例（UI 已写明显式提示）。
- 隔离 Unity 的首次失败与 IL 证据已记录；不重复不安全的反射 World 创建，不退出/修改用户主 Unity。下一轮先建立正确 DOTS 编译隔离与 URP 初始化 oracle，而不是继续盲试。

## 已拉取的源码参考

- `_codex_work/rc2-level-mod/references/unity-prefab-editor`：54def108f467e72b0f7ddd774ba69756a771aff0，Apache-2.0，只读参考；不直接导入遗留 Editor 插件。
- `_codex_work/rc2-level-mod/references/UnityRuntimeInspector`：930b0f7e7c9caecec9b79cd43fe0f46c48fb737f，MIT，只读参考。
- 现有关卡编辑器和已锁定上游源码继续使用现有许可证/构建边界。

## 变更记录（只追加）

- 2026-11-07：用户明确旧版 BPLE 贴图替代不符合要求；以 APK 原始效果作为目标，彻底排除旧版近似转换路线。
- 2026-11-07：续轮独立源组件扫描发现 9,689 个节点的 scripts 归属均不正确（源脚本 5,052 条、导出 25,644 条），由 append 缩进错误及残留 record 引起；瀑布 84 层不是源数据。3 个沙漠云节点的 m_Sprite 指针为零，需显式保留源空状态。主 Unity 当前 Play Mode/编译忙，本轮只使用隔离验证工程，不中断用户场景。
- 2026-11-07：修复 GPU 原图上下翻转、损坏骨骼数组的漏验与无效 JSON 污染；新增完整源组件只读存档及真实载入状态回复。源/几何/C# 文档/网页编辑验收与 APK 效果/Unity 整体门禁分别记录；最终 ZIP 保持阻断。
- 2026-11-07：用户实际游玩报错；本轮 native Console 已确认更早错误为 AssetBundle Memory 与 newer Unity runtime 不兼容，并非漏装（安装文件与源码 bundle SHA256 相同，均带 2022.2.23f1c1 header）。重新开放 bundle/gameplay 与背景绑定门禁，优先解决真实游戏载入。
- 2026-11-07：修复 native AssetBundle 清单对象缺失、重复 bundle 生命周期、TMP 旧版升级清空图集、旧载具/镜头定位、背景源引用和缩放/重复/前后层次。实际主游戏原生 sweep 17/17 通过，恢复用户关卡，原编辑存档哈希未变。发布边界为 Windows 试玩修复版，不是 APK 全机制/全帧还原版。
- 2026-11-07：用户要求 RC2 编辑器成为关卡编辑器 Mod 的一个模式；预注册 F5020，采用同一 WebView 内模式往返、未保存 BPLE 文档阻止切换、RC2 保留为单独关卡包 Mod 的验收边界。
- 2026-11-07：实际安装目录 `BpsimRC2Levels_v012.dll` 与包内程序集 SHA256 一致；Unity Play Mode 原生 sweep 的 17 关记录全部通过，`first_failure` 与 `restore_error` 为空。试玩包门禁和独立 ZIP CRC/清单/资源哈希校验通过，产物为 `Builds/Mods/RC2关卡mod-0.1.2-试玩修复版.zip`，共 628 个条目；见 F5021、F5022。当前 Unity 仍处于 Play Mode，未重载场景；编辑器模式的浏览器往返/样式验收已通过，宿主 `HtmlPanelView` 内的同窗切换仍待实机复测，F5020 保持待证。
- 2026-11-16：用户要求彻底重做——RC2 编辑必须活在关卡编辑器同一页里（不再有独立 RC2 UI/模式），支持导入关卡，并要在不改动既有 UI 的前提下新增预制体编辑，同时删除旧的独立 RC2 mod 包。本轮据此推翻 F5020 后半句「RC2 保留为单独关卡包 Mod」。
- 2026-11-16：C# 侧合并完成。`Rc2Pack`（`Mods/LevelEditor/Code/Rc2Pack.cs`）取代 `Rc2EntryPoint`，成为关卡编辑器内部服务：复用宿主 `ModDataStore`、`ModLevels.RegisterCatalog/SubscribeLoaded`、日志；`LevelEditorEntryPoint` 删掉 `Rc2ModGuid`/`IsRc2Loaded()`/`CreateRc2EditorUrl()`/反射，新增 `/api/rc2/*` 路由与 `modes.rc2`/`rc2_error`。8 个 `Rc2*.cs` 改命名空间 `Bpsim.LevelEditor.Rc2` 后并入 `Mods/LevelEditor/Code/`（`BuildMod()` 只编译这一层，所以不能放子目录）；RC2 资源整棵搬到 `Mods/LevelEditor/Rc2/`（613 文件 / 111.1 MB，含 `Rc2/Assets/rc2-windows.bundle`）。版本升 0.4.0，程序集改 `BpsimLevelEditor_v040.dll`，四处硬编码旧名同步改。
- 2026-11-16：Web 侧同页模式落地。新增 `Web/bpsim/rc2-mode.js`（约 950 行）与 `rc2-mode.css`（约 200 行），把原独立页的左「对象列表」/中画布工具栏/右「属性」整套搬进 `#bpsim-rc2-layer`；颜色、圆角、边框全部取宿主 `--wb-*`/`--color-*` 变量，控件尺寸对齐 `#bpsim-doc-actions`。`editor-adapter.js` 只改 `#bpsim-rc2-mode` 一个 click 处理器：删掉 `location.assign`，改为 `window.bpsimRc2Mode.toggle()`；`index.html` 只新增两处引用（`rc2-mode.css`、三个 `<script>`），`editor.css` 与其余 613 行 adapter 逐字未动（对比已发布 0.3.1 ZIP 基线，见 F_RC2_INLINE_20261116_04）。
- 2026-11-16：修掉一个真实渲染缺陷——`.bpsim-rc2-loading` 的作者样式 `display:grid` 盖过 `[hidden]` 的 UA `display:none`，导致遮罩永久挡住画布；补 `.bpsim-rc2-loading[hidden]{display:none}` 后解决。
- 2026-11-16：两套验收仪器落地并通过。① `Tools/LevelEditor/verify_rc2_inline.cjs`（无头 Chrome + CDP，桩 `/api/rc2/*` 真读 `Rc2/catalog.json`）12 步全过，含像素级判据（`gl.readPixels` + distinct colors）与真实 `#bpsim-rc2-file` 导入；② Unity Play Mode 内 `Rc2PlayDiagnostics.Roundtrip()`→`Selftest()` 得 `enter.modeAfterEnter="rc2"`/`exit.modeAfterExit="bple"`/`bpleIntact=true`，17 关 `StartSweep()` `pass=true`，`ShaderLeaseProbe` 对称，`Pack()` 命中旧存档。
- 2026-11-16：删除旧独立包。`Mods/RC2Levels`（含 17 关原始资源副本与 4 个历史 DLL）、`Mods/RC2Levels.zip`、`Mods/RC2Levels.dshclaim`、`Builds/Mods/RC2关卡mod-0.1.1-试玩修复版.zip`/`-0.1.2-试玩修复版.zip` 全部移除；已安装槽位 `install-0be8291b370c420b8d0d375ff6d4aa0f` 删除；`Modding.xml` 的 `maintenance-lastMods` 只保留关卡编辑器 0.4.0 一条（备份 `_codex_work/rc2-inline/Modding.xml.before-040.bak`）。玩家存档 `ModData/683178f54b605fd78ab4f885409b48ca/Levels/5cab05e95ce05896b49f2a618a7e3835.rc2level` 未删未改（sha256 `55D7564A…`）。
- 2026-11-16：分发件 `Builds/Mods/BpsimLevelEditor-0.4.0.zip`（794 entries，117,402,771 B）重建并与工作区逐文件哈希一致；安装槽位 `install-00000000000000000000000000000042` 与工作区 794 文件全 SAME。旧工具链 `Tools/RC2Levels/{test_core.cjs,test_background.cjs,preview_server.cjs,verify_managed.py,validate_managed.cs}` 已改指向新路径并复跑通过（`test_core` 17 关/10085 节点/9 负例，`verify_managed` 17 文档/433 网格/9 负例）。
- 2026-11-16：修掉自检注入路径的真实缺陷——`Rc2InlineSelftest.Roundtrip()` 原来只注入 `enter().then(selftest)`，退出证据要再手工注入一次才会到，所以自动化轮询永远等不到 `has_exit`（`Specified cast is not valid` 修复后暴露出来的下一个坑）。改为注入 `window.bpsimRc2Mode.roundtrip()`，页面内一次跑完「进入→记录→退出→记录」。同时修 `_codex_work/rc2-inline/qa_run.py`：桥接有时把 Mod 返回的 JSON 字符串原样透传，加一层 `json.loads` 归一化。
- 2026-11-16：最终交付态复跑（安装槽位 `install-00000000000000000000000000000044`，DLL sha256 `25A4776D…`）：`Roundtrip()` → `{has_enter:true,has_exit:true,pass:true}`，`enter{modeAfterEnter:"rc2",docActionsAlive:true,renderer:295,triangles:34536,levels:17,nodes:739,bpleIntact:true}`、`exit{modeAfterExit:"bple",layerVisible:false,bpleDocumentIntact:true,dirty:false}`；17 关 sweep `{status:"finished",pass:true,failures:null,first_failure:null,restore_error:null}`，合计 `nodes:10085 renderers:4301 static_colliders:1936`，每关 `camera_probe.pass=true`、TMP 字体 `atlas:true`；`Pack()` 的 `saved`/`legacy` 同时命中旧存档键。五支 node 仪器（`test_core`/`test_background`/`test_level_binary`/`test_editor_adapter`/`test_water_canvas`）exit 0，`verify_rc2_inline.cjs` `{pass:true,steps:12}`。分发件重建为 794 entries / 117,404,341 B，解包逐文件与工作区全等（mismatch 0）且与槽位 44 逐 sha256 全等。见 F_RC2_INLINE_20261116_06。

## 2026-11-17 · 0.5.0：RC2 同页模式补上画笔与水域

- 新增 `Mods/LevelEditor/Web/bpsim/rc2-tools.js`（纯几何层，UMD 双导出）：`terrainMesh` 生成双 submesh 地形网格（本体 + 表面条带，复刻 APK `59cf7e76f371f35221b785ba` 的材质对形态），`polygonCollider` / `boxCollider` 复刻 APK 碰撞体字段，`createTerrain` / `createWater` / `refreshTerrain` 落地到 RC2 文档。
- `rc2-mode.js` 新增「选择 / 画笔 / 水域」三工具、参数条、属性面板画笔段与 `setTool`/`plant` 仪器入口；工具参数不进撤销栈，落笔进撤销栈。
- 浏览器验收 `Tools/LevelEditor/verify_rc2_inline.cjs` 扩到 15 步（新增 brush / water / tool-roundtrip），`Tools/LevelEditor/test_rc2_tools.cjs` 17 项全绿。
- **修掉一个真实渲染缺陷**：`Rc2SceneRuntime.Initialize` 原先 `if(key==null && node["fluid"]==null) continue;` 只看 prefab 的 catalog 网格键，把「几何只在 `scene.mesh_overrides` 里」的画笔地形整块跳过——画笔在网页里能画、进 Unity 完全不渲染。改为 `key==null && meshOverride==null` 才跳过。
- 新增 `Rc2PlayDiagnostics.ToolProbe(document)`：接受浏览器存档，重建运行时并逐节点回报 mesh / submesh / 顶点数；判据含真实笔触节点（`drawn_ok`）。实测 `pass:true`、`terrain{submeshes:2}`、`water{vertices:114}`、`drawn_ok:2/2`。
- 版本升 0.5.0（`Mod.xml`、`AssemblyInfo.cs`、`LevelEditorModBuild.cs:37`、三个 QA 脚本），`BuildPackage` → `Builds/Mods/BpsimLevelEditor-0.5.0.zip`（795 entries / 117,422,273 B，逐文件与工作区 mismatch 0），装槽位 `install-…57` 并 `compare_slot` 全等。
- 依据：F_RC2_TOOLS_20261117_01（画笔/水域端到端可用）、F_RC2_TOOLS_20261117_02（渲染跳过缺陷）、F_RC2_TOOLS_20261117_03（Newtonsoft 嵌套 JArray 展平）。
- 边界：画笔只有五种多边形近似，未复刻 APK 曲线/圆弧笔形与贴图档位；水域是正弦波动 + 触发盒，未复刻 `FluidObject` 入水算法；不覆盖 Android bundle。

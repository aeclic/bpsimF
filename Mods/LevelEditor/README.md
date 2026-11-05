# 可选关卡编辑器 Mod — 0.3.0

只有安装并启用此 Mod 后，选关面板才出现“关卡编辑器”按钮。
本目录是开发源码，不会被默认构建复制到 StreamingAssets，不会自动安装或启用。

## 安装与启用

1. 使用当前宿主源码重新编译 bpsim。旧版本没有本 Mod 需要的通用宿主接口。
2. 在游戏 Mod 管理器中安装 `BpsimLevelEditor-0.3.0.zip`，确认 Mod 已启用。已有旧版时安装新版并重启游戏，避免旧 DLL 留在 Mono 域中。
3. 打开“关卡”面板，点击“关卡编辑器”。编辑器窗口不再有独立标题栏，界面就是完整的内置画布页面；内置 WebView 不支持 WebGPU 时，画布区域中央会显示提示条。
4. 关卡名称、关卡选择与载入、保存 / 试玩 / 导入 / 导出 / 视差 / 返回游戏都在页面顶部的文档工具栏里（保存与试玩是图标按钮）。导出 `.bpslevel` 时用工具栏的导出图标，不要把原编辑器的裸 `.bytes` 当完整存档。

禁用/卸载会移除按钮、关闭窗口、停止本地服务、卸载当前由此 Mod 提供的关卡和水域，释放水力注册、相机、RenderTexture、材质及背景视差副本。
已保存的关卡不会因卸载而删除，位于 `persistentDataPath/ModData/e47b018a61a64e79a789c5f627a00aa4/Levels/`。
重新启用后，“我的关卡 · Mod”会重新显示它们。

## 水域与主关卡画布

- 原 badpiggies-editor 的地形、对象树和属性编辑。
- 保存、打开、导入/导出 `.bpslevel` 完整 JSON 关卡包，包含原始覆盖数据、资源映射、起始载具、水域和未知扩展字段。
- “水域工具”是原主画布上的工具层，入口是画布左上工具栏第 5 个水滴图标（与选择 / 框选 / 画笔 / 平移并列），不隐藏地形，不打开第二个页面。地形模式中水面效果也持续显示。
- 新建 / 画矩形、移动、四角缩放、删除、独立撤销/重做、颜色及波浪参数；中键 / Alt 平移、滚轮缩放都交给上游镜头。
- 同时观察主线程和 Worker 渲染后端的相机事件，不另维护一套镜头坐标。
- 波浪、接触涟漪、水线泡沫、折射扰动、纹理焦散、光柱和屏幕空间 2D 平面反射；参数修改实时显示在关卡画布。
- 可投放临时木 / 金属预览块解释密度与水流；它们是网页物理示意，**不保存、不导入为真实游戏部件**。
- 游戏内坐标使用 BPLE 单位，导入时统一乘以 8 并按 LevelStart 平移。
- **没有复制 InteractiveWaterSystem 源码或素材**，该仓库授权尚未明确。水体物理、网格、涟漪和着色器为独立实现。
- 水域历史与原编辑器的地形历史独立；地形用原编辑器撤销，水域用水域面板撤销。
- 当前资源映射基于所选内置关卡模板，不支持任意新 Unity prefab/纹理资源包。

## 游戏内物理与渲染

- 浮力使用旋转后的碰撞体投影与水域矩形裁剪，按浸没面积、密度和重力在浮心施加冲量，产生力矩。
- 已有碰撞体提供有效体积时用填充比例估计二维排水量；体积字段为 0 的重建碰撞体回退到投影包围盒。不是三维精确流体或空腔进水模拟。
- 线性阻力按相对水流速度指数衰减，旋转阻力同样指数衰减；可额外设置水流方向加速度。
- 系统运行在物理导出 / 巨构镜像之后。普通、巨构及脱落部件使用同一施力链路，巨构在下一步统一折算冲量，不创建 Rigidbody2D 代理。
- 重叠的物理水域不重复叠加浮力 / 阻力；按列表第一片命中的物理水域计算。水域底边是力场边界，不自动生成地面碰撞。
- 接触事件限量且按水面空间分桶限流；弹簧涟漪使用 CFL 约束子步，避免事件队列积压或数值爆炸。
- 所有水域共用一次半分辨率场景捕获，排除 UI 和水自身；已导入地形填充网格提供遮罩。水域不在镜头中时不捕获。
- 反射是关于 XY 水线的**屏幕空间**镜像：不能反射镜头外的对象，也不是完整 3D 反射相机。焦散、光柱和泡沫是纹理/程序化近似，不是光线追踪。
- 背景利用项目已有的原版层级 / 纹理，按 Sky 0.8、Further 0.7、Far 0.6、Near 0.4、Foreground -0.4 的水平跟随比例移动，前景保留受限纵向跟随，重复条带水平平铺。
- Shader 仅随此 Mod 的 Windows AssetBundle 载入；不修改 URP Renderer 资产，不增加常驻 RendererFeature。Shader 不可用时保留色层波浪 / 涟漪与物理，并显示日志。
- 只有启用 Mod 且载入带水关卡时才创建 ECS 水力系统和捕获资源。不开启 Mod 就没有编辑入口、服务、水体或水力步进。

旧 0.1.0 关卡自动补充参数，但保留 `physics_enabled=false`；新建水域默认开启水力。完整字段与单位见 `WATER_SCHEMA.md`。

## 水面外观

浏览器关卡画布和 Unity 实战都读取 `Web/bpsim/ultimate-water/style.json` 中从 Ultimate Water 2D Demo Scene 提取的材质参数，并使用同一组水面坐标、颜色、法线流动、折射、反射、泡沫和焦散公式。浏览器以 WebGL 绘制覆盖层，Unity 使用 URP Shader；两端都从当前关卡画面和地形填充网格生成场景纹理与岸线遮罩。

| 层次 | 资产设置 |
|---|---|
| 水色 | `_WaterColor` `(0, 0.6634, 0.9922)`、`_BaseFarColor` `(0, 0.8068, 1)` |
| 波纹法线 | 原始 DXT5nm `WaterNormals_01`，按 `.ag` 读取，平铺 `(6.9, 32.27)`、贴图 Y 缩放 `-0.98`、强度 `0.251`、流速 `(0.04, 0)` |
| 水深与折射 | 资产渐变距离 `1.18`；用实时关卡画面做法线折射和水线镜像反射 |
| 反射 | 资产材质反射强度 `0.663`，按水深衰减 |
| 泡沫与焦散 | 原始 `Foam_01`、`Foam_02`、`Foam_03` 纹理滚动采样，岸线和接触涟漪增加泡沫 |

原始 PNG 保存在 `Textures/UltimateWater/`，浏览器副本在 `Web/bpsim/ultimate-water/`。Unity AssetBundle 构建递归打包这些图，导入时关闭 sRGB、保留 alpha、关闭压缩并设置 Repeat；法线贴图的 DXT5nm 通道不再烘焙或改写。颜色、透明度及水域交互参数仍可按关卡调整。

这些纹理来自 DiffusionWorks 的 Ultimate Water 2D 商业资源，版权归其作者，不属于本仓库的 AGPL 授权范围；公开再分发前需单独确认权利。本项目使用原始贴图和提取出的材质数值，运行时 Shader 为适配 BPSIM 场景捕获而编写。

## 网页与安全边界

上游发布版锁定到提交 `8697da72181ea1c7770c0b356c93d58678911e0a`。
伴随适配器观察 `bpEditorCanvas.render(ScenePayload)` 及主线程 / Worker 相机事件，并使用原编辑器的文件拖放导入链路；
未修改上游 WASM。切换到未绑定的其他编辑器标签时，拒绝保存，避免覆盖错误关卡。
导入协议、序列化结构变化后，应先更新适配器测试，再更新上游版本。

本地服务仅监听随机 `127.0.0.1` 端口，写请求要求会话令牌、同源和独立请求 ID；
不提供任意路径读写、DLL执行或编辑器 HTTP 桥转发。禁用 Mod 后服务失效。
服务由宿主提供，未放宽 Mod 程序集扫描规则。

## 重建

在项目 PowerShell 中：

```powershell
python Tools/LevelEditor/sync_editor.py
node Tools/LevelEditor/test_level_binary.cjs
node Tools/LevelEditor/test_water_canvas.cjs
node Tools/LevelEditor/test_editor_adapter.cjs
```

然后在持有 Unity MCP 租约时调用：

- `Bpsim.EditorTools.LevelEditorModBuild.Refresh()`
- `Bpsim.EditorTools.LevelEditorModBuild.BuildPackage()`
- `Bpsim.EditorTools.LevelEditorWaterQA.RunMath()`
- `Bpsim.EditorTools.LevelEditorModQA.Start()`（自动测试）或 `StartPreview()`（测试后暂留 UI）；完成预览用 `FinishPreview()`。
- `StartCanvasPreview()` 仅运行文档 / 生命周期检查后暂留主画布，用于单独验收前端；它不代替完整水力测试。

产物仅写入 `Builds/Mods/`；不会触碰玩家安装目录或内置 Mods。
DLL 用当前 Unity 的 Mono 编译器和实际宿主引用程序集预编译，玩家端不需要编译器。
打包会从 `Shaders/` 与 `Textures/` 暂存到 `Assets/Editor/LevelEditorModAssets/` 并构建 Windows Shader + 纹理 bundle；这些是编辑器构建素材，不会默认安装 Mod。
QA 暂时禁用已安装旧版、使用新包进行测试，并在退出 Play Mode 时按原始字节恢复 Mod 配置及关卡会话；不替换玩家已安装 DLL，不删除玩家关卡。
本工程编辑器的 player DOTS 兼容路径使用显式同步按块施力；正常 Burst 运行环境使用并行块任务。两者执行同一套水力公式。

上游代码为 AGPL-3.0-or-later；许可证及对应源码在 `ThirdParty/`。
Rovio 游戏资源不属于 AGPL 授权，公开再分发前需单独确认其权利。
Vuplex 由宿主提供，本 Mod 包不包含该商业插件。

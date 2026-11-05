# 阵营识别器与彩色描边

## 使用

- 在建造界面双击「分离器」，选择索引 **3：三星分离器（阵营）**，装到载具上。
- 在部件属性的「朝向序号」选择阵营：**0 红 / 1 蓝 / 2 绿 / 3 黄**。序号 **4–7** 分别对应同样四队，但保留现有的斜放同队穿透规则。属性标题显示当前阵营。
- 标记赋予整辆**实际连接的载具**阵营；同车多个标记按位合并，属于多个队伍，描边使用这些队伍颜色的平均色。没有标记的车不描边，不会把所有未标记车辆当成同队。
- 建造描边默认开启；「设置 → 建造界面阵营描边」可关闭。
- **运行时描边默认关闭**；「设置 → 运行时阵营描边（默认关闭）」可打开。
- 「阵营描边宽度（1–8 像素）」默认 2.5，随镜头缩放保持屏幕像素宽度。设置保存、重载、恢复默认都接入原设置系统，旧设置文件无需迁移。

## 与 BPRC / 原有移植的关系

本项目已具备 `MarkerTeams`、`MarkerCollisionSystem`、三星分离器非触发标记、制导避开同队等运行时机制；本次补齐建造期识别和描边，并共享标记识别/队伍掩码规则，不重复加入第二套玩法管理器。

建造预览只读计算连通块，与 `PartSimulator.BuildJointsAndComponentsJob` 的容器连接、机械方向、电气端子和高层对公共层焊接保持一致。旋转、移动、换层、添加、删除标记后刷新。模拟期直接读取现有 `PartMarkerTeam`，跟随分离和损毁后的实时连通块。

## 渲染选择

- **按用户要求实际使用 Easy Performant Outline 2D 3D v3.7 资产**，不再使用先前的自定义 alpha 外扩描边 shader。资产位于 `Assets/Plugins/Easy performant outline`。
- 当前 URP 2D Renderer 已安装资产原生 `URPOutlineFeature`。相机 `Outliner` 使用原生 Outline / Dilate / Gaussian Blur / FinalBlit 流程，Native 分辨率、高质量扩张和柔化边缘。
- ECS 部件按阵营、图集和移动端索引上限合并为轮廓目标网格，交给资产 `Outlinable` 的 alpha cutout；不是每个部件一个 GameObject。`EpoFactionTarget.shader` 只提供不可见的目标 Renderer，**本身没有描边算法**。处理自由缩放、镜像和生成框形状，采样做半 texel 内缩以隔离图集邻接精灵。
- 直接读取其他 agent 新增的 `PartAtlasIndex` / `PartAtlasRegistry`，沿用正常渲染的材质选择，分别给 EPO 提供各图集纹理。**不修改其他 agent 占用的 SceneRenderSystem、PartEntityFactory 或 Rendering 目录**。
- 描边关闭时隐藏全部目标、移除本功能的相机 Outliner，并恢复其改变的 `forceIntoRenderTexture` 标志。不遍历精灵、不计算预览、不执行 EPO 后处理。
- 只导入核心、URP、Inspector 和文档，排除 Demo、HDRP、DOTween 和资产包自带的 `Packages/manifest.json`。适配 4 个 asmdef 的引用，因为项目的 URP 来源是恢复的 DLL；只改一个 Editor 创建管线的辅助方法以兼容缺失的 editor-only 工厂函数。原生描边运行时代码和效果 shader 保持资产原样。
- `Tools/ImportEpoAsset.py` 可重放导入，`Tools/EpoAssetImport.json` 记录源包 SHA-256、导入清单和适配点。没有替换项目管线包或导入演示场景。

## 验证

独立编辑器测试入口：`Bpsim.EditorTools.FactionFeatureQA.Run()`，或菜单 `Bpsim/QA/Faction feature (isolated)`。使用单独 ECS World 和离屏 RenderTexture，不加载/保存场景，不切换 Play Mode。

46 项检查覆盖设置默认值、旧 JSON、保存/Update/reset、宽度校验、8 朝向、无标记/多标记规则、机械/容器/跨层/电气连接、旋转与删除标记，以及使用 URP 原生 SingleCameraRequest 的真实 EPO 四队描边、透明内部、图集隔离和资产 Outline/Dilate/Blur/FinalBlit shader 编译。没有伪造 EPO 的目标可见性字段。

- 测试报告：`perf/faction-feature-qa.json`
- EPO GPU 测试图（合成圆形精灵，非游戏截图）：`perf/faction-outline-epo-gpu-probe.png`

完整游戏里的高部件数性能、Havok 碰撞/制导场景以及 Android 真机仍应在相应环境做验收；离屏测试不替代完整关卡验证。

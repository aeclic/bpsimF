# bp-rebooted ECS 框形 / BepInEx

来源：<https://github.com/Goggs77/bp-rebooted>。
本 Mod 将其中的长方形框、直角三角框部件改写为 ECS 数据与系统；兼容原有的半圆框，并额外提供完整圆框。

## 使用

Windows x64 构建自动安装 BepInEx 5.4.23.2 和 `BepInEx/plugins/Bpsim.FrameShapes/Bpsim.FrameShapes.dll`。
启动后在 F10 的 Mod 列表中显示「bp-rebooted ECS 框形」，使用独立的 `bput.bpsim.bpreboot-frame-shapes` ID，可与 BPIR 的同类 Mod 并列显示；首次默认开启，之后遵循玩家保存的启停选择。
选择木框、铁框、彩色框、透明框等框架部件，在部件属性中点「形状」循环切换长方形框、直角三角框、半圆框和圆框。
木箱、铁箱不参与。旋转、翻转、自由缩放沿用原部件操作。模拟中禁止修改形状。
停用 Mod 会停止当前模拟并恢复方框外观及碰撞；保留存档中的形状参数，重新启用后恢复。

## 构造

- `FrameShapesPlugin.cs` 是真正的 BepInEx 插件入口，通过场景就绪事件注册一次生命周期。
- BepInEx 使用 `Camera` 初始化入口，避开 `Application` 入口过早创建的插件对象被启动场景清理的问题。部署时仅迁移默认入口，保留其它自定义配置。
- `BpsimModHost` 是不引用 BepInEx 的宿主 API；`ModManager` 管理同一个插件实例的启停、状态持久化和 ECS 注册表。
- `FrameShape` 为 ECS 数据，`FrameShapeDirty` 为可启用组件。只有创建、参数修改、Mod 启停时进入更新查询。
- `FrameShapeSystem` 按几何、碰撞过滤和物理材质共享凸包。几何保留 BPIR 的实心凸碰撞语义，包括半圆底边；框内件沿用原本的容器和连接规则。
- 外观由现有 BRG/DOTS instancing shader 生成；沿用 `_BlendFactor.xy` 的颜色/纹理混合，`.z` 表示形状，`.w` 表示玻璃填充。不增加材质实例、每框脚本或额外 draw call。
- 非等比缩放在模拟创建刚体和计算惯量前重建对应凸包。缓存由 ECS 系统销毁时释放，模拟专属缩放碰撞体由原有模拟器释放。
- `Schematics.Unit.FrameShape` 在 JSON、CSV、XML、加密 XML、复制粘贴和模拟实体克隆中保留；旧存档缺省为方框。新增 XML 属性为 `shape`，CSV 第 13 列为形状。

## 构建和验证

Unity 的 Windows 构建通过 `FrameShapesBuildPostprocessor` 自动调用 `deploy.ps1`。BepInEx 运行时在 `Runtime/` 缓存，缺少时从官方 GitHub release 下载。
手动部署到已包含本次 ECS 宿主改动的构建：

```powershell
& .\Mods\FrameShapes\deploy.ps1 -GameDir 'D:\path\to\bpsim'
```

无需把 BepInEx 引用放进 Unity Assets，也无需 `BEPINEX` 编译符号或 Harmony 补丁。
Unity 编辑模式下调用 `Bpsim.EditorTools.FrameShapesAudit.Run()`，报告写入 `Builds/FrameShapesQA/core-tests.txt`。
报告覆盖四种格式往返、形状保留、凸包缺角射线、非等比缩放、关闭恢复、重启复用缓存和 shader 编译诊断。

独立播放器验收（自动恢复原 Mod 设置文件，结束后退出）：

```powershell
& .\Mods\FrameShapes\Tests\run-smoke.ps1 -GameDir 'D:\path\to\bpsim'
```

报告写入游戏 `output/frame-shapes-smoke.txt`，验证 BepInEx 加载、默认启用、ECS 注册、启停持久化、模拟中停用，以及属性按钮。`output/frame-shapes*.png` 为离屏渲染的实机截图。
验收会临时安装 `BepInEx/plugins/FrameShapesQA`；验收结束后移除此目录即可，正式插件在 `Bpsim.FrameShapes` 中。

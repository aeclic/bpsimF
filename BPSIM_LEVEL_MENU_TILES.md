# Bpsim 关卡菜单：正方形数字选关格子（BPLE 风格）

需求（用户）：把关卡面板（点击「关卡」按钮展开）中的关卡选择按钮改成正方形选关页面，
中央加粗数字显示关卡号，样式参考 BPLE 的选关截图，但**不带下方的星**。
后续追加（m00254/m00255）：按钮用**普通单色方形**（不要渐变），颜色对上 BPLE；
按钮尺寸略微缩小。

## 实现

全部在 `Assets/Scripts/Assembly-CSharp/Bpsim/Levels/LevelMenu.cs`（运行时代码构建 UI，
无新资产）。关键点：

- `CreateTile()`：每个关卡一格 = 深色描边方块（Outline，比面大 `TileOutline*2`）+
  扁平单色面（Face，带 Button，点击载入关卡）+ 居中加粗白色数字（Number，
  独立 GameObject——一个 GameObject 只能挂一个 Graphic）。
- **配色为 BPLE 实测值**（从 BPLE 选关截图原分辨率 2562x1603 逐块取样）：
  - 面色 `TileFaceColor = #70C030`（112,192,48）——BPLE 格子的主体绿，
    参考图里所有解锁格子都是这个纯色（带很轻的立体渐变，用户要求去掉）。
  - 描边 `TileOutlineColor = #3A542C`（58,84,44）——参考图格子边缘的深绿描边，
    每边约 4px/2562px 宽，代码里 `TileOutline = 4f`。
- **尺寸**：`TileSize = 108`（原 120），`TileStepX/Y = 128`（原 140），8 列，
  数字字号 58（BestFit 22..58）。
- 旧的 `TileTop/TileBottom/s_tileGradient/TileGradient()`（1x32 渐变贴图）已整体删除，
  Face 直接 `Image.color = TileFaceColor`，无贴图。
- 数字每关 = 导入顺序 +1（与 BPLE 内的关卡序号一致）；完整关卡标题保留在
  `TileTooltip.Title`（对象名仍是 `Level_<scene>`），不显示在格子上。

## 验证

- 编译：bridge `compile_status` 无错误（`Library/ScriptAssemblies/Assembly-CSharp.dll`
  在改动后重生成）。
- 运行验证（Play Mode，`UnityEngine.ScreenCapture.CaptureScreenshot` 截真实 Game View）：
  面板打开后第 1 章显示 36 个格子（274 关分章），取样渲染像素：
  面色精确等于 (112,192,48)，描边 (58,84,44)，白色加粗数字居中，间距均匀。
- 注意：bridge 自带的 `screenshot` 命令是临时相机渲染（不含 Screen Space Overlay UI），
  截 UI 必须用 `invoke_static UnityEngine.ScreenCapture.CaptureScreenshot` 后从
  `Temp/_gv.png` 取（路径相对项目根）。

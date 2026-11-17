# 开场品牌页（BrandSplash）集成与修复记录

## 2026-10-28：改用 Unity 原生静态启动画面

按 Personal 许可证的限制保留原生 SplashScreen 和 Made with Unity。
用户确认改用静态图标后，使用 `Brand/png/bpsim-wordmark-static-1600.png`
作为源图，复制到 `Assets/Texture2D/Bpsim_Splash_Logo.png` 并以 Sprite 导入。
`PlayerSettings.SplashScreen` 注册此 Logo（2 秒，原生 API 的最短时长），
`UnityLogoBelow` 模式让它与 Made with Unity 同屏显示。

构建列表直接从 `MainScene` 开始，移除 `BrandSplash`，不再在原生启动画面后
额外播放独立动画。`MainScene` 和备用 `Game` 场景中 `CoreManager.m_prefabList`
移除 `StartPage` 引用，从源头取消原透明图标的黑白渐变及其 4.5 秒等待。
旧动画资源保留用于编辑器预览，不再进入发布版启动流程。

`Bpsim/Brand/Configure Native Splash` 可重新应用上述启动配置；批处理入口为
`BpsimBrandSplashBuilder.BuildBatch`。此入口不再创建场景或关闭原生 SplashScreen。

验证：Unity 2022.2.23f1c1 批处理编译与配置入口退出码为 0；Windows 验证版
输出到 `Builds/NativeSplash/bpsim.exe`，构建结果为 `Succeeded`（214.6 MB）。
构建报告仍计入 1 error / 1 warning，编辑器日志有项目原有的
`OnParticleUpdateJobScheduled` 回调签名诊断，未阻止构建。
读取产物确认原生启动画面和 Unity Logo 均启用、自定义 Sprite 引用有效、
只有一项 2 秒 Logo，构建场景只有 MainScene，启动场景无 StartPage 或 BrandSplash。
窗口验证工具报 native pipe unavailable，本轮未作启动截图或视觉实测。

以下为旧版独立动画的历史记录，已由上述配置替代。

日期：2026-10-26。承接上一轮 dsh 会话（session-c174021e…）未完成的收尾工作。

## 1. 当前报错（已修）

`Assets/Editor/BpsimBrandSplashBuilder.cs:83-84` 引用的是控制器里**已经不存在的字段**：

```
Assets\Editor\BpsimBrandSplashBuilder.cs(83,14): error CS1061: 'BrandSplashController' does not contain a definition for 'MinDuration' ...
Assets\Editor\BpsimBrandSplashBuilder.cs(84,14): error CS1061: 'BrandSplashController' does not contain a definition for 'HardTimeout' ...
```

`Bpsim.UI.BrandSplashController` 的公开参数只有 `NextScene` / `BodyDuration` / `FadeOut`，
构建器改成了：

```csharp
controller.NextScene = "MainScene";
controller.BodyDuration = 3.4f;
controller.FadeOut = 0.45f;
```

修完 `Assembly-CSharp-Editor.dll` 才会重新编译（之前一直停在 20:30 的旧 DLL，所以
`ImportTextures()` 的新代码根本没跑过）。

## 2. 本轮发现的另外两个真 bug

### 2.1 i 点的双重偏移

`BrandSplashController.Update()` 里原本是：

```csharp
Vector2 dotLocal = UnitToLocal(482.04f, 33.55f);
m_dot.rectTransform.anchoredPosition = dotLocal + new Vector2(0f, hop);
```

但 `dot.png` 是**整张词标画布**（1858x592），点已经画在 i 上方的正确位置，
再叠一个 SVG 坐标偏移等于把点顶到画布外（实测偏 +17px / +103px）。
现在只叠加上跳偏移：`m_dot.rectTransform.anchoredPosition = new Vector2(0f, hop);`

### 2.2 水体渐变没有对齐设计

`sea.png` 是一条 `#7dd3fc → #3b82f6 → #6366f1` 的竖向渐变，但代码把水体高度写成
`m_wordmarkHeight * 1.9f` 并对齐到字母顶部 —— 字母只吃到渐变最上面 ~29%，
于是整体发白、发青。

设计里的真实几何（`Brand/svg/bpsim-wordmark-animated.svg`，渐变 `userSpaceOnUse y1=-46 y2=270`）：

| 量 | SVG 单位 | 说明 |
| --- | --- | --- |
| 水面 | -46 | 蓄满时的水面 |
| 渐变末端 | 270 | 之后是纯 `#6366f1` 的补边 |
| 水体底部 | 360 | 水流 path 的 `L1500 360 L-500 360 Z` |
| 字母画布 | -8..288 | `letters.png` 覆盖的范围 |
| 起始位移 | +310 | `rise` 动画的 `translateY` |

所以 `sea.png` 重烘成 16x812：前 631 行是渐变，后面补 `#6366f1`；
控制器按单元换算高度（406 单元）与起始位置（下移 310 单元），
`m_seaTop = UnitToLocal(0f, SeaSurfaceY).y`。

### 2.3 水流带（c1..c4）

原来的 `wave.png`（3 条粗带、被拉伸到 1.9 倍水体）在画面上是糊成一片的重条纹。
按 SVG 的四条 stroke 重烘了 `Assets/Resources/BrandSplash/currents.png`（2048x592）：

| 带 | y | 波长 | 振幅 | 颜色 | 描边 | 不透明度 |
| --- | --- | --- | --- | --- | --- | --- |
| c1 | 34 | 150 | 12 | #e0f2fe | 11 | .34 |
| c2 | 96 | 122 | 10 | #ffffff | 7 | .22 |
| c3 | 164 | 165 | 11 | #a5f3fc | 13 | .30 |
| c4 | 232 | 131 | 8 | #e0f2fe | 8 | .20 |

纹理覆盖 SVG x -8..1016（1024 单元），字母只占前 929.25 单元，多出的 95 单元
就是横向漂移的余量 —— `uvRect` 永远取样不到边界，不会出现拉伸条纹。
两层 `CurrentsA/CurrentsB` 反向漂移（第二层 alpha 0.6）加密度。

## 3. 配色与背景

- 背景从 Unity 引擎 Splash 的 `#231F20` 改成品牌墨色 `#0a0d14`
  （`Brand/README.md` 的 ink 值），构建器同时写 `PlayerSettings.SplashScreen.backgroundColor`。
- `dot.png` 重上色为 `#fcd34d`：SVG 的 amber 渐变是 `userSpaceOnUse x1=774..913`，
  i 点（x≈490）在渐变起点之前，按 pad 规则应该取起点色 —— 原来烘出来的橙色是错的。

## 4. 验证工具（batchmode，不需要进 Play Mode）

`Assets/Editor/BpsimBrandSplashPreview.cs`：

```
Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BpsimBrandSplashPreview.CaptureEntry
```

- 反射调用 `BrandSplashController.BuildUi()`（`Awake` 在编辑器里不会跑），
  再把 `m_started` 置 true 让 `Update()` 真的推进动画；
- Canvas 切到 `ScreenSpaceCamera` + 正交相机渲染到 1920x1080 `RenderTexture`，
  在 t = 0.55 / 1.25 / 2.05 / 3.05 各存一张 `Tools/_final/rt_splash_*.png`；
- 同时打印 9 张 Sprite 的加载状态（`Resources.Load<Sprite>` 是否为 NULL）。

**必须带图形设备**：`-nographics` 下 `Camera.Render()` 什么也不画，RenderTexture
读出来是 205,205,205 的垃圾值。

与主图 `Brand/png/bpsim-still-settled-1600.png` 同一 p 竖笔列的取色对比：

| 高度 | 主图 | 实机渲染 |
| --- | --- | --- |
| 5% | (61,132,246) | (62,134,246) |
| 25% | (67,124,245) | (108,174,247) ← 条带位置差 |
| 50% | (104,154,245) | (78,116,243) |
| 75% | (89,108,242) | (90,108,242) |
| 95% | (99,102,241) | (99,102,241) |
| 均值 | (80.4,127.0,234.7) | (80.1,129.5,244.8) |

两端完全一致，中段差异来自水流带的瞬时相位（动画中本来就在移动）。

## 5. 环境坑

**DSH 的 run_code 里 `process.env` 是空的**，直接 spawn `Unity.exe` 会静默挂死
（不写日志、不报错、CPU 0）。必须自己拼一个完整环境再启动：

```js
const env = { SystemRoot:'C:\\Windows', windir:'C:\\Windows', SystemDrive:'C:',
  ComSpec:'C:\\Windows\\System32\\cmd.exe', PATHEXT:'.COM;.EXE;.BAT;.CMD',
  PATH:'C:\\Windows\\system32;C:\\Windows;...', TEMP:..., TMP:..., USERPROFILE:'C:\\Users\\aecli',
  HOMEDRIVE:'C:', HOMEPATH:'\\Users\\aecli', APPDATA:..., LOCALAPPDATA:...,
  ProgramData:'C:\\ProgramData', 'ProgramFiles':'C:\\Program Files', NUMBER_OF_PROCESSORS:'8', ... };
```

另外：
- `Tools\\unity-batch-bpsim.bat` 只清代理变量、不设这些环境变量，所以从 DSH 里调用同样会挂。
- 从 run_code spawn 出去的 GUI 编辑器会在该次调用结束时被一起杀掉（日志停在中途、
  进程消失、事件日志无崩溃记录），所以远端可视化验证走 batchmode + RenderTexture。
- `Temp/UnityLockfile` 残留时要先删，否则新实例起不来。

## 6. 图片识别（HUBcore）

`read_image` 一路报 `model "deepseek-v4.1-flash-1017" does not declare image input`。
根因在 `C:\Users\aecli\.dsh\profiles\desktop\cordis.patch.yml`：
`starapi` 的 `deepseek-v4.1-flash` 有 `input: [text, image]`，
而 `starapi-claude`（displayName: HUBcore）下的 7 个模型**都没有 `input:` 块**，
运行时因此按纯文本处理。已给这 7 个模型补上 `input: [text, image]`（备份
`cordis.patch.yml.bak-images-1793021977970`），保存后立即生效，无需重启。

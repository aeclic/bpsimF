# BPSIM UPX 压缩壳实测（2026-10-26）

对象：D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim_2023.0.2_windows_x64（仓库里唯一一份 Windows Player 产物，197.2 MB / 206 779 168 B）。工具：UPX 4.2.4 win64，--lzma -9。

## 一、结论（先看这个）

1. 能压，但只有约 14%：197.2 MB → 169.6 MB（省 28.9 MB）。天花板就在这里，因为 81% 的体积是 bpsim_Data 里的 Unity 序列化资源，PE 文件只是零头。
2. 如果目的是"发出去更小"，UPX 基本白干：整目录 LZMA2 压完是 44.7 MB；而 UnityPlayer.dll 单独 LZMA 是 9.1 MB、UPX 之后是 9.2 MB —— 两者干的是同一件事，打完壳再打包并不会更小。
3. 有一个必踩的坑：mono-2.0-bdwgc.dll 一旦 UPX，游戏启动到 "Mono config path" 就 ACCESS_VIOLATION 退出（0xC0000005，模块 +0x145B70）。这个 DLL 必须排除，其余照压。

## 二、逐文件实测

| 文件 | 原始 | UPX 后 | 压缩率 | 结论 |
|---|---:|---:|---:|---|
| UnityPlayer.dll | 30 359 576 | 9 629 208 | 68.3% | 安全，收益最大 |
| Plugins/x86_64/lib_burst_generated.dll | 5 145 600 | 1 270 784 | 75.3% | 安全 |
| Plugins/x86_64/HavokNative.dll | 5 249 536 | 1 735 680 | 66.9% | 安全 |
| UnityCrashHandler64.exe | 1 115 160 | 358 424 | 67.9% | 安全 |
| bpsim.exe | 666 624 | 612 352 | 8.1% | 安全（本身只是启动器） |
| Plugins/x86_64/Havok.Vdb.dll | 5 785 088 | — | — | UPX 拒绝：混合模式 .NET |
| bpsim_Data/Managed/*.dll（共 35.4 MB） | — | — | — | UPX 拒绝：.NET |
| MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll | 7 760 920 | (2 500 120) | 67.8% | 不许压，会崩 |
| MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll | 610 840 | (221 720) | 63.7% | 一并排除（同一运行时） |

合计（只压安全项）：native PE 46.1 MB → 18.5 MB（省 27.6 MB / 59.9%）；整个目录 197.2 MB → 169.6 MB。

## 三、两类压不得的东西

Managed 程序集（bpsim_Data/Managed/*.dll，35.4 MB）——UPX 直接拒绝：

    upx: ...\Assembly-CSharp.dll: CantPackException: .NET files are not yet supported

Mono 运行时——能压成功，但装不上，压完全包启动日志只写两行就没了：

    Mono path[0] = '.../bpsim_Data/Managed'
    Mono config path = '.../MonoBleedingEdge/etc'

进程退出码 0xC0000005（bash 里看到 2816 = 0xB00），Windows 应用日志给出确切位置：

    错误模块名称: mono-2.0-bdwgc.dll
    异常代码: 0xc0000005
    错误偏移量: 0x0000000000145b70

把这两个 mono DLL 换回原版、其余保持 UPX，同一份构建立刻正常启动（见第四节）。

Havok.Vdb.dll 是 C++/CLI 混合模式镜像：CLI 元数据根（BSJB）在 5.5 MB 文件的 4.43 MB 处，埋得很深，所以 Tools/pack-upx.ps1 的 .NET 探测是全文件流式扫描，而不是只看前 4 KB。

## 四、验证（确实还能跑）

用 -Verify 启动 40 秒，日志走到：

    Direct3D: Version 11.0 [level 11.1], Renderer: NVIDIA GeForce GTX 1050 Ti
    Begin MonoManager ReloadAssembly
    - Loaded All Assemblies, in 0.262 seconds
    <RI> Initialized touch support.

原版对照是 Loaded All Assemblies, in 0.205 seconds —— UPX 只多花约 0.06 s（进内存时自解压一次），运行时没有额外开销。

## 五、怎么用

    # 压缩（自动识别、自动跳过 mono 与 .NET、可选启动自检）
    Tools\pack-upx.ps1 -PlayerDir 'D:\build\bpsim_win64' -Verify

    # 还原成未压缩
    Tools\pack-upx.ps1 -PlayerDir 'D:\build\bpsim_win64' -Restore

脚本本地没有 upx 时会自动下载 4.2.4 到 Tools/upx/。跳过规则：文件名在 $NeverPack（两个 mono DLL）、路径命中 *_Data\Managed*、文件内容出现 BSJB、文件已带 UPX! 标记（幂等，可重复跑）。

## 六、真想变小，枪口应该对准数据

| 文件 | 原始 | LZMA2 后 |
|---|---:|---:|
| bpsim_Data/sharedassets0.assets | 55.7 MB | 14.0 MB |
| bpsim_Data/sharedassets0.assets.resS | 46.0 MB | 3.2 MB |
| UnityPlayer.dll | 29.0 MB | 9.1 MB |
| 整目录 | 197.2 MB | 44.7 MB |

- 分发形态优先选 7z / 自解压包（197.2 → 44.7 MB），而不是 UPX；
- 需要"解压即玩、不带压缩包"时才用 UPX（197.2 → 169.6 MB）；
- 想再往下砍只能动 Unity 侧：纹理压缩格式、音频压缩、Shader 变体裁剪（本项目已有 UnusedPostProcessShaderStripper）、资源剔除——收益是几十 MB 级；UPX 是 29 MB 级且只此一次。

## 七、复现

1. 取一份 Windows Player 产物（本仓库当前没有 Windows 构建，测试用的是 ../bpsim_2023.0.2_windows_x64 的副本）；
2. 跑 Tools\pack-upx.ps1 -PlayerDir <目录> -Verify；
3. 期望输出 native PE : 46.1 MB -> 18.5 MB、VERIFY: OK (player stayed alive)；
4. 若启动即退，先看应用日志的"错误模块名称"：出现 mono-2.0-bdwgc.dll 就是把 mono 压了，-Restore 后重压，确认 $NeverPack 生效。

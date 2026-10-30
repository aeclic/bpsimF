# BPSIM_ANDROID_BUILD_FIX — 安卓打不开 & 安卓打包过慢

> 2026-10-26。两个问题一起修：
> 1. **前代项目（bpsim 2023.0.2，APK `bpsim_2023.0.2_android.apk`）在安卓设备上无法正常打开**。
> 2. **安卓打包速度过慢**（本工程 bpsim-main 的 Android 构建）。
>
> 两个问题都已在 LDPlayer 模拟器（Android 14 / API 34，含 arm 转译）上复现、修复并验证。

---

## 一、无法打开：Android 上 Vulkan 图形设备线程启动即崩

### 复现（模拟器，实测）

```
adb install -r bpsim_2023.0.2_android.apk          # Success
adb shell am start -n com.bput.bpsim/com.unity3d.player.UnityPlayerActivity
# 约 10 秒后进程死亡
```

logcat（完整日志存 `Logs/bpsim-2023.0.2-android-launch.log`）：

```
D/vulkan  : load vulkan HAL success!
D/libEGL  : loaded /vendor/lib/egl/libGLESv2_adreno.so
F/libc    : Fatal signal 11 (SIGSEGV), code 1 (SEGV_MAPERR), fault addr 0x0 in tid ... (UnityGfxDeviceW), pid ... (com.bput.bpsim)
    #00 pc 00100430  /system/vendor/lib/libvulkan_enc.so (get_host_u64_VkSemaphore+16)
    #01 pc 000f8672  /system/vendor/lib/libvulkan_enc.so (reservedmarshal_VkDescriptorUpdateTemplateCreateInfo)
    #02 pc 0013c9ae  /system/vendor/lib/libvulkan_enc.so (VkEncoder::vkCreateDescriptorUpdateTemplate)
    #03 pc 000bdb7d  /system/vendor/lib/libvulkan_enc.so (entry_vkCreateDescriptorUpdateTemplate)
    #04 pc 004456ed  /system/vendor/lib/libhoudini.so
```

即：**应用在 `UnityGfxDeviceW` 线程上初始化 Vulkan，创建 `VkDescriptorUpdateTemplate` 时空指针崩溃**，第一帧都还没画出来就被 `Force finishing activity` 杀掉。前代 APK 里 Vulkan 与 GLES 两套 API 都在（构建日志里每个 shader 都同时编 `gles3` 和 `vulkan` 两套），Unity 在 Android 上默认先试 Vulkan，于是必然走到这条崩溃路径。

> 次要现象：前代 APK 反复打印 `E/Unity: Unable to load plugin 'HavokNative'`。它随崩溃一起出现，本工程的修复 APK 里不再出现（本工程不依赖该插件），不影响本次结论。

### 根因

Android 图形 API 列表沿用 Unity 默认（Vulkan 优先 + GLES3）。本项目是自绘 BatchRendererGroup 的 2D 渲染，用不到 Vulkan；Vulkan 在模拟器/部分真机驱动上不可靠。

### 修复

**Android 图形 API 只留 OpenGL ES 3.x**（去掉 Vulkan）。在 `Assets/Editor/Bpsim/AndroidBuild.cs` 的 `ApplyAndroidSettings()` 里：

```csharp
PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
```

运行后 `ProjectSettings.asset` 落地为
`m_BuildTargetGraphicsAPIs: [{ m_BuildTarget: AndroidPlayer, m_APIs: 0b000000, m_Automatic: 0 }]`
（`0b000000` = `GraphicsDeviceType.OpenGLES3` = 11）。

### 验证（模拟器，实测）

修复后的 APK 启动：

```
I/Unity: Graphics API: OpenGLES3
I/Unity: [Mods] 初始化完成：发现 0 个 Mod。
I/Unity: [PartSelector] skin part icon -> procedural ...
```

进程存活（多次轮询 pid 不变），`FATAL / SIGSEGV = 0`，`libvulkan_enc` 不再出现。日志存 `Logs/bpsimF-android-launch.log`。

---

## 二、打包过慢：URP 后处理 shader 变体爆炸（UberPost 单 pass 92160 个变体）

### 现场（实测，Editor.log）

修复前正在进行的 Android 构建卡在：

```
Compiling shader "Hidden/Universal Render Pipeline/UberPost" pass "UberPost" (vp)
    Full variant space:         92160
    After settings filtering:   92160
    After built-in stripping:   92160
    After scriptable stripping: 92160   <-- 一个都没剥掉
    [1085s] 49568 / 92160 variants ready   <-- 18 分钟才过半，仍在爬
```

按当时速度（≈46 变体/秒）光 UberPost 这一趟约需 **≈2000 s（30+ 分钟）**，后面还有 FinalPost(2560×2) 等。整个打包十几/几十分钟都耗在 shader 上。所有 shader 的 variant space 合计约 97496，其中 UberPost 占 92160。

### 根因（两层）

1. **图形 API 有两套**：默认 Vulkan + GLES3，同一 shader 要编两份 → 变体数 ×2。
2. **URP 后处理变体没有被剥离**：`UniversalRenderPipelineGlobalSettings` 里
   `m_StripUnusedPostProcessingVariants: 0`；即便置 1，本工程用的是「只含 Shader/ShaderLibrary 的覆盖版 URP 包」
   （`Packages/com.unity.render-pipelines.universal` 里没有 Editor 的 `ShaderPreprocessor`），该开关在这里不生效——
   实测置 1 后 UberPost 仍是 `After scriptable stripping: 46080`。
   而本工程**根本不用后处理**：两个场景相机都是 `m_RenderPostProcessing: 0`，且是自绘 BRG。

### 修复

1. 去掉 Vulkan（见上）→ 变体数先减半（92160 → 46080）。
2. 新增 `Assets/Editor/Bpsim/UnusedPostProcessShaderStripper.cs`（`IPreprocessShaders`），
   在构建期把这些**从不使用**的 URP 后处理 shader 的变体全部清空：
   UberPost / FinalPost / Bloom / LutBuilderLdr|Hdr / Scaling Setup /
   GaussianDepthOfField / BokehDepthOfField / MotionBlur / PaniniProjection / TemporalAA / CameraMotionBlur。
3. `UniversalRenderPipelineGlobalSettings.asset` 的 `m_StripUnusedPostProcessingVariants` 也顺手置 1（无害；真正生效的是上面的 stripper）。

修复后同一行日志变成：

```
Compiling shader "Hidden/Universal Render Pipeline/UberPost" pass "UberPost" (vp)
    Full variant space:         46080
    After scriptable stripping: 0
Serialized binary data for shader ... UberPost   (gles3 total internal programs: 0)
Assets/Shader/Hidden_Universal Render Pipeline_UberPost.shader   0.8 kb   <-- 近乎为空
```

FinalPost / Bloom / LutBuilderHdr / Scaling Setup 同样 `After scriptable stripping: 0`。

### 结果（实测）

| | 修前 | 修后 |
| --- | --- | --- |
| UberPost 变体 | 92160（后处理未剥离，双 API） | **0** |
| shader 阶段 | 单 pass ≈2000s 仍未完 | 秒级 |
| 打包（`BuildPipeline.BuildPlayer` 计时） | 十几~几十分钟（卡在 shader） | **60.4 s**（清空 `Library/ShaderCache` 冷编）/ 98.3 s（热） |
| 产物 | — | `Builds/Android/bpsimF_android.apk`，81.9 MB，armeabi-v7a，GLES3，minSdk22/targetSdk33 |

其中 Gradle 打包本身约 34–40 s，是现在的主要耗时，属正常。

---

## 三、改动清单

| 文件 | 改动 |
| --- | --- |
| `Assets/Editor/Bpsim/AndroidBuild.cs`（新增） | 菜单 / 批处理入口；应用 Android 设置（GLES3-only、ARMv7、Mono）并出包到 `Builds/Android/bpsimF_android.apk`，打印耗时 |
| `Assets/Editor/Bpsim/UnusedPostProcessShaderStripper.cs`（新增） | `IPreprocessShaders`：构建期清空未使用的 URP 后处理 shader 变体 |
| `Assets/MonoBehaviour/UniversalRenderPipelineGlobalSettings.asset` | `m_StripUnusedPostProcessingVariants: 0 → 1` |
| `ProjectSettings/ProjectSettings.asset` | Android 图形 API = OpenGLES3（脚本写入）、`AndroidTargetArchitectures` 保持 1（ARMv7）、backend Mono2x |

## 四、怎么打包 / 怎么验

```bat
:: 菜单：Tools/BPSim/Android/Build APK
:: 批处理：
..\Tools\unity-batch-bpsim.bat -executeMethod Bpsim.EditorTools.AndroidBuild.Build

:: 安装到本机模拟器（LDPlayer）并启动
"D:\leidian\LDPlayer14\adb.exe" install -r Builds\Android\bpsimF_android.apk
"D:\leidian\LDPlayer14\adb.exe" shell am start -n com.bprc.bpsimF/com.unity3d.player.UnityPlayerActivity
```

## 五、备注 / 仍存项

- **要上 64 位商店包**：把 `ApplyAndroidSettings()` 里 `AndroidArchitecture.ARMv7` 改成 `ARM64`（或 `ARMv7 | ARM64`）。模拟器已验证 arm 转译可跑 armeabi-v7a；真机/商店建议 ARM64。加 ARM64 会让 Burst AOT/原生编两份，打包会变慢。
- 打包产物启动仍打印既有的 `Script error: OnParticleUpdateJobScheduled` 与
  `BurstCompiler.CompileILPPMethod2 ... ArgumentNullException: burstMethodHandle`（`FanPropellerSystem` 的 Burst DirectCall 静态构造），
  之后照常进游戏——与本次修复无关，见 AGENT.md §11.3。
- 若将来重新启用后处理：删掉 `UnusedPostProcessShaderStripper` 里对应 shader 名，并把相机 `m_RenderPostProcessing` 打开。

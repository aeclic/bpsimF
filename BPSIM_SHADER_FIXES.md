# bpsim-main 控制台报错修复（ShaderRipper 对照 + 3 处根因修复）

2026-10-26。起因：Windows Player 构建失败，控制台 6 条 shader 错误、`Error building Player: 10 errors`。
工具链：用 [ShaderRipper](https://github.com/StArraySharp/ShaderRipper) 从原版发布包
`bpsim_2023.0.2_windows_x64/bpsim_Data` 里把原版 shader 全部抓出来做"事实对照"，再针对根因改工程内的 shader。

## 1. 修复前的报错（`%LOCALAPPDATA%\Unity\Editor\Editor.log`）

```
Shader error in 'Hidden/Universal Render Pipeline/UberPost': undeclared identifier 'RemapFoveatedRenderingDistort' at line 184 (on d3d11)
Shader error in 'Hidden/Universal Render Pipeline/Bloom': undeclared identifier 'RemapFoveatedRenderingResolve' at line 60 (on d3d11)
Shader error in 'Hidden/OcclusionDebugOccluders': unrecognized identifier 'UNITY_VERTEX_INPUT_INSTANCE_ID' at line 32 (on d3d11)
Shader error in 'Hidden/OcclusionDebugComposite': unrecognized identifier 'TEXTURE2D' at line 30 (on d3d11)
Shader error in 'Hidden/OccludeeScreenSpaceAABB': unrecognized identifier 'UNITY_VERTEX_INPUT_INSTANCE_ID' at line 39 (on d3d11)
Shader error in 'Bpsim/URP/Unlit_Texture_Instancing': redefinition of '_Color' at line 110 (on d3d11)
...
Error building Player: 10 errors
Build completed with a result of 'Failed' in 30 seconds
```

（日志里另有一批 `PhysicsPhaseProbe.cs(99,5) error DC0058 ... partial keyword` 是**更早**的陈旧记录，该文件现在
`public abstract partial class PhaseMarker : SystemBase` 已经带 `partial`，与本轮无关。）

## 2. ShaderRipper：从原版发布包里取"真值"

用的是官方发布版单文件 exe（v1.1.0，AOT，无需 .NET SDK）：

```
# 仓库：/d/temp/ShaderRipper        exe：/d/temp/ShaderRipper-bin/ShaderRipper.exe
ShaderRipper.exe --root /d/temp/ShaderRipper/src/ShaderRipper \
  --list-shaders "<game>/bpsim_Data/sharedassets0.assets"
ShaderRipper.exe --root /d/temp/ShaderRipper/src/ShaderRipper \
  --file "<game>/bpsim_Data/sharedassets0.assets"      --out /d/temp/rip-sharedassets0.assets
ShaderRipper.exe --root /d/temp/ShaderRipper/src/ShaderRipper \
  --file "<game>/bpsim_Data/globalgamemanagers.assets" --out /d/temp/rip-globalgamemanagers.assets
```

结果：

| 数据文件 | 抓到 | 说明 |
|---|---|---|
| `sharedassets0.assets` | 2 个 | `Bpsim/URP/Unlit_Texture_Instancing`、`Bpsim/Unlit_Texture` |
| `globalgamemanagers.assets` | 28 个 | `Hidden/Universal Render Pipeline/{Bloom,UberPost,FinalPost,…}`、`Hidden/Light2D-*`、`Hidden/Shadow2D*`、`Hidden/Universal/CoreBlit` 等 |
| `resources.assets` | 0 | 工具读该文件时抛 `ArgumentOutOfRangeException … SkipSerializedType`（工具侧限制，非工程问题） |

这些产物是 **DXBC 逐指令转写**，用途是当"原版事实参照"，不直接覆盖工程里的文件：

- `Bpsim/URP/Unlit_Texture_Instancing`：原版只有 5 个属性（`_MainTex / _Color / _Rect / _UVRect / _BlendFactor`），
  变体集是 `DOTS_INSTANCING_ON` × `INSTANCING_ON` 的 3/4 组合，`#pragma target 5.0`，Pass 标签在原版数据里
  恢复不出 `Universal2D`。工程版多出的 `_LayerTint`（分层着色）和 `LightMode=Universal2D` 是**本项目需要的**，
  所以只借它校验"属性/变体确实如此"，不做整文件替换。
- `Hidden/Universal Render Pipeline/UberPost`：转写出来 168 071 行。URP 内置后处理 shader 若换成这种转写，
  会丢掉 `Packages/...` 的 include 关系与 SRP Batcher 兼容性，得不偿失 —— 因此错误 1、2 走的是"补上游缺失的
  fallback"路线（见下）。
- `Hidden/OcclusionDebug*`：原版发布包里**没有**这三个 shader（源码注释本来就写着 dead asset），
  只能按工程内的重建版本修。

## 3. 三处根因与修复

### 3.1 Bloom / UberPost：`RemapFoveatedRendering*` 在 d3d11 上没有定义

URP 14.0.7 的 `Bloom.shader:186` / `UberPost.shader:13` 都有
`#pragma multi_compile_fragment _ _FOVEATED_RENDERING_NON_UNIFORM_RASTER`。**Player 构建会把这个 multi_compile
的"开"变体也编出来**（日志：`Full variant space: 4 … After scriptable stripping: 4`），于是 d3d11 上就要编
`#if defined(_FOVEATED_RENDERING_NON_UNIFORM_RASTER)` 里那句 `RemapFoveatedRenderingResolve/Distort`。
而上游 `FoveatedRendering.hlsl` **只在 PS5 下定义这些函数**：

```
#if defined(SHADER_API_PS5) && defined(_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
#include ".../FoveatedRendering_PSSL.hlsl"      // 只有 PS5 才有实现
#endif
```

（工程内 vendored 副本、编辑器内置 14.0.7 包、`Unity-Technologies/Graphics` 的 `2022.2/staging` 三者内容一致，
所以不是"我们拷丢了"。）

**修复**：`Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl` 补非 PS5 的恒等 fallback
（`Distort`/`Resolve`/`Naive` 返回 `uv`，`Density` 返回 `float2(1,1)` —— 与 `SSAO.hlsl:362` 的
`float2 pixelDensity = RemapFoveatedRenderingDensity(...)` 期望的 `(1,1)` 一致）。该变体在桌面端是死代码
（URP 只在 PS5 / VRS-XR 下打开这个关键字），恒等即正确行为。

### 3.2 三个 occlusion 调试 shader：没有 include SRP 着色器库

`Assets/Resources/occlusion/{occlusiondebugoccluders,occlusiondebugcomposite,occludeescreenspaceaabb}.shader`
只有裸 `HLSLPROGRAM`，却用了 `UNITY_VERTEX_INPUT_INSTANCE_ID` / `UNITY_VERTEX_OUTPUT_STEREO` / `UNITY_SETUP_INSTANCE_ID` /
`TEXTURE2D` / `SAMPLE_TEXTURE2D` / `TransformObjectToHClip` —— 这些全在 URP/Core 的着色器库里。

**修复**：每个 `#pragma target 3.0` 之后加
`#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"`。

### 3.3 `Bpsim/URP/Unlit_Texture_Instancing`：`_Color` 重复定义

`CBUFFER_START(UnityPerMaterial)` 里已经声明了 `_Color/_Rect/_UVRect/_BlendFactor/_LayerTint`，
而 `#if …DOTS… #else` 的 `#else` 分支又把同样的 5 个 `float4` 裸声了一遍 → 非实例化变体里 `_Color` 重定义。

**修复**：`#else` 分支只保留 `#define BPSIM_INSTANCED_PROP(type, name, def) name`，删掉那 5 行重复声明
（MaterialPropertyBlock 写的正是 `UnityPerMaterial` 的槽位，语义不变）。

## 4. 验证

新增 `Assets/Editor/Bpsim/ShaderVariantProbe.cs`（通过 DSH 桥接 `127.0.0.1:8791` 调用，编辑器内**真正编译**）：

```
py br.py invoke_static '{"type":"Bpsim.EditorTools.ShaderVariantProbe","method":"Run"}'
```

它做两件事：

1. 对 7 个 shader 打印 import 期的 `ShaderMessage[]`；
2. `Shader.EnableKeyword("_FOVEATED_RENDERING_NON_UNIFORM_RASTER")` 后对 Bloom 的 4 个 pass、UberPost 的 1 个 pass
   逐 pass `ShaderUtil.CompilePass`，再对 3 个 occlusion shader 和 Unlit 的 2 个 pass 编译普通变体，
   期间挂 `Application.logMessageReceived` 收集错误/告警。

结果（修复后）：

```
FOVEATED … Bloom pass 0/1/2/3 compiled
FOVEATED … UberPost pass 0 compiled
PLAIN … OcclusionDebugOccluders / OcclusionDebugComposite / OccludeeScreenSpaceAABB compiled
PLAIN … Bpsim/URP/Unlit_Texture_Instancing pass 0/1 compiled
---- captured log messages (0) ----
```

**敏感性自检**（证明 probe 不是空转）：把 `FoveatedRendering.hlsl` 临时改回上游 8 行版、重跑 probe，立刻报出与
构建里一模一样的错误 ——

```
Error: Shader error in 'Hidden/Universal Render Pipeline/Bloom': undeclared identifier
'RemapFoveatedRenderingResolve' at line 60 (on d3d11)  Compiling Subshader: 0, Pass: Bloom Prefilter,
Fragment program with _FOVEATED_RENDERING_NON_UNIFORM_RASTER
```

恢复修复后回到 0 条。`FinalPost` 还剩 1 条**import 期警告**（`CalculateDebugColorRenderingSettings` 可能未初始化），
属 URP 自带代码，不阻塞构建。

## 5. 端到端

`ShaderVariantProbe.StartBuild()` / `BuildStatus()` 可以从编辑器里直接跑一次 StandaloneWindows64 构建
（输出到工程外的 `_shaderfix_build/`，不碰玩家的构建产物）。修复后的构建结果见本节末尾的实测记录。

```
py br.py invoke_static '{"type":"Bpsim.EditorTools.ShaderVariantProbe","method":"StartBuild"}'
py br.py invoke_static '{"type":"Bpsim.EditorTools.ShaderVariantProbe","method":"BuildStatus"}'
```

> **记录（2026-10-26）**：`Result: Success.`，`BuildPipeline` 报告 `Succeeded errors=4 warnings=3 time=00:16:05`，
> 产物 `_shaderfix_build/bpsim.exe` + `bpsim_Data/`。构建区间（`Editor.log` 第 14234 行之后）**没有任何
> `Shader error in`**。对比修复前的同一台机器、同一个工程：`Error building Player: 10 errors` /
> `Build completed with a result of 'Failed'`。
>
> 那 6 条 shader 错误消失后剩下的 `errors=4` 是另一件事，见 5.1。

### 5.1 剩下的 4 条 error 是 DOTS 计算着色器，不是 URP shader

`errors=4` 的内容是四行同构的日志：

```
Computing compute shader "BlendShapeComputeShader"
    starting stripping...
unsupported platform: couldn't get group name for target %i        (BuildTargetPlatformSpecific.cpp:348)
Assertion failed on expression: 's.buildTarget.platform >= kFirstValidStandaloneTarget'
    finished in 0.01 seconds. 1 of 1 variants left
Compute shader BlendShapeComputeShader was not imported correctly.  (ShaderCompilationManager.cpp:1419)
```

四个名字：`BlendShapeComputeShader` / `InstantiateDeformationData` / `SkinningComputeShader` / `SparseUploader`，
全部来自 `com.unity.entities.graphics` 的蒙皮/形变内核。

关键事实：

1. **这与本次修 shader 无关**。用户自己在 Build Settings 窗口点的构建（栈是
   `BuildPlayerWindow/DefaultBuildMethods:BuildPlayer`）产生的内容**完全一样**，包括那两条 assert。
2. **两份同名资产**。`AssetDatabase.FindAssets("<name> t:ComputeShader")` 对每个名字都返回 **2** 个：
   - `Assets/Resources/<name>.asset` —— 反编译出来的**原生 ComputeShader 序列化 blob**（`!u!72`、
     `NativeFormatImporter`、`targetRenderer: 2 / targetLevel: 0`，也就是原版游戏构建后的产物形态）；
   - `Packages/com.unity.entities.graphics/.../Resources/<name>.compute` —— 包内**源码**。
3. 构建日志里每个名字出现**两次**：第一次 `1 of 1 variants left` 之后报错，第二次没有任何错误。而
   `Resources.Load<ComputeShader>("<name>")`（`BlendShapeDeformationSystem.cs:45`、
   `InstantiateDeformationSystem.cs:37`、`SkinningDeformationSystem.cs:47`）按**名字**取资源，
   两份同名的东西在 `Resources` 里是互相遮蔽的。
4. 四份原生 blob **没有任何 GUID 引用**（在 `Assets`/`Packages`/`ProjectSettings` 全量搜 guid 结果为 0）。

结论：那份原生 blob 是反编译残留，它无法为构建目标重新导入（原生 blob 只对烘焙它的平台/层级有效），
而包内的 `.compute` 源码可以，所以构建要为它报 "was not imported correctly"。

**处理**：把四个 blob 连同 `.meta` 移到 `_codex_work/removed-compute-stubs/`（备份，不删数据）。
`Resources.Load` 之后解析到包内同名 `.compute`（已用 `ComputeReport()` 确认每个名字只剩 1 份资产、路径在包内，
`SkinningDense1ComputeKernel` 在源码里同样能找到，kernel 索引 0）。

> 复验状态：删除后重新构建的那次被**用户进入 Play Mode** 打断了（domain reload 清掉了
> `ShaderVariantProbe` 的静态构建状态，`_shaderfix_build/bpsim.exe` 时间戳没有更新），所以
> "`errors=4` → `0`" 尚未在完整构建里实测。等编辑器退出 Play Mode 后重跑 `StartBuild` 即可确认。

## 6. 改动清单

| 文件 | 改动 |
|---|---|
| `Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl` | 非 PS5 平台补 4 个 `RemapFoveatedRendering*` 恒等实现 |
| `Assets/Resources/occlusion/occlusiondebugoccluders.shader` | + `#include` URP `Core.hlsl` |
| `Assets/Resources/occlusion/occlusiondebugcomposite.shader` | 同上 |
| `Assets/Resources/occlusion/occludeescreenspaceaabb.shader` | 同上 |
| `Assets/Shader/Bpsim_URP_Unlit_Texture_Instancing.shader` | `#else` 分支删除重复的 5 个 `float4` 声明 |
| `Assets/Editor/Bpsim/ShaderVariantProbe.cs` | 新增：变体/probe 编译自检、`ComputeReport()`、可选的端到端构建入口 |
| `Assets/Resources/{BlendShapeComputeShader,InstantiateDeformationData,SkinningComputeShader,SparseUploader}.asset` | **移除**（反编译原生 blob，被包内同名 `.compute` 遮蔽；备份在 `_codex_work/removed-compute-stubs/`） |
| `BPSIM_SHADER_FIXES.md` | 本文件 |

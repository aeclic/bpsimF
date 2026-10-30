# 安卓「部件放下去没有正常显示贴图」修复记录

**症状.** 同一份工程打出来的 Windows 包一切正常，安卓包在建造界面放下部件后**看不到部件**
（用户描述为「没有正常显示贴图」）。安卓上连场景里已有的部件也一个都不画，
所以它不是「贴图错位/串图」，而是**这一整条 BRG 渲染路径在 GLES 上根本没建起来**。

## 根因

### 1. 主因：GLES3 的 BatchRendererGroup 只接受 constant buffer

部件的批量绘制在 Assets/Scripts/Assembly-CSharp/Bpsim/Parts/SceneRenderSystem.cs：
场景里每个部件是一条实例数据，用 BatchRendererGroup（BRG）一次画完。
实例数据放在一块 GraphicsBuffer 里，最后交给
m_batchRendererGroup.AddBatch(batchMetadata, m_buffer.bufferHandle)（两参重载）。

问题在于 BatchRendererGroup.BufferTarget（**当前图形 API 能接受的 target**）在 GLES/WebGL 上是
ConstantBuffer，而 d3d11 上是 Raw。两参重载的 windowSize 恒为 0，Unity 在 GLES 上直接拒绝：

E Unity : BatchRendererGroup.AddBatch requires a non-zero windowSize parameter when BufferTarget is ConstantBuffer.

batch 从未创建 ⇒ 部件**完全不画**。这条错误**只打一次**，因为
OnUpdate 里 UpdateBatch() 只在容量增长时才调，很容易在 logcat 里漏看。

### 2. 常数缓冲窗口只有 16 KB

com.unity.render-pipelines.core/ShaderLibrary/UnityDOTSInstancing.hlsl 里：

    #ifdef UNITY_DOTS_INSTANCING_UNIFORM_BUFFER     // GLES / WebGL 走这条
    CBUFFER_START(unity_DOTSInstanceData)
        float4 unity_DOTSInstanceDataRaw[1024];     // 1024 x 16 = 16384 B
    CBUFFER_END
    #else                                            // D3D11 走这条
        ByteAddressBuffer unity_DOTSInstanceData;
    #endif

寻址公式（同文件 264-280 行）是 address = metadata 里的 byteOffset + instanceIndex x 该属性元素大小，
**必须落在 16384 B 之内**。而本工程每个部件要 **176 B**
（unity_ObjectToWorld 48 + unity_WorldToObject 48 + _Color/_Rect/_UVRect/_BlendFactor/_LayerTint 各 16），
所以一个窗口最多装 16384 / 176 = 93 个部件。存档「不败荣光」有 1648 个部件，
原来那种「一整块 buffer 全塞进去」的布局在 UBO 下必然越界。

### 3. 顺带发现的第二个 bug：draw command 计数错

分页改造后 EmitDrawCommandsJob.Execute() 里统计「不同的页分组数」时，
用第一个元素给「上一组」播种：

    int num2 = ((length > 0) ? SortedIndices[0].Group : (-1));   // <- 错

第一组因此看起来「和上一组一样」，于是

- **单页场景**（Windows/d3d11，或部件很少）算出 drawCommandCount = 0 ⇒ 什么都不画；
- **多页场景**少算 1，但末尾 if (num5 >= 0) EmitDrawCommand(unsafePtr, num4, ...) 照样写第 num 条
  ⇒ **越界写进 Malloc(num * sizeof(BatchDrawCommand)) 之外** ⇒
  E/Unity: Invalid memory pointer was detected in ThreadsafeLinearAllocator::Deallocate! + 主线程卡死（ANR）。

## 修复

### SceneRenderSystem：常数缓冲分页

- ConfigureConstantBuffer()（:489）：读 BatchRendererGroup.BufferTarget 判断是否走 CB；
  m_pageBytes = min(GetConstantBufferMaxWindowSize(), 16384)（并受 SystemInfo.maxConstantBufferSize 约束）、
  m_instancesPerPage = max(1, m_pageBytes / m_sizeOfInstance) = 93、
  m_pageStride 按 GetConstantBufferOffsetAlignment()（256）向上对齐且强制 16 的倍数。
  启动日志：`[SceneRenderSystem] constant buffer instancing: target=ConstantBuffer window=16384
  alignment=256 partsPerPage=93 pageStride=16384 maxWindow=16384`（安卓上每次启动都会打，可用来确认走了哪条路）。
- AllocateBuffer（:545）：CB 模式建 GraphicsBuffer.Target.Constant（num3 * m_pageStride / 16 个 float4），
  Raw 模式保持原样；重建前先 m_performCullingDependency.Complete() + RemoveBatches() + Dispose()，
  否则上一帧的 culling job 会读到悬空 handle。
- UploadPages()（:582）：**逐页**布局 —— 每页内仍是属性主序（每个属性一段连续区，
  步长 = 该属性元素大小），页与页之间按 m_pageStride 隔开；一次性 m_buffer.SetData() 上传。
- UpdateBatch()（:733）：CB 模式每页一个 batch，
  AddBatch(metadata, handle, (uint)(page * m_pageStride), (uint)m_pageBytes)；
  失败会打 `[SceneRenderSystem] AddBatch failed for page i/n (offset=..., window=...)`。
- 实例下标：渲染侧按页偏移 —— visibleInstances[j] = Index - page * InstancesPerPage，
  所以 shader 里的 instanceIndex 在每页内从 0 重新开始，正好对上「窗口起点 = 该页 bufferOffset」的语义。
- OnPerformCulling 的早退条件改成 !m_batchesReady || m_instanceCount == 0（m_batchID 已删除，
  换成 NativeArray<BatchID> m_pageBatches）。

### EmitDrawCommandsJob：计数

:258 改成 `int num2 = -1;`，并加注释说明原因。分页与不分页两条路都用得上这个计数。

**shader 一行没改**：Assets/Shader/Bpsim_URP_Unlit_Texture_Instancing.shader 的两个 pass
（BpsimUnlit2D LightMode=Universal2D / BpsimUnlit SRPDefaultUnlit）在
UNITY_DOTS_INSTANCING_UNIFORM_BUFFER 下由 Unity 自己把 metadata + 窗口拼成地址，
BPSIM_INSTANCED_PROP 宏不用动。

## 验证（雷电模拟器 emulator-5554，1920x1080，模拟器 GPU 是 GL ES 3.1）

出包：`cmd //c 'Tools\\unity-batch.bat -executeMethod Bpsim.EditorTools.AndroidBuild.Build'`
→ Builds/Android/bpsimF_android.apk（88,835,009 B）。

- **1648 部件大场景**（不败荣光 CSV，28850 B）：不再 ANR，全部部件（绿/青/红方块、车轮、风扇、猪头、
  网格背景）贴图正确。修之前同一场景会冻结并弹「bpsimF没有响应」，/data/anr/ 里能查到记录。
- **单页小场景**（同一 CSV 的前 40 行）：贴图正常 —— 这一条专门验第 3 节那个计数 bug（单页曾 = 0 个 draw command）。
- **放置**：左栏切「增加」后在空白网格上点，成功放下部件且贴图正常。
- **放大镜**（同时上线的另一个功能）：点一下整车入画并居中，再点复位，见 BUILD_TOOLBAR.md。
- logcat 无 Invalid memory pointer、无 E/Unity 报错；/data/anr/ 只剩 14:45 的旧文件。

## 设备上复现/验收的操作步骤（复用）

安卓上 BpsimLevels 导入器是关掉的（LevelCatalog.IsAvailable 用 File.Exists(StreamingAssets/...)
判断，APK 里是 jar 内路径，永远 false），所以**不能靠关卡列表进场景**。用工具栏的
「加载场景」对话框直接读工程根的 CSV 蓝图：

    ADB=/d/folder/2022.2.23f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe
    export MSYS_NO_PATHCONV=1
    "$ADB" install -r Builds/Android/bpsimF_android.apk
    "$ADB" push 不败荣光 /sdcard/Android/data/com.bprc.bpsimF/files/scene.csv
    "$ADB" shell am start -n com.bprc.bpsimF/com.unity3d.player.UnityPlayerActivity
    # 屏幕坐标 1920x1080：载入按钮 (429,46) → 路径框 (884,293) → 键盘「确定」(1833,1024) → 「加载」(1355,838)
    "$ADB" shell input swipe 429 46 429 46 120
    "$ADB" shell input text '/storage/emulated/0/Android/data/com.bprc.bpsimF/files/scene.csv'
    "$ADB" exec-out screencap -p > _codex_work/android/x.png

坑：`input tap` 事件会被这个 Unity 包丢掉，**必须 `input swipe X Y X Y 120`**（同点、120 ms）。
`logcat -d` 在清过 buffer 后抓不到 Unity 行，要在设备上常驻落盘：
`adb shell "nohup logcat -v time > /sdcard/.../live.txt 2>&1 &"`，再 `adb shell su 0 cat` 拉回
（模拟器已 root，形式是 `su 0 <cmd>`）。

## 后续加固（2026-10-28 晚）

- `EmitDrawCommand`（SceneRenderSystem.cs:313）原来把页号夹取写成
  `int num = ((group < BatchIDs.Length) ? group : (BatchIDs.Length - 1));` —— `BatchIDs.Length == 0`
  时会索引 -1。现已加 `if (BatchIDs.Length == 0) return;` 早退，并把夹取收紧为
  `group >= 0 && group < BatchIDs.Length`（防御：culling job 正常只产生已注册页的 Group，
  但崩溃现场分析时这里是唯一可疑的托管侧越界点，宁可空转也不越界）。
- 背景：设备（雷电模拟器）在连续运行约 5.5 h 后出现过一次 SIGSEGV
  （UnityMain 在 `ScriptableRenderContext:Submit_Internal_Injected`，`lr=0`，
  app 目录 tombstone_00 2026-10-28 16:33）。60+ 分钟浸泡测试（80 轮 × 40 s）
  未复现；模拟器本身跑在 libhoudini ARM 转译层上（tombstone maps 里有
  /system/vendor/lib/libhoudini.so，x86_64 内核转译 armeabi-v7a），崩溃可能与转译层
  或驱动相关，而非分页代码。加固后重新出包并复验通过（见下）。
- 复验（18:03 出包 88,835,006 B）：1648 部件大场景载入、整车贴图全对、放大镜
  on saved=20 overview=23.657 / off restore=20 正确复位、「增加」模式放置部件贴图正常；
  logcat 无 E/Unity、/data/anr/ 无新记录。

## 相关文件

| 文件 | 说明 |
| --- | --- |
| Assets/Scripts/Assembly-CSharp/Bpsim/Parts/SceneRenderSystem.cs | 常数缓冲分页（ConfigureConstantBuffer / AllocateBuffer / UploadPages / UpdateBatch）+ draw command 计数修复 + EmitDrawCommand 空批次/页号夹取防护 |
| Assets/Shader/Bpsim_URP_Unlit_Texture_Instancing.shader | 未改动；两个 pass 同时支持 UBO 与 SSBO 两种 DOTS instancing |
| Assets/Editor/Bpsim/AndroidBuild.cs | 出包入口（GLES3-only、ARMv7、Mono2x），菜单 Tools/BPSim/Android/Build APK |

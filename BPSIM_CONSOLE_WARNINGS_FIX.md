# bpsim-main 控制台黄/红警告清理（2026-10-28）

## 结论

| 场景 | 修复前 | 修复后 |
| --- | --- | --- |
| 编辑器空闲 | 1 条 Warning（layout 加载失败） | 0 |
| 进 Play（含恢复 20k 部件存档） | 16 条 Burst 假警告 + 1 条 `Ignoring invalid [UpdateAfter]` + 19 条 `Destroy may not be called from edit mode!` + 2 条 `NullReferenceException` | 0 |
| Play 内跑物理（`BpsimPlayQA place` / `fall`） | 上面全部 + 4 条 Havok job 的 Burst 假警告 | 0 |
| 编译 | 4 条 `warning CS` | 0 error / 0 warning |
| Windows Player | `ArgumentNullException: burstMethodHandle` + `Script error: OnParticleUpdateJobScheduled` | 只剩后者（见「未修项」） |
| Android Player | `Unable to load plugin 'HavokNative'` ×40+ + 2 条 NRE | 只剩后者（见「未修项」） |

验证口径：`GET http://127.0.0.1:8791/status` 的 `logs` 环形缓冲按 `type` 计数；物理侧另有 `Logs/bpsim-playqa.log`（`backend=HavokPhysics step=HavokPhysics`，7 个 dynamic body 从 t=1.0 的 −8.79 掉到 t=2.0 的 −21.68，说明确实在跑 Havok）。

---

## 逐条修复

### 1. 16 条 Burst 假警告（`not a known Burst entry point`，Havok 预编译插件）

文本：

```
Compilation was requested for method `Havok.Physics.PhysicsSimulationPickerSystem, Havok.Physics,
Version=0.0.0.0, ...::__codegen__OnCreate(System.IntPtr, ...)` but it is not a known Burst entry point.
This may be because the [BurstCompile] method is defined in a generic class, ...
```

**为什么之前的两层 `ILogHandler` 过滤器拦不到**：这条消息不是 `Debug.LogWarning` 发出的，而是 Burst 的编辑器侧客户端
`Burst.Compiler.IL.Client.CompilerClient.HandleInvalidMethodRequest()` 走 `HandleLogMessage` / `_debugLogWriter`
直接写出去的，绕过了 `Debug.unityLogger.logHandler` 整条链路（`Logs/burst-warn-trace.log` 里 190 条记录
`stackTrace` 全是空串就是这个原因）。所以两个过滤器（`BurstLoader.cs` 里的 `BpsimLogHandler`、
`Assets/Editor/Bpsim/BurstEntryPointWarningFilter.cs`）从一开始就不可能生效 —— 已删除，别再往回加。

`HandleInvalidMethodRequest` 只在 `CompilerClient.OnEntryPointsFound` / `MakeFunctionPointersAvailable`
里被调用，即「有人向 Burst 请求了某个 Havok 方法的函数指针，而编译器返回的入口点列表里对不上」。
真正的修法是**不去请求**：`Havok.Physics*.dll` 是别人用另一个 Burst 版本 IL 后处理过的预编译程序集，
其 `$BurstDirectCall` stub 永远解析不了，直接走 Burst 自己的托管回退（等同于 `--burst-disable-compilation`）。

补丁都在 `Packages/com.unity.burst/Runtime/BurstCompiler.cs`（本工程的 burst 是**嵌入**在 `Packages/` 下的，
不是 `Library/PackageCache`）：

| 位置 | 内容 |
| --- | --- |
| `CompileILPPMethod2` (~:226) | `IsPrecompiledPluginAssembly(burstMethod)` → `return IntPtr.Zero`（ILPP 直呼路径） |
| `GetILPPMethodFunctionPointer2` (~:297) | `ilppMethod == Zero && managedMethodHandle != Zero` → 返回 `MethodHandle.GetFunctionPointer()` |
| `Compile(...)` (`#if UNITY_EDITOR`, ~:418) | `!isILPostProcessing && IsPrecompiledPluginAssembly(methodInfo)` → `Marshal.GetFunctionPointerForDelegate(delegate)` |
| `IsPrecompiledPluginAssembly` (~:242) | 认 `Havok.Physics` / `Havok.Physics.Hybrid`，并递归看闭合泛型的泛型实参 |

`Compile()` 那处是最后补齐的：去掉 `isFunctionPointer` 前置条件后，`Unity.Jobs.IJobExtensions+JobStruct<T>::Execute`
和 `IJobParallelForExtensions+ParallelForJobStruct<T>::Execute`（T = `HavokSimulation+SyncJob` / `StartStepJob` /
`StepJob` / `ExtractMotionsJob`）这类**委托编译**路径也不再问编译器 —— 之前只挡住了 `isFunctionPointer` 那条，
所以一进物理（Havok 真的开始 step）就会漏出 4 条。

> 注意：`Assets/Scripts/Assembly-CSharp/-BurstDirectCallInitializer.cs` 与
> `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/FanPropellerSystem.cs` 里的
> `CompileILPPMethod2(default(RuntimeMethodHandle))` 是反编译丢了 `ldtoken` 的产物，
> 已经在 stub 里被 `try/catch` 包住，不要「修」回去。

### 2. `Ignoring invalid [Unity.Entities.UpdateAfterAttribute]`（每次域重载）

`Assets/Editor/BpsimPerf/PhysicsPhaseProbe.cs`：`MarkAfterBuild` 原来写的是
`[UpdateAfter(typeof(BuildPhysicsWorld))]`，而 `BuildPhysicsWorld` 属于 `PhysicsInitializeGroup`、
探针系统属于 `PhysicsSimulationGroup`，跨组排序属性被 Entities 判为非法。
改成组内排序：`MarkBegin` 用 `[UpdateInGroup(typeof(PhysicsSimulationGroup), OrderFirst = true)]`，
`MarkAfterBuild` 用 `[UpdateAfter(typeof(MarkBegin))] + [UpdateBefore(typeof(PhysicsCreateBodyPairsGroup))]`
（`PhysicsSimulationGroup` 本身已经 `[UpdateAfter(typeof(PhysicsInitializeGroup))]`，语义不变）。

### 3. 两个 `NullReferenceException`（每次进 Play，安卓必现）

ECS 世界的第一次 `Update` 早于场景里 Manager 单例的 `Awake()`，而这两个系统在 `OnStartRunning` 里
就读了单例：

- `Assets/Scripts/Assembly-CSharp/Bpsim/Rendering/SpriteSystem.cs`：`OnStartRunning` 里
  `SpriteManager.Instance.SpriteData` → 改成 `TryInitializeData()` 惰性初始化，`OnUpdate` 开头调用，
  `OnStopRunning` 里 `if (m_data.IsCreated) m_data.Dispose()`。
- `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/SceneRenderSystem.cs`：`OnStartRunning` 里
  `CoreManager.Instance.Resources.LoadAsset<Material>("Part_Texture_0")` → 同样改成惰性
  `TryRegisterResources()`。这个尤其重要：注册不上材质的话**安卓上部件完全不可见**。

### 4. 19 条 `Destroy may not be called from edit mode! Use DestroyImmediate instead.`（每次进 Play）

栈：

```
UnityEngine.Object:Destroy
UnityEngine.Rendering.CoreUtils:Destroy (UnityEngine.Object)
UnityEngine.Rendering.Universal.ScriptableRenderer:Dispose
UnityEngine.Rendering.Universal.UniversalRenderPipeline:Dispose
...
```

`Assets/Plugins/Unity.RenderPipelines.Core.Runtime.dll` 是**播放器版本**的 URP 程序集（逆向工程带来的），
`CoreUtils.Destroy` 里没有 `#if UNITY_EDITOR` 的守卫，IL 就是 `if (obj != null) Object.Destroy(obj)` 共 16 字节。
用 `Tools/PatchUrpCoreUtils.py` 把这段换成「编辑模式下走 `DestroyImmediate`、运行/播放器里仍走 `Destroy`」。

脚本自带精简的 ECMA-335 元数据解析器（找 `TypeDef/CoreUtils` → `MethodDef/Destroy` → `MemberRef`
`Application::get_isPlaying` / `Object::Destroy` / `Object::DestroyImmediate`），幂等，带 `--verify-only`
和 `--restore`：

```
python Tools/PatchUrpCoreUtils.py            # 打补丁（默认写回同一个 dll，先备份到 _codex_work/tmp/）
python Tools/PatchUrpCoreUtils.py --verify-only
python Tools/PatchUrpCoreUtils.py --restore
```

真机/播放器里 `Application.isPlaying` 恒 true，所以行为与原来完全一致；只有编辑器里变成 `DestroyImmediate`。

### 5. 4 条 C# 编译警告

| 文件 | 警告 | 修法 |
| --- | --- | --- |
| `Bpsim/Parts/Simulation/WheelSystem.cs:92` | CS0618 | `TryGetSingleton(...)` → `SystemAPI.TryGetSingleton(...)` |
| `Bpsim/Parts/Simulation/AutoGunProjectileSystem.cs:105` | CS0618 | 同上 |
| `Bpsim/Parts/Sandbag.cs:44` | CS0162（`SandbagSystem.Bind` 里 `if (Sandbag.DefaultRotatable)` 恒假 → 整块不可达） | `const bool` → `static readonly bool`，保留那条刻意复现 BPSS 的分支 |
| `Bpsim/UI/SkinPanelHost.cs` | CS0414（`m_statePending` 只写不读） | 删掉字段与两处赋值（`ready` 分支本来就自己 `PostState()`） |

### 6. `The layout "UserSettings\Layouts\default-2022.dwlt" could not be fully loaded`

根因是 `DshBridge` 的桥接状态窗口：自定义 `EditorWindow` 打开时被写进布局文件，下次启动 Unity
解析不出那个类（日志里是 `[LAYOUT] Removed un-parented EditorWindow ... type=UnityEditor.FallbackEditorWindow`），
于是报 layout 加载失败。`Assets/Editor/DshBridge/DshBridge.cs` 已改成**默认不自动打开**状态窗口
（`EditorPrefs "DshBridge.AutoOpenStatusWindow"` 置 true 才打开，菜单 `DshBridge/Status Window` 可手动开）。

### 7. `[BpsimLevels] PartManager never became ready; skipping the session restore.`

`PartManager.IsReady` 依赖 ECS 世界已经创建，而之前那两个 `OnStartRunning` NRE 会让世界初始化中途异常，
`LevelManager.RestoreSession()` 的 600 帧守卫就超时。第 3 条修好后恢复流程正常
（控制台是一条 `[BpsimLevels] restore: scene '不败荣光'`），这条警告不再出现。

### 8. 打包入口：`DirectoryNotFoundException: The path C:/Users/aecli/Downloads/新建文件夹 does not exist.`

`UnityEditor.WindowsStandalone.WinPlayerPostProcessor.PrepareForBuild` 会去清理
`EditorUserBuildSettings.GetBuildLocation()` 记录的**上一次**输出目录，而那个目录早就没了。
`Assets/Editor/Bpsim/WindowsPlayerBuild.cs` 在 `BuildPipeline.BuildPlayer` 之前加了
`EditorUserBuildSettings.SetBuildLocation(BuildTarget.StandaloneWindows64, exe)`。

---

## 安卓侧（与 Havok 闪退同一个任务的产物，一并记录）

- `Assets/Plugins/Android/libs/armeabi-v7a/libhavoknative.so`（4 003 124 B，stripped）+ 对应 `.meta`
  （`Any/Editor/Windows/Linux/OSX` 全关，只开 `Android/ARMv7`）。托管侧是 `DllImport("HavokNative")`，
  所以文件名必须以 `lib` 开头，实际使用名为 `libhavoknative.so`（Android 文件名区分大小写）。
- 该 .so 由 `Tools/PatchHavokNativeAndroid.py` 从旧安卓包里的原始库打补丁得到：
  `HP_CheckCompatibility` 改成 `movs r0,#1; bx lr`，并把 `UnityPluginLoad` 里写 `s_Unlocked` 的那三指令
  改成恒 1（`s_Unlocked` 是 `HP_SyncWorldIn` / `HP_SyncMotionsOut` / `HP_SyncSecondaryMotionsOut` /
  `HP_StepWorld` / `HP_ProcessStep` / `HP_StepVisualDebugger` / `HP_InjectContacts` 七个入口的总开关）。
- 雷电模拟器实测：`I Havok : Found 4 Cpu Cores (sysconf says 4)`，`Unable to load plugin 'HavokNative'` 消失，
  进程不再闪退。
- 打包前提：Burst 需要 Android NDK，而本机 `ANDROID_NDK_ROOT` 为空、`NdkUseEmbedded` 没设，导致
  `GenerateNativePluginsForAssemblies` 直接失败。已设
  `EditorPrefs.SetBool("NdkUseEmbedded", true)` + `EditorPrefs.SetString("AndroidNdkRoot", "<PlaybackEngine>/NDK")`。
- **已知未修（安卓专属假阴性）**：`LevelCatalog.IsAvailable` 用 `File.Exists(streamingAssetsPath/BpsimLevels/index.json)`，
  安卓上 StreamingAssets 在 APK 内部（`jar:file://...!/assets`），`File.*` 永远读不到，于是刷
  `W/BpsimLevels StreamingAssets/BpsimLevels is missing`。要修得改成 UnityWebRequest 读 jar 或构建期解包到
  `persistentDataPath`（编辑器里 `[BpsimLevels] ready: 274 BPLE levels available` 是正常的）。

---

## 未修项（已确认无害/超出范围）

### `Script error: OnParticleUpdateJobScheduled`

Player/Android 启动时（以及编辑器**每次域重载**，写在 `Logs/editor-run.log` 里、但不进 Console 窗口）出现三行：

```
Script error: OnParticleUpdateJobScheduled
The message may not have any parameters.
The message will be ignored.
```

已查明：

- 三行都来自 Unity 原生侧（`UnityPlayer.dll` 里 `MonoScriptCache.cpp` 的那组字符串，
  `OnParticleUpdateJobScheduled` 只是内置消息名表里的一项），不是任何脚本 `Debug.Log` 出来的，
  所以 `ILogHandler` 过滤器够不到、控制台窗口也不显示它。
- 全工程唯一声明这个名字的托管代码是 `Assets/Plugins/UniTask.dll` 里的
  `Cysharp.Threading.Tasks.Triggers.AsyncParticleUpdateJobScheduledTrigger.OnParticleUpdateJobScheduled(ParticleSystemJobData)`
  （游戏里没有任何场景/预制体引用 UniTask 的 trigger 组件，也没代码 AddComponent 它）。
- 现象与 Unity 2022.2 的「零参数消息 + 一带参数处理函数」不匹配一致：引擎在装配/消息表注册阶段发现签名不符，
  打一次就当无事发生，物理与渲染都不受影响。
- 已做过对照实验：把 `-BurstDirectCallInitializer` 里的调用注释掉重新打包，消息照旧 → 与 Burst 无关。

要彻底消掉只能改 UniTask.dll（把那个方法改名）或换 UniTask 版本；代价大于收益，暂不动。

### 其它已验证「不是问题」的日志

- `<color=#12bae9ff>[3D WebView]</color> Using Vuplex 3D WebView for Windows v4.11.2`、
  `[BpsimCompat] JobsUtility.JobDebuggerEnabled = False (managed DOTS fallback)`、
  `[BpsimLevels] ready: ...`、`[Mods] 初始化完成：发现 1 个 Mod。`、`[PartSelector] skin part icon -> ...`
  —— 都是正常的蓝色 `Log`。Console 里 `[Mods]` 那行显示成乱码是 Windows 控制台 GBK 渲染问题，源文件是 UTF-8。
- `PigMotionSystem.cs(50,26)` / `(91,26)` 的 `SP0001`/`CS0282` 是 DOTS `partial struct` 需要的信息级提示，
  已被工程里的 DiagnosticSuppressor 抑制，不用管。

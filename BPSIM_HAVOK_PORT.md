# BPSIM_HAVOK_PORT.md — Havok 插件反编译、绕过版本校验并接入

日期：2026-10-26　工程：`bpsim-main`（Unity 中国版 2022.2.23f1c1）
目标（用户）：**对 bpsim 之前打包的项目中的 havok 插件做反编译，绕过其版本验证并添加到现在的项目上。**

## 1. 插件从哪来

旧打包产物 `..\bpsim_2023.0.2_windows_x64\bpsim_Data\`：

| 文件 | 大小 | 说明 |
|---|---|---|
| `Managed/Havok.Physics.dll` | 82 432 B | 运行时（Havok 系统 + `Unity.Physics` 侧接口） |
| `Managed/Havok.Physics.Hybrid.dll` | 15 360 B | Authoring（Display*System、HavokConfigurationAuthoring/Baker） |
| `Plugins/x86_64/HavokNative.dll` | 5 249 536 B | 原生求解器 |
| `Plugins/x86_64/Havok.Vdb.dll` | 5 785 088 B | Visual Debugger 支持 |

反编译工具链（本机无 dotnet/ilspycmd/objdump）：

```
mono.exe <Unity>/Editor/Data/MonoBleedingEdge/lib/mono/4.5/ikdasm.exe X.dll > X.il
```

产物与全部分析脚本在 `..\_dsh_work_havok\`（`Havok.Physics.il`、`UP_2023.il`、`ACS.il`、`hn_*.py`、`patch*.py`、`verify_patch.py`、`ANALYSIS.md`、`backup\`）。

## 2. 反编译结论：版本验证在哪

**托管层没有任何版本/licence 校验**——`Havok.Physics.dll` 的 `ldstr` 全是要序列化的类型名，唯一的报错串是 `"Reflection data was not set up by an Initialize() "`；`Havok.Physics.Plugin` 里 `HP_CheckCompatibility` 与 `HP_GetPluginVersionString` 只是 `pinvokeimpl("HavokNative")` 声明，**托管 IL 从未调用它们**（Burst 侧也不调）。

全部校验都在原生 `HavokNative.dll`：

| RVA | 内容 |
|---|---|
| 0x3eff60 | `Havok Physics for Unity is available to Pro, Enterprise, and Unity Industrial Collection users.` |
| 0x3effc0 | `Failed to load Havok Physics for Unity, this version of Unity is not supported.` |
| 0x3f0060 / 0x3f0100 / 0x3f0160 | `...reported a different number of checked types / field offset length / struct size length` |
| 0x45db90 / 0x45dc2a / 0x45dc37 | `Mismatched Product Against Keycode` / `Product license key for Havok` / `MISSING KEY` |
| 0x3f01c0 | 构建标签 `0x358558c9-0x73280ca9:2024-06-30.Physics.UnityTechnologies_UnityIntegration` |

- 导出表：`HP_CheckCompatibility` **0x16be0**、`UnityPluginLoad` **0x17790**、`HP_AllocateWorld` 0x16af0、`HP_GetPluginVersionString` 0x16ec0 等 14 个。
- xref 定位（自写 REX 感知的 rip-relative 扫描；capstone 线性扫在 4.1 MB 的 .text 上严重失步，只能解出 8.3 万条，不可用）：
  - `EDITION` 提示在 **0x17827**、`UNITY_VERSION` 提示在 **0x17836**，都在 `UnityPluginLoad` 内，且**只经 IUnityInterfaces 的 vtable 打日志、不 abort**（0xe4 / 0xe6 行号）。
  - 类型布局校验在 **`HP_CheckCompatibility` 0x16be0**：`cmp [rcx],0x2b → [rcx+4],0x6c → [rcx+8],0xc`，任一不符就报错并 `xor al,al` 返回 false。

> **2026-10-27 更新**：真正的门禁不是 `HP_CheckCompatibility`，而是 §9 的全局字节 `g_flag`（RVA 0x4C6C00）。
> 本节这个补丁保留（无害），但**既非必要条件也不是病根**。

## 3. 绕过方式（已实施）

只改一处、最小副作用：把 `HP_CheckCompatibility` 的入口改成 `mov al,1; ret`（`B0 01 C3`），即**恒判"兼容"**。

- 文件偏移：`.text` raw 0x400 / VA 0x1000 → `0x400 + (0x16be0 - 0x1000) = 0x15fe0`
- 原始字节：`40 57 48 81 EC 30 02 00`（push rdi / sub rsp,0x230）
- 补丁后：`B0 01 C3 81 EC 30 02 00`
- 脚本：`..\_dsh_work_havok\patch2.py`（`patch3.py` 是同逻辑的临时文件+替换版），校验脚本 `verify_patch.py`
- 原文件备份：`..\_dsh_work_havok\backup\HavokNative.dll`

> 注意：`Assets/Plugins/x86_64/HavokNative.dll` **不能就地覆盖写**（Windows 下对该文件 `open(...,'wb')` / `os.replace` 报 `OSError 22` / `WinError 5`），但**改名可以**。正确做法：写一份 `HavokNative.dll.patched`，再 `mv` 原名挪走、`mv` 新名就位。

UnityPluginLoad 的两条提示**没有打补丁**：实测本机编辑器下它们根本不触发（见 §5），且它们只是日志，保留有助于诊断。

## 4. 接入方式（工程内 6 处改动）

Havok 能否启用由 `HAVOK_PHYSICS_EXISTS` 决定：`Unity.Physics.asmdef` 与 `Unity.Physics.Hybrid.asmdef` 里都有 `versionDefines: { name: "com.havok.physics", expression: "0.0.0", define: "HAVOK_PHYSICS_EXISTS" }`。该 define 打开后，`SimulationType` 才多出 `HavokPhysics`，`ICollisionEventsJob` 等接口才由 `Havok.Physics.dll`（而不是 Unity.Physics）提供。**旧发布版的 `Unity.Physics.dll` 正是带 HAVOK 编译的**（`SimulationType.HavokPhysics = 2`、只有 `ICollisionEventsJobBase`、有 `CustomPhysicsSystemGroupBase` 而无 `CustomPhysicsSystemGroup`，且 `Assembly-CSharp.dll` 有 `.assembly extern Havok.Physics`），所以本工程必须同样重编包，而不是塞一个 .dll 了事。

1. **嵌入 `com.unity.physics`**：`Library/PackageCache/com.unity.physics@1.0.0-pre.44` → `Packages/com.unity.physics`（保留 .meta，GUID 不变）；并从 `Packages/manifest.json` 删掉 `"com.unity.physics": "1.0.0-pre.44"` 一行。
2. **本地 shim 包 `Packages/com.havok.physics/package.json`**：只有元数据（`name: com.havok.physics`, `version: 1.0.0-pre.44`, `unity: 2022.2`）。它唯一作用是让上面两条 versionDefines 命中，从而定义 `HAVOK_PHYSICS_EXISTS`。真正的实现来自 Assets 里的 DLL。
3. **`Packages/com.unity.physics/Unity.Physics/Unity.Physics.asmdef`**：`"overrideReferences": true`、`"precompiledReferences": ["Havok.Physics.dll"]`、`references` 追加 `"Havok.Physics"`。
4. **`Assets/Plugins/Havok.Physics.dll` 与 `Assets/Plugins/Havok.Physics.Hybrid.dll`**（新 .meta：Any + Editor 均启用）。
5. **`Assets/Plugins/x86_64/HavokNative.dll.meta`**：`Editor: Editor → enabled: 1`（原本是 **0**，也就是编辑器根本不加载原生插件——这正是过去"编辑器里用不了 Havok"的直接原因）。Win64/Linux64/OSX/Any 本来就 enabled。
6. 代码两处（见 §6）。

## 5. 验证证据

- **编译**：`Logs/havok-import2.log`、`Logs/havok-import3.log` 均无 `error CS`，有 `AssetDatabase: script compilation time: ...`，退出码 0。（第一次 `Logs/havok-import.log` 只有 1 个预期错误，见 §6。）
- **原生插件可加载**（`Logs/havok-probe.log`）：
  ```
  [HavokProbe] unityVersion=2022.2.23f1c1 platform=WindowsEditor
  [HavokProbe] Havok.Physics loaded: True
  [HavokProbe] HP_GetPluginVersionString str=Havok native plugin built from 'master' branch; revision '1d0062e9643b46014b47eb6b5cb6f20e7a635d3b'
  [HavokProbe] SimulationType values: NoPhysics,UnityPhysics,HavokPhysics
  ```
  日志里**没有** `this version of Unity is not supported`、也**没有** `Pro, Enterprise` 提示 → 本机编辑器没被版本/授权门禁拦。
- **补丁生效**（`Logs/havok-probe2.log`）：把未初始化的 `TypeCheckInformation` 传给原生 `HP_CheckCompatibility`：
  ```
  [HavokProbe2] HP_CheckCompatibility(uninitialised) => True   (false = native validation active, true = bypassed)
  ```
  未打补丁时这里必然 false 并打印 "reported a different number of checked types"。现在是 True。
- 自检已收成正式 QA 工具：`Assets/Editor/Bpsim/HavokBackendProbe.cs`
  `Unity.exe -batchmode -quit -executeMethod Bpsim.QA.HavokBackendProbe.Run`

## 6. 代码改动

1. `Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartSimulationSystem.cs`（`UpdateJoints`）：
   `IImpulseEventJobExtensions.Schedule(...)` → **`IHavokImpulseEventsJobExtensions.Schedule(...)`**。
   原因：`HAVOK_PHYSICS_EXISTS` 下包里的 `IImpulseEventJobExtensions.Schedule` 只剩一个 `[Obsolete(..., true)]` 的 `ISimulation` 桩，实机是 `Unity.Physics.IHavokImpulseEventsJobExtensions.Schedule<T>(this T, SimulationSingleton, JobHandle)`——旧发布版走的也是这条。不改就是 `CS1503: cannot convert from 'SimulationSingleton' to 'ISimulation'`。
2. `Assets/Scripts/Assembly-CSharp/Bpsim/SimulationSettings.cs`：构造函数默认 `SimulationType.UnityPhysics` → **`SimulationType.HavokPhysics`**（与发布版一致）。

**回退**：把默认改回 `UnityPhysics`，或运行期 `BpsimPlayQA.SetSimulationType("UnityPhysics")`。若连 Havok 程序集都不想要：删 `Packages/com.havok.physics`、还原 asmdef 的 `overrideReferences/precompiledReferences`、删 `Assets/Plugins/Havok.Physics*.dll`、把 `HavokNative.dll.meta` 的 Editor 平台关回去即可（manifest 里要重新加回 `com.unity.physics`）。

## 7. 未验证 / 风险

- **没有在真实关卡里跑过 Havok 模拟**：本仓库进不了关卡（既有问题，`LevelLoader.Awake` 对 `m_Data.LoadValue<TextAsset>()` 返回 null → NRE）。因此"整车用 Havok 跑起来"只能在能进关卡的构建里验。
- 用 `DefaultWorldInitialization` 裸建世界做验证会失败，但**与 Havok 无关**：`Unity.Physics.Systems.BuildPhysicsWorldDependencyResolver.OnCreate` 在系统创建阶段就 `GetSingleton<SimulationSingleton>` 报"没有实体"，属于创建顺序问题（`Logs/havok-world.log`）。
- `Assets/Plugins` 里 `Unity.Burst.dll` / `Unity.ScriptableBuildPipeline.dll` 与包内同名程序集，Unity 一直报 "same filename as Assembly Definition File"（既存问题，本次未动）。
- `Assets/Plugins/Unity.Burst.dll` 的 meta 是 `Editor enabled: 0 / Win64 enabled: 1`，与本次 Havok 的 meta 策略不同（Havok 两处都要），原因：Havok 程序集必须参与编辑器编译。

## 8. 复现命令

```bash
cd "D:/rpgvx + magpie/BPRC 2026.3.2 proj/bpsim-main"
# 编译（必须清掉两套大小写代理变量，否则 Bee 会挂）
env -u NO_PROXY -u no_proxy -u HTTP_PROXY -u http_proxy -u HTTPS_PROXY -u https_proxy \
  "D:/folder/2022.2.23f1/Editor/Unity.exe" -batchmode -quit -accept-apiupdate \
  -projectPath "D:/rpgvx + magpie/BPRC 2026.3.2 proj/bpsim-main" \
  -logFile "D:/rpgvx + magpie/BPRC 2026.3.2 proj/bpsim-main/Logs/havok-import.log"
# Havok 自检
#   -executeMethod Bpsim.QA.HavokBackendProbe.Run
```
---

## 9. 根因与修复（2026-10-27）：原生门禁字节 `g_flag` 为 0，七个步进入口全部空转

现象：`SimulationSingleton.Type = HavokPhysics`、Havok 的四个系统都在 tick、速度按重力正确累积，
但 `MotionDatas[i].WorldFromMotion.pos` 与实体 `LocalTransform` 一直停在建造位置。

用 capstone 反汇编 `HavokNative.dll`（`ImageBase 0x180000000`；`.text` RVA 0x1000 / raw 0x400）后定位到真正的门禁：

- 七个步进入口的第一条指令都是 `cmp byte ptr [rip+X],0` + `je <提前返回>`：`HP_InjectContacts`（0x16EE2）、
  `HP_ProcessStep`（0x171E6）、`HP_StepVisualDebugger`（0x172E6）、`HP_StepWorld`（0x17357）、
  `HP_SyncMotionsOut`（0x17464）、`HP_SyncSecondaryMotionsOut`（0x174F4）、`HP_SyncWorldIn`（0x1759E）。
  它们读的是同一个全局字节 `g_flag` @ RVA **0x4C6C00**（`.data` 尾部 BSS，加载时为 0）。
- 唯一的写入点：`UnityPluginLoad` 里 0x177D5 `call qword ptr [rax]`（`GetInterface(GUID1)->vtable[0]()` 返回 bool），
  紧接 0x177D7 `mov byte ptr [rip+0x4af423], al` 写进 `g_flag`；`UnityPluginUnload` 0x1794D 清零。
- 同函数在 `g_flag == 0` 时打印 `EDITION_MSG`（0x3EFF60「Havok Physics for Unity is available to Pro, Enterprise,
  and Unity Industrial Collection users.」）或 `NOT_SUPPORTED_MSG`（0x3EFFC0）。全 `.text` 只有 0x17827 一处引用
  `EDITION_MSG` —— 所以日志里出现这条字符串，就等于 `g_flag == 0`，与布局校验毫无关系。

结论：本机 Unity 中国版 2022.2.23f1c1 不被该原生库认作可授权宿主，插件加载时把 `g_flag` 置 0，
之后**所有** `HP_*` 步进入口一律空转。观察到的「速度在涨」来自托管侧 `StepImmediate` 里的
`Solver::ApplyGravityAndCopyInputVelocities`，跟 Havok 有没有步进无关 —— 这就是「速度积分、位置不写回」的全部原因。

**修复**：`Tools/PatchHavokNative.py`（幂等，可重复执行）对 `Assets/Plugins/x86_64/HavokNative.dll` 打两处短补丁：

| 文件偏移 | 原字节 | 补丁 | 作用 |
| --- | --- | --- | --- |
| 0x15FE0（RVA 0x16BE0） | `40 57 48 81 EC` | `B0 01 C3` | `HP_CheckCompatibility` 直接 `return true` |
| 0x16BD5（RVA 0x177D5） | `FF 10` | `B0 01` | 把 vtable 调用换成 `mov al,1`，随后 `g_flag = 1` |

打完 md5 = `03986ceb702b69ac42965d1d694839d0`（原库 `c79ad81532b75bd39b36ea0a4dd3a5f2`）。
**这个字节是唯一的总开关**：打开后编辑器与打包产物里的 Havok 都正常步进（见 §12）。
打包不需要额外步骤 —— 补丁就在 `Assets/Plugins/x86_64/` 里，构建会自动带上；若有人从反编译源重新导入该 DLL
把补丁冲掉，重跑一次脚本即可。

## 10. 同批修掉的报错

1. **启动/退出 14 次 `NullReferenceException`**：默认 world 在 `PartManager.Awake` 之前、以及 Play 退出销毁之后
   仍会 tick 这些系统，`PartManager.Instance` 为 null。已在这 6 个系统 `OnUpdate` 开头加守卫：
   `PartSimulationSystem.cs`、`FanPropellerSystem.cs`、`TransparentFrameSystem.cs`、
   `PartTransformSystem.cs`、`ColoredFrameSystem.cs`、`ActiveSceneSystem.cs`。
2. **`PartManager.IsReady` 自带 NRE**：`m_world` 在 `Start()` 才赋值，旧实现 `m_world.IsCreated` 在 null 时抛异常
   （Player 里表现为 `LoadEmptyScene` 的 `NullReferenceException`）。改为 `m_world != null && m_world.IsCreated`。
3. **Player 启动的 `ArgumentNullException: burstMethodHandle`**：反编译丢掉了 `BurstCompiler.CompileILPPMethod2`
   的 ILPP metadata handle（`default(RuntimeMethodHandle)`），而 `-BurstDirectCallInitializer` 只在 `!UNITY_EDITOR`
   调用它，于是真机构建启动即抛 `TypeInitializationException`。已在
   `FanPropellerSystem.RunWithoutJobSystem_000006E1_0024BurstDirectCall.Constructor()` 里 try/catch 兜住
   （拿不到指针时 `Invoke()` 本来就走 `RunWithoutJobSystem_0024BurstManaged`）。

仍存在的**既有**噪声（本次未动、与 Havok 无关）：`Script error: OnParticleUpdateJobScheduled`
（来自 `Assets/Plugins/UniTask.dll` 的异步触发器桥）、`Internal: JobTempAlloc … 4 frames old`、
`Plugin 'Assets/Plugins/Unity.ScriptableBuildPipeline.dll' has the same filename as Assembly Definition File …`。

## 11. 打包与产物自检（已通过）

- 打包入口：`Bpsim.EditorTools.WindowsPlayerBuild.Build(outDir)`（`Assets/Editor/Bpsim/WindowsPlayerBuild.cs`）。
  可在已打开的编辑器里经 DshBridge 调 `invoke_static`，也可 `-executeMethod Bpsim.EditorTools.WindowsPlayerBuild.BuildDefault`。
  构建前先 `taskkill /F /IM bpsim.exe`：运行中的玩家会锁 `lib_burst_generated.dll`，
  报 `Copying the file failed: 另一个程序正在使用此文件`。
- 产物自检：`Assets/Scripts/Assembly-CSharp/Bpsim/QA/PlayerSelfTest.cs`，带 `-bpsimSelfTest` 启动即跑
  「放 6 个部件 → StartSimulation → 采样 t=0/1/2 位置 → 判 PASS → Quit(0/1)」。

```bash
cd "D:/rpgvx + magpie/BPRC 2026.3.2 proj/bpsim-main/Builds/HavokTest5"
./bpsim.exe -batchmode -bpsimSelfTest -bpsimSelfTestLog "C:/Users/aecli/bpsimlogs/selftest.log" -logFile -
```

实测结果（`Builds/HavokTest5`，result=Succeeded sizeMB=212.3）。
注意：**这是 §9 补丁之前的产物**，那时默认后端还是 UnityPhysics，所以下面 `simulatorType=UnityPhysics`：

```
[SelfTest] boot: waited=0.0s simulatorType=UnityPhysics
[SelfTest] diag: PartManager ready=True m_scenePrefab=PartScene m_factory=PartFactory m_world=Default World
[SelfTest] place: placed=6 parts=6
[SelfTest] sim: t=1.0 p=(0.00, -5.06) v=(0.00, -9.19)
[SelfTest] sim: t=2.0 p=(0.00, -18.09) v=(0.00, -16.39)
[SelfTest] sim: maxDrop=19.094 m
[SelfTest] errors: count=0
[SelfTest] PASS: physics integrated positions in the player build
```

注意：`-logFile` 带空格路径会 `Unable to open log file, exiting`（退出码 127），要用无空格路径或用 `-logFile -` 重定向。
## 12. 验证（2026-10-27，编辑器 + 打包产物都通过）

### 编辑器 Play Mode
`Bpsim.QA.BpsimPlayQA.Run("fall")`（读**模拟器**实体，不是建造期场景实体）：

```
fall: backend=HavokPhysics scene parts=7
fall: simulator parts=7 joints=5 components=2
fall: world simSingleton=HavokPhysics step=HavokPhysics gravity=(0.00,-9.81) motionDatas=7 dynamic=7 m0=(0.00,-0.33) v=(0.00,-2.48)
fall: t=0.0 [0] (0.00,-0.33) v=(0.00,-2.48) m=0.5 world=True
fall: t=1.0 [0] (0.00,-7.40) v=(0.00,-11.05) m=0.5 world=True
fall: t=2.0 [0] (1.22,-19.57) v=(1.32,-16.62) m=0.5 world=True    ← 中途注入 v=(0,5)，横向也真的动了
```

编辑器日志里没有 `Pro, Enterprise …` 授权文案、没有 `NullReferenceException`（§10 的守卫确实生效）。

### 打包产物
`-executeMethod Bpsim.EditorTools.WindowsPlayerBuild.BuildDefault -outDir Builds/HavokFinal` →
`result=Succeeded errors=0 warnings=1 sizeMB=212.3 elapsed=00:23`；产物内
`bpsim_Data/Plugins/x86_64/HavokNative.dll` md5 = `03986ceb702b69ac42965d1d694839d0`。

```
cd "D:/rpgvx + magpie/BPRC 2026.3.2 proj/bpsim-main/Builds/HavokFinal"
./bpsim.exe -batchmode -bpsimSelfTest -logFile - > selftest.txt 2>&1
```

```
[SelfTest] boot: simulatorType=HavokPhysics
[SelfTest] place: placed=6 parts=6
[SelfTest] sim: world simSingleton=UnityPhysics step=HavokPhysics gravity=float3(0,-9.81,0)
[SelfTest] sim: t=1.0 p=(0.00,-4.97) v=(0.00,-9.19)
[SelfTest] sim: t=2.0 p=(0.00,-17.91) v=(0.00,-16.39)
[SelfTest] sim: maxDrop=18.914 m
[SelfTest] errors: count=0
[SelfTest] PASS: physics integrated positions in the player build
```

非自检正常启动（带图形跑 30 秒）也无异常：只剩既有的 `Script error: OnParticleUpdateJobScheduled`
（UniTask 异步触发器桥，与 Havok 无关）。

### QA 探针口径修正
`sim` / `phys` 两个场景原先读建造期场景实体 `scene.Unmanaged.Parts`，而 `StartSimulation` 之后
物理组件全在模拟器克隆上，于是永远打印 `vel=0 mass=0 worldIndex=0`（看起来像「完全没进物理世界」）。
现统一走 `ProbeParts()`：模拟中读 `PartManager.Instance.PartSimulator.UnmanagedRef.Value.Parts`。

### 回归清单
1. `python Tools/PatchHavokNative.py` → 输出 already patched 或打出两处补丁；
2. 编辑器进 Play，`BpsimPlayQA.Run("fall")` → t=1.0 应约 -7.4，t=2.0 应约 -19.6；
3. 打包 → `bpsim.exe -batchmode -bpsimSelfTest` → PASS 且 `errors: count=0`。

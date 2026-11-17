# bpsim-main 打包失败修复：Bee 环境变量大小写重复

## 症状

点 **Build** / 走 `-executeMethod Bpsim.EditorTools.AndroidBuild.Build` 打包时，
**1 秒内就失败**，而且报的是编译错误，但仓库里根本没有 `error CS`：

```
Internal build system error. BuildProgram exited with code -532462766.
Unhandled exception. System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.
 ---> System.ArgumentException: Item has already been added. Key in dictionary: 'no_proxy'  Key being added: 'NO_PROXY'
   at System.Collections.Hashtable.Insert(Object key, Object nvalue, Boolean add)
   at System.Collections.Hashtable..ctor(IDictionary d, Single loadFactor, IEqualityComparer equalityComparer)
   at Bee.Stevedore.Program.EnvironmentPaths..ctor(IDictionary environment)
   at Bee.Stevedore.Program.StevedoreConfig.ReadEnvironmentDefaults(Action`1 reportParseError)
   at Bee.Core.Stevedore.StevedoreUnpacker..ctor(Backend backend)
   at Bee.Core.BuildProgramContext..ctor(String[] args, String buildTitle)
   at ScriptCompilationBuildProgram.ScriptCompilationBuildProgram.Main(String[] args)
```

随后 Unity 把这次后端崩溃**误报**成：

```
DisplayProgressNotification: Build Failed
Error building Player because scripts have compile errors in the editor
Build completed with a result of 'Unknown' in 0 seconds (0 ms)
```

关键点：**C# 编译器根本没跑过**。上面那句 "compile errors" 是 Bee 后端启动失败的
替罪羊消息，所以去翻脚本、删代码、清 `Library/` 都是白费力气。

## 根因

Unity 的 Bee 后端（`ScriptCompilationBuildProgram.exe`）启动时会把**整个进程环境块**
拷进一个**大小写不敏感**的 `Hashtable`（`Bee.Stevedore.Program.EnvironmentPaths..ctor`）。
Windows 允许同一个进程环境块里**同时存在两种拼写**的变量，而字典不允许。

本机（DSH / 各种 launcher 注入的环境）实际带着三组重复：

```
NO_PROXY  / no_proxy
HTTP_PROXY / http_proxy
HTTPS_PROXY / https_proxy
```

于是 `Hashtable.Add` 抛 `ArgumentException`，后端在**编译任何代码之前**就退出。

**只有走 Bee 的路径会挂**：编辑器里日常改脚本、进 Play Mode 用的是增量/缓存路径，能正常跑；
一旦触发真正的 script compilation（切平台、player build、`RequestScriptCompilation`），才撞上。

## 对照实验（同一份 DAG，只改环境）

直接调用那个后端程序，输入完全相同的 DAG：

| 环境 | 命令 | 结果 |
| --- | --- | --- |
| 带 `NO_PROXY` + `no_proxy` | `netcorerun ScriptCompilationBuildProgram.exe 1900b0aE.dag.json ...` | `ExitCode: -532462766`（崩） |
| 清掉重复拼写后 | 同上（DAG、入参一模一样） | `exit=0`（成功） |

即：**只由环境变量的拼写重复决定成败**，与工程代码、DAG、缓存都无关。

## 修复

新增 `Assets/Editor/Bpsim/BeeEnvironment.cs`：把每组**仅大小写不同**的变量收敛成一个。

- `[InitializeOnLoadMethod]` 在编辑器加载时自动跑一次 —— 覆盖 DSH 直接拉起编辑器（不经
  `Tools\unity-batch-bpsim.bat`）的情况。
- 保留大写拼写（Windows 惯例），值不变；Windows 环境查找本来就忽略大小写，语义无损。
- 公开 `CollapseCaseDuplicates()`，可重复调用，环境干净时是空操作。

并接入各个打包入口，保证即使环境在运行期被重新污染也安全：

| 入口 | 位置 |
| --- | --- |
| `AndroidBuild.Build()`（命令行/菜单） | `Assets/Editor/Bpsim/AndroidBuild.cs` |
| Build Settings 默认构建回调 | `Assets/Editor/Bpsim/AndroidBuildEnvironment.cs` |
| `WindowsPlayerBuild.Build()` | `Assets/Editor/Bpsim/WindowsPlayerBuild.cs` |
| `ShaderVariantProbe.StartBuild()` | `Assets/Editor/Bpsim/ShaderVariantProbe.cs` |

> 注意：`Tools\unity-batch.bat` / `unity-batch-bpsim.bat` 里那行 `set no_proxy=` 只清掉
> **某一个拼写**，而且只对经由该 `.bat` 启动的进程生效。GUI 里点 Build、或被 DSH 直接
> 拉起的编辑器都不受它保护 —— 这才是同一个 bug 反复出现的原因。

## 验证

环境本身带重复拼写（`NO_PROXY` + `no_proxy`）时，**直接**启动编辑器（故意绕过 `.bat`），
让 `BeeEnvironment` 自己收敛：

```
[BeeEnvironment] collapsed case-duplicate environment variables:
  NO_PROXY/no_proxy -> NO_PROXY, http_proxy/HTTP_PROXY -> HTTP_PROXY, https_proxy/HTTPS_PROXY -> HTTPS_PROXY
```

在同一进程里跑真实打包：

```
Build Finished, Result: Success.
[AndroidBuild] result=Succeeded size=241120808 bytes time=50.1s steps=0 errors/0 warnings
```

- `532462766` 崩溃次数：**0**
- `error CS` 次数：**0**
- 产物 `Builds/Android/bpsimF_android.apk`：463 个 zip 条目，含 `classes.dex`、
  `lib/armeabi-v7a/libmono*.so`、`libunity.so`、`assets/bin/Data`

另外命令行路径（`unity-batch-bpsim.bat -executeMethod ...Build`）连跑两次：0 个 CS 错误，
`Succeeded`（57.9s / 102.5s）。

> 首次从空 `Library` 缓存打包时可能先出现一轮 `CS0246`（`ModSettingSpec`、`UiPalette`
> 尚未导入），Unity 重试后自愈 —— 那是首次导入顺序问题，与本文的 Bee 崩溃无关。

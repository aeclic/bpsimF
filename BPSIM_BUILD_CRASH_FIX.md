# bpsim-main 打包崩溃修复：`m_DefaultShaderChunkSizeInMB: 0`

## 症状

在 Unity 里 Build Windows 玩家（或 `-buildWindows64Player`）时，**编辑器自己崩溃退出**，没有任何 `error CS` / 打包错误提示。日志末尾固定是：

```
Compiling shader "Hidden/Universal Render Pipeline/FallbackError" pass "" (fp)
    Prepared data for serialisation in 0.00s
=================================================================
	Native Crash Reporting
```

崩溃栈（`%TEMP%\Unity\Editor\Crashes` 里的 .dmp 解析结果一致）：

```
0x...  (Unity) SubProgramBlobWriter::Flush
0x...  (Unity) CompileShaderSubprograms
0x...  (Unity) Shader::Transfer<StreamedBinaryWrite>
0x...  (Unity) SerializedFile::WriteObject
0x...  (Unity) PersistentManager::WriteFile
0x...  (Unity) WriteSharedAssetFile
0x...  (Unity) CompileSharedAssetsFile
0x...  (Unity) BuildPlayerData
```

异常码 `0xC0000094` = **STATUS_INTEGER_DIVIDE_BY_ZERO**（整数除零）。

崩溃点与具体着色器内容无关——它总是落在"写出第一个需要现场编译的 shader 的子程序 blob"那一刻，所以看起来像是 `FallbackError` 坏了，其实是所有 shader 都会崩。

## 根因

`ProjectSettings/ProjectSettings.asset` 里

```yaml
m_DefaultShaderChunkSizeInMB: 0     # ← 错误值（Unity 新工程默认 16）
m_DefaultShaderChunkCount: 0        # 这个 0 是正常的（= 自动）
```

打包时 Unity 要把每个 shader 的编译产物切成"shader 分块"写进 sharedassets，
分块大小取自 `m_DefaultShaderChunkSizeInMB`。值为 0 时
`SubProgramBlobWriter::Flush()` 里 `(blobSize + chunkSize - 1) / chunkSize`
直接整数除零 → 进程被 `RaiseException(0xC0000094)` 干掉。

编辑器里预览/编译不受影响（不走这条分块写盘路径），所以"编辑器里一切正常、一打包就崩"。

这个 0 是从反编译得到的原版工程设置里带过来的；本仓库根目录的 BPLE 主工程
（能正常打包）同字段是 `16`，可作对照。

## 对照实验（最小复现，与 bpsim-main 无关）

用同版本编辑器（`2022.2.23f1c1`）新建空工程 `D:/temp/chunkprobe`，一个默认场景 + 空 `Probe.Build` 脚本：

| 设置 | 结果 |
| --- | --- |
| 新建工程默认 `m_DefaultShaderChunkSizeInMB: 16` | 打包成功（`BuildPipeline.BuildPlayer` 返回 Succeeded） |
| 只把该值改成 `0`，其余不动 | **同一栈崩溃**：`SubProgramBlobWriter::Flush` → `CompileShaderSubprograms` → `WriteSharedAssetFile` |

即：单一个字段就能 100% 复现/消除该崩溃。

## 修复

```yaml
m_DefaultShaderChunkSizeInMB: 16
```

（在 `ProjectSettings/ProjectSettings.asset`；必须在编辑器**关闭**时改，否则编辑器保存设置时可能写回。
等价做法：编辑器里 Project Settings → Player → 相关分块设置，或
`PlayerSettings` 脚本里设置后保存工程。）

改完 `-buildWindows64Player` 打包：`FallbackError` 通过、继续编译后续 shader 变体、不再崩溃。

## 注意 / 防回归

- 不要把这个字段写成 0 或负数；Unity 新建工程默认 16，主工程 BPLE 也是 16。
- 如果有人从反编译工程重新导入 `ProjectSettings`（例如整体替换 `ProjectSettings.asset`），
  这个 0 会回来，打包会再次以"编辑器无声崩溃"的形式失败。症状即上面的栈。
- 遇到"打包时编辑器崩溃、无 C# 报错、栈里有 `SubProgramBlobWriter::Flush`"，
  先查这个字段，别去怀疑某个具体 shader。
- `Library/ShaderCache` 与本次问题无关（清空缓存不解决问题，已实测）。

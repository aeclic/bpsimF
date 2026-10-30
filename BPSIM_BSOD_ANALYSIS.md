# bpsim 项目与蓝屏问题核查（2026-10-21 23:2x）

## 结论（先看这三条）

1. **项目里确实有一个真实的 GPU 稳定性缺陷，已找到并修复。** 706 会话定位到 `SceneRenderSystem` 每帧对同一个 1 MB `GraphicsBuffer` 做 6 次 `LockBufferForWrite/UnlockBufferAfterWrite`，D3D11 报 `multiple uploads in flight ... Falling back to slow path`，反复创建 staging resource，最终 `device removed`。修复后同一条日志路径 0 次、16 个部件 QA 全部通过。这能解释“Play 久了编辑器崩溃/显卡驱动重置”的一部分现象。
2. **但今天两次 0x3B 蓝屏的签名不像 Unity/C# 用户态代码，而像第三方内核驱动。** 两次故障指令地址 `fffff80569aa4cf2` 与 `fffff80640b74cf2` 的低 12 位完全相同（`4cf2`），基址差恰好是 `0xD70D0000`（ASLR 随机基址差），说明是**同一个内核模块的同一条指令**。C# 代码越界只会抛 `NullReferenceException`/崩溃进程，不会让 Windows 蓝屏；本项目也没有任何 `.sys` 或内核驱动。
3. **现有转储全部丢失或被 SYSTEM ACL 锁住，无法直接点名模块。** `C:\Windows\Minidump` 为空、`MEMORY.DMP` 不存在，WER 里两个 `Kernel_3b_*` 目录当前用户不可读。需要管理员权限提取（脚本已备好，见文末）。在拿到模块名之前，不应把蓝屏归因于 bpsim。

## 蓝屏证据（系统日志 `Microsoft-Windows-WER-SystemErrorReporting` 事件 1001）

| 日志时间（重启后写入） | BugCheck | 参数 | 转储文件名 |
|---|---|---|---|
| 2026-07-27 15:36:50 | `0x0A` IRQL_NOT_LESS_OR_EQUAL | `0x11c, 0x2, 0, fffff80468c41814` | `072726-9687-01.dmp` |
| 2026-08-08 23:48:13 | `0x20001` **HYPERVISOR_ERROR** | `0x26, 0, fffff8036c0d0a8b, fffff6044402f030` | `080826-8796-01.dmp` |
| 2026-08-22 21:45:07 | `0x0A` | `0x11c, 0x2, 0, fffff80457c41814` | `082226-9140-01.dmp` |
| 2026-08-24 21:05:33 | `0x20001` **HYPERVISOR_ERROR** | `0x26, 0, fffff807236d0a8b, ffffe207af79f030` | `082426-10031-01.dmp` |
| 2026-08-30 01:33:00 | `0x20001` **HYPERVISOR_ERROR** | `0x26, 0, fffff80752ad0a8b, fffffef06147cf030` | `083026-9468-01.dmp` |
| 2026-10-21 19:31:57 | `0x3B` SYSTEM_SERVICE_EXCEPTION | `c0000005, fffff80569aa4cf2, fffff858420d86820, 0` | `102126-9828-01.dmp` |
| 2026-10-21 22:58:27 | `0x3B` SYSTEM_SERVICE_EXCEPTION | `c0000005, fffff80640b74cf2, ffffe206e726e820, 0` | `102126-9078-01.dmp` |

要点：

- **三次 `0x20001` 就是 `HYPERVISOR_ERROR`。** 这台机器同时装了 Hyper-V/Docker、雷电模拟器（`Ld9BoxSup.sys`，VirtualBox 6.1.50 内核驱动）和 VMware（`vmx86.sys 16.0.0.16`，而且还在 Auto-start）。三条虚拟化栈共存是 `HYPERVISOR_ERROR` 的头号嫌疑。
- 两次 `0x0A` 的 P4 都是 `fffff804...c41814`（同一驱动偏移的两次不同基址）；两次今天的 `0x3B` 也是同一模块偏移。说明这些不是随机内存故障，而是**固定的第三方驱动代码路径**（WHEA、磁盘/NVMe、NTFS、TDR 记录均为 0，内存诊断通过）。
- 今天实际崩溃时间约 **19:07** 和 **22:57**（`Kernel-Power 41` 在 22:58:20 记录；编辑器日志 `Editor-prev.log` 最后一行是 22:56:59 的 QA 完成）。第二次崩溃发生在 QA 结束、编辑器已经空闲之后，**不是渲染风暴进行中**——这进一步说明内核崩溃与项目当前负载没有直接同步关系。
- 20:51 / 21:05 的 `Unity.exe` 崩溃是 `0x40000015`（`STATUS_FATAL_APP_EXIT`，KERNELBASE 里的 abort），属于进程级崩溃，不是蓝屏；那是修复前渲染问题造成的编辑器退出。

## 项目侧已修复的缺陷

文件：`Assets/Scripts/Assembly-CSharp/Bpsim/Parts/SceneRenderSystem.cs`

- 旧：`PopulateBuffer` 每帧多次 `LockBufferForWrite` / `UnlockBufferAfterWrite`（同一 1 MB buffer），D3D11 日志 `multiple uploads in flight ... size 1048576. Falling back to slow path`。旧日志 `Logs/bpsim-editor2.log` 有 **560,396** 条，约 490 条/秒。
- 旧：batch 不 `RemoveBatch`，且指向已释放 buffer。
- 新：每帧只做一次 lock/unlock（`InitialBufferSize = 65536`，按实例数扩容），batch 正确释放，`OnDestroy` 清理。
- 复核：修复后的 `Editor-prev.log` / 当前 `Editor.log` 中 `multiple uploads in flight` 为 **0**，D3D11 device removed 为 **0**；`Logs/bpsim-gameplayqa.log` 16/16 PASS（含本轮新增的 wing 升力）。

## 为什么不能从项目侧彻底修好

- `0x3B` / `0x20001` 是内核态 bugcheck；项目代码没有、也不应该有能力直接触发内核异常。
- 能做的只有：降低 GPU 驱动压力（已做）、避免在项目里制造长时间 CPU/GPU 满载，以及把崩溃现场保留下来供驱动厂商定位。
- 真正防止蓝屏要处理驱动/虚拟化冲突，属于系统层操作，需要管理员权限，当前会话的审批策略不允许自行提权。

## 建议的下一步（按优先级）

1. **以管理员身份运行** `D:\bsod-triage\Run-BsodTriage.cmd`。它会把 WER 里两个 `Kernel_3b_*` 目录（SYSTEM ACL）和 Minidump 复制到 `D:\bsod-triage\dumps`，再用 `mdmp.py` 把故障地址映射到模块。跑完把输出贴给我，就能确认 `...4cf2` 是哪个 `.sys`。
2. **虚拟化冲突（对应三次 HYPERVISOR_ERROR）**：不用雷电模拟器时卸载它或至少停用 `Ld9BoxSup.sys`；VMware 的 `vmx86.sys` 是 16.0.0.16 孤儿驱动，已在 23:34 一并停用（需要 VMware 时用 `Enable-SuspectDrivers.ps1` 恢复）；Hyper-V/Docker 与它们二选一。
3. **安全软件/反作弊驱动**：`ACE-GAME.sys`、`ACE-ADVT.sys`（腾讯，10-14 更新）、`OWNeacSafe.sys`（网易 15.7 MB）、`WeGameDriver*.sys`、`gameflt.sys`、火绒 `sysdiag` 6.0.11.1（2023 年构建）。这些是 `0x0A/0x3B` 的常见来源。**2026-10-21 23:34 已按要求停用 ACE 系 9 个服务 + `vmx86` + `WeGameProcService`，共 12 个内核服务**（备份 `D:\bsod-triage\driver-backup-20261021-233405.json`，日志 `driver-disable.log`；恢复用 `Enable-SuspectDrivers.ps1`）。火绒至少升级到最新版，测试期可临时退出。
4. **显卡驱动**：当前 NVIDIA 566.24（2024-11-22），建议清洁安装最新 Game Ready/Studio 驱动；这也能降低 device removed 概率。
5. **平台/固件**：i5-13500H + 2023 年 BIOS `M2CN25WW`，更新联想 BIOS（含 13 代微码修复）；C: 只剩 15.3 GB，清理到 30 GB 以上，避免转储/更新失败。
6. **保留现场**：以管理员开启完整/自动转储并保留：
   ```bat
   reg add "HKLM\SYSTEM\CurrentControlSet\Control\CrashControl" /v AlwaysKeepMemoryDump /t REG_DWORD /d 1 /f
   reg add "HKLM\SYSTEM\CurrentControlSet\Control\CrashControl" /v CrashDumpEnabled /t REG_DWORD /d 7 /f
   ```
   再蓝屏时 `C:\Windows\MEMORY.DMP` 会保留，可以直接 `mdmp.py` 分析。
7. 若复现时想快速排除项目：退出 Unity 后做同样时长的 GPU 负载测试（如 FurMark / 3DMark 循环）。如果不在项目里也蓝屏，就是驱动/系统问题；如果只在 Unity Play 时蓝屏，再回来一起看渲染路径。

## 已有的排查工具

| 路径 | 用途 |
|---|---|
| `D:\bsod-triage\Run-BsodTriage.cmd` | UAC 启动器，一键收集证据 |
| `D:\bsod-triage\Get-BsodEvidence.ps1` | 事件日志、驱动列表、转储状态（需管理员） |
| `D:\bsod-triage\preserve-dumps.ps1` | 复制/保留转储 |
| `D:\bsod-triage\mdmp.py` | 无 WinDbg 的 minidump 解析，地址→模块（自测通过） |
| `D:\bsod-triage\REPORT.md` | 本轮之前的完整排查报告 |

## 代码侧检查结论

- 全项目没有 `.sys`、没有 P/Invoke 到内核、没有 `unsafe` 驱动交互；`Assets/Plugins` 只有托管 DLL、`HavokNative.dll`、`Havok.Vdb.dll`。
- 唯一与 GPU 稳定性相关的项目代码就是已修复的 `SceneRenderSystem`；`BpsimPlayQA.cs` 只是反射读取 buffer 做校验，不写 GPU。
- 结论：**bpsim 不是蓝屏的根因，但不能排除它通过高负载/驱动重置间接触发；项目侧该修的已经修了，剩下需要系统层证据。**

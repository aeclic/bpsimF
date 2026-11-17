# dsh-slots —— Unity 多实例测试槽位

给"多 agent / 多会话同时验证 Unity 改动"用的一套**一次性工程副本 + 独立 bridge 端口 + 用完即删**的脚手架。

```
主工作树 bpsim-main（唯一真相源，file-claim 在这里生效）
      │  robocopy 单向镜像（Assets / Packages / ProjectSettings）
      ▼
slot s1 ── slot s2 ── slot s3 …          ← 各自独立的 Library / Temp / Logs
      │            │
      ▼            ▼
 DshBridge(自动端口)  DshBridge(自动端口)   ← 注册表：%LOCALAPPDATA%\DshBridge\instances
      │            │
      ▼            ▼
  测试/截图/PlayMode   测试/截图/PlayMode   ← 客户端用 project 身份校验，绝不会串工程
```

## 快速开始

```powershell
cd <主工作树>

# 1) 建副本（首次冷启动会导入资源，慢；加 -WithLibrary 可暖启动但多占 ~6G）
powershell -File Tools\dsh-slots\slots.ps1 create -Name s1
# 2) 批处理启动（-batchmode -nographics），直到 bridge 就绪才返回，打印 port/pid/projectId
powershell -File Tools\dsh-slots\slots.ps1 start -Name s1 -Batch -WaitSeconds 1800

# 3) 跑测试（客户端自动发现 s1 的端口）
$env:DSH_UNITY_PROJECT = "D:\rpgvx + magpie\_dsh_slots\s1"
py <技能目录>\client\unity_client.py ping
py <技能目录>\client\unity_client.py playmode '{"action":"enter"}'

# 4) 停 + 删除（默认行为；日志归档到 <root>\logs）
powershell -File Tools\dsh-slots\slots.ps1 stop -Name s1
```

GUI 验证时去掉 `-Batch`（需要别的 agent 看画面时才有意义；同机 GUI 编辑器建议 ≤2 个）。

## 命令

| 命令 | 作用 |
| --- | --- |
| `status` | 列出槽位、端口、pid、存活、占用空间 |
| `instances` | 列出**所有**运行中的 bridge（含主工程），JSON |
| `create -Name X [-WithLibrary] [-Force]` | 从主工作树镜像出副本；改 `companyName` 做 persistentDataPath 隔离 |
| `sync -Name X` | 主工作树 → 槽位增量镜像（改了代码后重跑） |
| `start -Name X [-Batch] [-Nographics] [-RunTests -TestPlatform PlayMode] [-UnityArgs ...]` | 启动并等 bridge 就绪 |
| `stop -Name X [-Keep]` | 停编辑器；默认连副本一起删，`-Keep` 只停 |
| `remove -Name X [-Force]` | 删副本（运行中需 `-Force` 或先 stop） |
| `sweep [-TtlHours N]` | 清理"无活进程且超过 TTL"的孤儿槽位 |

`-LogFile` 每个槽位独立（`<slot>\Logs\editor.log`，`stop` 时归档到 `<root>\logs`），不会和 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 互串。

## 配置 `config.json`

```json
{
  "source": "D:\\...\\bpsim-main",   // 唯一真相源
  "root":   "D:\\...\\_dsh_slots",   // 槽位根目录（绝不能在任何 Assets/ 下）
  "unity":  "D:\\folder\\2022.2.23f1\\Editor\\Unity.exe",
  "ttlHours": 12,                    // sweep 的默认 TTL
  "archiveLogs": true
}
```

## 为什么不会串工程（DshBridge v0.2）

- 每个编辑器实例按工程路径算 `projectId`，从 8790 起扫描空闲端口（`DSH_BRIDGE_PORT` 可强制），绑定用 `ExclusiveAddressUse`，撞端口不可能"假成功"。
- 自述文件三处：`<工程>/Temp/DshBridge/instance.json`、`%LOCALAPPDATA%\DshBridge\instances\<projectId>.json`、`port.txt`；4 秒心跳刷新，正常退出时删除；pid 已死的残留条目启动时自动清理。
- 请求必须带 `X-Dsh-Project`（头）或 `project`（body）且与实例匹配，否则 `409 / projectError`。客户端 `unity_client.py` 首次连接还会拿 `/ping` 的 `project` 再校验一次。
- 同一 `projectPath` 仍只能开一个编辑器（`Temp/UnityLockfile`）——这是 Unity 的硬约束，槽位的意义就是给每个并行任务**不同的 projectPath**。

## 纪律与预算

- **写永远只写主工作树**：槽位是只读镜像 + 临时产物；在槽位里改的东西 `sync` 会被覆盖，别指望回收。需要回收的改动手动抄回主树。
- **一个任务一个槽位**，命名用任务短名（`s1`、`qa-texture`），`stop` 即回收；`sweep` 兜底。
- 磁盘：每槽位 ≈ 源工程非 Library 部分 + 导入缓存（本项目冷启动后约 5–6G，含 Library 复制再 +6G）。D: 目前只剩 ~33G，**同时最多 3–4 个冷槽位**。
- 内存：`-batchmode -nographics` 每实例约 1–1.5G；GUI 每实例 1.5–2G。16G 机器上 GUI 别超过 2 个。
- 槽位里已排除 `.git`、`Temp`、`Logs`、`obj`、`.vs`、`*.dshclaim*`、`*.dshpending`。

## 排障

- **卡在"等待 bridge"**：看 `<slot>\Logs\editor.log`；常见是首次导入没跑完、许可失败、或脚本编译错误。
- **端口变了**：正常。客户端只认 instance.json；`DSH_BRIDGE_PORT` 才是固定端口的唯一手段（同一端口被占时启动会失败并写 `error` 到 instance.json）。
- **编辑器被 kill 后有残留**：`instance.json` 里的 pid 已死 → 客户端忽略、服务启动时清理；`Temp/UnityLockfile` 由 `start` 在确认无进程后删除。
- **`stop` 删不掉目录**：通常是编辑器还没完全退出或文件被占用；脚本会重试，再不行 `remove -Force` / `sweep -Force`。

## 实测数据（本工作区，2026-11-06）

| 项 | 值 |
| --- | --- |
| `create`（Assets 647M + Packages 751M + ProjectSettings，排除 .git/Temp） | 1380 MB，约 1 分钟 |
| 冷启动 `start -Batch` 到 bridge 就绪 | 约 1 分 47 秒（端口自动拿到 8791） |
| 首次导入完成（主线程空闲 `mainThreadStalled:false`） | 全流程约 5 分钟，导入后槽位总占用 2230 MB |
| 域重载（改桥源码后） | 端口保持 8791（`PreviousPort()` 生效），reload 约 10 秒 |
| `stop` | 编辑器进程退出、目录删除、注册表条目清理、日志归档、磁盘回收到 32.4 GB |

- `-WithLibrary` 只在**源编辑器空闲或已关闭**时用（正在写的 Library 可能拷到半写文件）；冷启动 5 分钟一般可接受，复制 5.7G Library 反而更慢。
- batchmode 下 `screenshot` / 窗口类命令不可用；验证画面要用 GUI 实例（去掉 `-Batch`），且 GUI 实例建议 ≤2。


# Coplay MCP for Unity

本项目使用 Coplay 的 Unity MCP Package 与编辑器通信。Unity Package 固定为官方稳定版 `v10.3.0`，Codex 使用项目级 Streamable HTTP 配置。

## 项目配置

- `Packages/manifest.json` 从 OpenUPM 引入并固定 `com.coplaydev.unity-mcp` `10.3.0`。
- `.codex/config.toml` 注册 `unityMCP`，连接 `http://localhost:8080/mcp`。此配置只作用于本项目，不修改用户级 Codex 配置。
- `AGENTS.md` 描述当前 MCP 名称与编辑器状态检查入口。
- `Assets/Editor/DshBridge` 已不是通信层：那 30 个旧方法名现在由本文下方[兼容层](#兼容层旧方法名-新传输)经 Unity MCP 转发，脚本调用方式不变。

## 首次启动

1. 在 Unity 中等待 Package Manager 完成依赖解析。
2. 打开 `Window > MCP for Unity`，按设置向导配置 Python 3.10+ 与 `uv`，然后启动 MCP server。
3. 状态面板显示已连接后，在 Codex 中新开本项目的 chat，使其读取项目级 `unityMCP` 配置。

如果已打开的编辑器仍显示之前 Git URL 安装留下的错误，结束当前 Play Mode 工作后重新打开项目，让 Package Manager 按 OpenUPM 配置重新解析依赖。

不要使用设置向导中的全局 Codex 自动配置选项。项目 `.codex/config.toml` 已连接到本机 HTTP endpoint。

Codex 配置内容如下：

```toml
[features]
rmcp_client = true

[mcp_servers.unityMCP]
url = "http://localhost:8080/mcp"
startup_timeout_sec = 30
tool_timeout_sec = 120
```

## 使用

在 `unityMCP` 工具列表中检查 Unity Editor 状态，再执行所需的资产、场景、脚本、测试或构建操作。Coplay 工具直接连接 Unity Editor。

Coplay 工具文档：[Tool reference](https://coplaydev.github.io/unity-mcp/reference/tools)。

## 兼容层：旧方法名，新传输

旧的 Unity 侧插件（`Assets/Editor/DshBridge/DshBridge.cs`，本机 8790/8791 端口）不再作为通信层。那 30 个方法名与返回结构保留在 Python 侧，改由本目录的以下文件实现：

| 文件 | 职责 |
| --- | --- |
| `unity_mcp.py` | 标准库 Streamable HTTP MCP 客户端：`initialize` → `tools/call`，维持 `mcp-session-id`，解析 SSE；从 `Library/MCPForUnity/RunState/mcp_http_<port>.pid` 发现端点（默认 8080）。 |
| `adapter.py` | 把 30 个旧方法映射到 Coplay 工具。能用类型化工具的就用（`manage_gameobject`/`manage_scene`/`manage_camera`/`read_console` …），语义会被改变的就用 `execute_code`（世界坐标变换、点路径属性、静态反射调用、profiler 实时读取）。 |
| `bridge.py` | 保留原有全部约束：跨进程控制租约、写入隔离（`pending`/acknowledge）、路径与 `.dshclaim` 校验、`invoke_static` 限定 `Bpsim.*`、`quit` 禁用。 |
| `server.py` | FastMCP stdio 适配器（`unity_status`、`unity_read`、`unity_write` …）。 |
| `legacy_client.py` | 一行式客户端，供 `Tools/dsh.py`、`Tools/Brand/ubridge.py`、`Tools/_bridge_build.py` 复用；读方法直接转发，写方法自动取一次短租约。 |

回显与旧实现的差异（这些是 MCP 侧能力所限，不是回归）：

- `console` 的 `time` 恒为 `null` —— `read_console` 不返回时间戳。
- `screenshot` 的分辨率上限由 MCP 决定（实测 640×334），故额外返回 `requested` 与实际 `width`/`height`。
- `hierarchy` 的世界坐标由 `execute_code` 计算，与旧实现一致；`manage_scene get_hierarchy` 本身只给局部坐标、且只返回一层。

验证方式：

```powershell
$env:PYTHONIOENCODING='utf-8'
py Tools/UnityMcp/prove_methods.py     # 逐条实测 30 个旧方法，应为 33/33
py -m unittest test_bridge test_profiler   # 在 Tools/UnityMcp 目录下运行
```

## 旧 DshBridge MCP 代码

`Tools/UnityMcp/server.py` 与其测试保留，但已从 Codex 项目配置移除（Codex 直接使用上面的 `unityMCP`）。`bridge.py` 不再是"直连插件"的客户端——它是兼容层的入口，QA 脚本仍然导入它。

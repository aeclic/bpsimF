# Coplay MCP for Unity

本项目使用 Coplay 的 Unity MCP Package 与编辑器通信。Unity Package 固定为官方稳定版 `v10.3.0`，Codex 使用项目级 Streamable HTTP 配置。

## 项目配置

- `Packages/manifest.json` 从 OpenUPM 引入并固定 `com.coplaydev.unity-mcp` `10.3.0`。
- `.codex/config.toml` 注册 `unityMCP`，连接 `http://localhost:8080/mcp`。此配置只作用于本项目，不修改用户级 Codex 配置。
- `AGENTS.md` 描述当前 MCP 名称与编辑器状态检查入口。
- 原有 `Assets/Editor/DshBridge` 仍供项目内 QA 和历史自动化脚本使用，不再作为 Codex MCP 服务。

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

## 旧 DshBridge MCP 代码

`Tools/UnityMcp/server.py` 与其测试保留为旧实现源码，但已从 Codex 项目配置移除。`Tools/UnityMcp/bridge.py` 仍供项目内直接调用 DshBridge 的 QA 脚本复用。

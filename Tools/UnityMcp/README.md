# BPSim Unity MCP

为 **Unity 2022.2.23f1c1 + Codex + 多 agent** 安装的项目级 MCP 服务。
它使用开源 [MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk) 的
`FastMCP`，将项目已有的 DshBridge HTTP 接口适配为 MCP stdio 工具。
**不是 Coplay/其他 Unity 插件的完整安装，也没有拷贝它们的实现代码。**

## 开源方案参考与本次选择（2026-10-31）

| 方案 | 已核实的要求 | 本项目选择 |
| --- | --- | --- |
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) | v10.0.0 的 package.json 声明 Unity 2021.3；依赖 Newtonsoft JSON 与 Test Framework；支持 Codex | 如果以后需要其完整资产、动画等工具，可另行评估导入。现在避免新增 Unity 包、触发编译或打断并行 agent |
| [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) | 当前 package.json 声明 Unity 2022.3，README 要求 Unity 6；提供 `.codex/config.toml` 项目级接入示例 | 本项目 2022.2 不直接安装当前版 |
| [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) | 当前 README 的 Requirements 明确警告项目路径不能含空格 | 当前项目路径含空格，不直接套用其安装流程 |

本次参考开源方案的 MCP 工具/资源与 Codex 项目配置方式，复用已运行的
`Assets/Editor/DshBridge/DshBridge.cs`。不更改 `Packages/manifest.json`、业务 C#、场景或全局 Codex 配置。
SDK 固定为 `mcp==1.30.0`（1.x API），其余依赖在独立虚拟环境解析。

## 安装与验证

在项目根目录的 PowerShell 执行：

```powershell
& .\Tools\UnityMcp\install.ps1
codex mcp get bpsim-unity
& .\Tools\UnityMcp\.venv\Scripts\python.exe .\Tools\UnityMcp\smoke_test.py
```

- Python 3.10+；当前环境是 Python 3.12。`.venv` 和运行状态放在本项目所在盘，不占用 C 盘的大型缓存。
- 生成项目级 `.codex/config.toml`，不修改 `C:\Users\aecli\.codex\config.toml`。此本地配置含绝对路径，已由 `.codex/.gitignore` 排除；换机器/路径后重新安装并检查配置。
- Codex 只对受信任项目加载项目配置。如果 `codex mcp get` 看不到服务，在 Codex 中信任本项目；不要为了接入而改变别的项目权限。
- **现有 chat 的 MCP 工具列表不保证热更新。请在本项目新开一个 Codex chat；不要自动重启所有 agent。**
- Unity 必须打开这个项目，默认 DshBridge 地址为 `http://127.0.0.1:8791`。桥在打开项目时自启，也可从 Unity 的 `DshBridge/Start Server` 菜单启动。
- 如果修改了 `DSH_BRIDGE_PORT`，同步在 MCP 的 args 加 `"--bridge-url", "http://127.0.0.1:新端口"`。
- 安装和 smoke test 不触发编译、Play Mode、场景保存或任何 Unity 内容变更。

## 提供的工具

1. `unity_status`：连接、项目身份、运行/编译状态、当前租约和未确认写请求。
2. `unity_read`：只读查询，无需租约。
3. `unity_acquire_control`：以唯一 agent/chat 名称和用途取得编辑租约；返回 token。
4. `unity_renew_control`：续租，TTL 范围 15–600 秒，默认 180 秒。
5. `unity_release_control`：完成任务后立即释放。
6. `unity_write`：持有效 token 执行允许的修改命令。
7. `unity_acknowledge_uncertain`：人工/agent 实际核查失败请求后的状态，并明确确认后解除不确定状态。

另有 MCP 资源 `unity://bpsim/guide`，内容就是本文档。

推荐给其他 agent 的提示：

> 使用 bpsim-unity MCP。先查 unity_status，只读用 unity_read；需要改编辑器时，以自己的 chat 名称取得控制租约，再执行，完成后释放。不抢占、不绕过他人的租约，不覆盖 .dshclaim 文件。

## 参数示例

`unity_read(method=..., params=...)`：

`unity_read` 和 `unity_write` 返回 `{"method":"命令名","result":桥返回值}`，其他工具返回状态字典。

| method | params 示例 |
| --- | --- |
| hierarchy | `{}`（活动场景，子树最多 6 层） |
| find | `{"name":"Camera"}`（按对象名称片段查找；仅返回桥能查到的活动对象） |
| get_components | `{"target":"Main Camera"}` |
| get_property | `{"target":"Main Camera","component":"UnityEngine.Camera","path":"orthographicSize"}` |
| read_file | `{"path":"ProjectSettings/ProjectVersion.txt"}` |
| list_dir | `{"path":"Assets/Scenes"}` |
| console / compile_status | `{"count":20}` |
| playmode_state | `{}` |

写命令必须提供 `token`，并先与其他 agent 协调：

| method | params 示例 |
| --- | --- |
| set_status | `{"text":"agent-label: inspecting UI"}` |
| playmode | `{"action":"enter"}` / `{"action":"exit"}`（优先明确动作，不用 toggle） |
| compile | `{}`（返回请求已启动；继续查询 compile_status，不表示编译成功） |
| screenshot | `{}`（在 Unity 的 Temp/DshBridge 写截图，需要租约） |
| invoke_static | `{"type":"Bpsim.QA.SpinPerfProbe","method":"Run","args":[]}`（仅 Bpsim.* 类型；先核实函数签名与副作用） |
| create_empty | `{"name":"Example"}` |
| create_primitive | `{"type":"Cube","name":"Example","position":[0,0,0]}` |
| destroy | `{"target":"Example"}` |
| set_transform | `{"target":"Example","position":[1,2,3],"rotation":[0,0,0],"scale":[1,1,1]}` |
| set_active | `{"target":"Example","active":true}` |
| add_component / remove_component | `{"target":"Example","type":"UnityEngine.Rigidbody"}` |
| set_property | `{"target":"Example","component":"UnityEngine.Rigidbody","path":"mass","value":2}` |
| write_script | `{"path":"Assets/Editor/Example.cs","content":"..."}`（写入后桥会刷新资产，可能编译；优先常规文件编辑与文件认领） |
| save_scene / open_scene | `{"path":"Assets/Scenes/MainScene.unity"}`（必须显式路径） |
| new_scene | `{}`（可能丢弃当前场景的未保存改动） |
| create_material | `{"name":"Example","shader":"Standard","color":[1,1,1,1]}` |

`target` 支持桥的对象名称/层级路径/instance ID；重新编译或切换 Play Mode 后重新查询 ID。
`invoke_static` 只是方法调用返回，不代表异步测试/构建已经完成。请查 Console 与目标输出。
场景修改未必支持 Undo，不自动保存，也不自动退出 Unity。

## 多 agent 边界与故障保护

- 所有此 MCP 的 stdio 进程共用 `Tools/UnityMcp/.runtime/control.lock` 与 `control.json`。
  文件锁跨进程串行化桥请求；活跃的编辑租约只能由取得它的 MCP 会话使用。
- 租约到期才能由别的会话重新取得。跨 HTTP 请求期间锁仍保持；等待超过 3 秒的调用返回忙，避免积累写请求。
- `unity_status` 的 `editor_busy` 修正了已有 DshBridge 的标志语义：它的原始 `changing/busy` 在稳定 Play Mode 也可能为 true。
  实际编译、主线程停滞和已知进入 Play Mode 的状态仍阻止分发。
- 每次分发前核对 `/ping` 的绝对项目路径，错误项目/非 loopback 地址一律拒绝。
- 文件路径限制在本项目；脚本和场景写入检查目标及父目录的 `.dshclaim`。
  **这不是文件所有权系统**：检查后外部进程仍可能建立新认领；命令行编辑、场景对象修改、静态方法内部副作用不能靠该检查完全隔离。
- **原始 DshBridge 端口没有因适配而增加认证或强制锁。**旧 HTTP 客户端、`Tools/dsh.py`、Unity 内的人类操作仍能绕过 MCP 的协调规则，必须主动协调；不要把 8791 暴露到公网。
- POST 写请求没有自动重试。调用前写入 pending 状态，客户端崩溃、HTTP 超时或失败会阻止后续写入，防止原请求延迟执行造成重复操作。
- 不确定状态不会因租约到期自动消失。先查看状态、Console/场景结果，确保 Unity 稳定，再持租约调用
  `unity_acknowledge_uncertain(token=..., inspection="已检查的实际结果", confirmed=true)`。
  **不要仅因工具提示而盲目确认，也不要删状态文件强制解锁。**
- 如需删除本服务，只移除项目配置中 `bpsim-unity` 的两个配置表；不停止或移除原有 DshBridge。

## 验证范围

`test_bridge.py` 使用临时目录和模拟桥，验证权限、路径、错误项目、忙状态、跨进程竞争、超时保护及恢复。
`smoke_test.py` 启动两个真正的 MCP stdio 会话，验证初始化、工具/资源发现、Unity 连接、读文件/Console，以及租约互斥与释放。
它不执行任何 Unity 写命令。Play Mode 切换、编译、构建和截图的写侧功能不通过本次安装进行实机破坏性验证。

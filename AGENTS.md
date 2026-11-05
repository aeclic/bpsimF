# Unity editor automation / 多 agent 协作

- 本项目已有 `Assets/Editor/DshBridge/DshBridge.cs`，不重复安装另一套 Unity 桥。
- Codex 的项目级 MCP 服务名是 `bpsim-unity`；安装、参数、排错见 `Tools/UnityMcp/README.md`。
- 操作编辑器前先调用 `unity_status` 核实项目、编译状态和控制者。
- 只读检查使用 `unity_read`。修改场景、切换 Play Mode、触发编译、运行测试/构建前，必须先用唯一 chat 标签和用途调用 `unity_acquire_control`；完成后立即 `unity_release_control`，长任务提前续租。
- 不抢占其他 agent 的有效租约，不把自己的 token 交给其他 chat。不要在别人控制编辑器时绕过 MCP 直接调用 `Tools/dsh.py` 或 HTTP 写接口；旧客户端并不受 MCP 互斥锁保护。
- MCP 租约不等于代码文件所有权。继续遵守现有 `.dshclaim` 文件/目录占用标记，不覆盖别的 agent 的文件。静态测试方法内部及命令行文件修改不在 MCP 的路径检查范围内。
- 写请求失败/超时后禁止盲目重发：原请求可能已经执行。检查 Unity/Console，稳定后明确确认结果，再调用 `unity_acknowledge_uncertain`。不要删除 `.runtime/control.json` 来绕过保护。
- 不为安装 MCP 自动进入 Play Mode、重新编译、保存/切换场景或退出 Unity；安装验证只做只读查询和取得/释放租约。

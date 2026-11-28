gate: passed | mode=直接做 | oracle=Unity Editor.log 与 UniTask DLL 元数据 | criteria=最终完整域重载后精确诊断数为 0 | round=1 | status=done | date=2026-11-25

# UniTask Particle Callback Diagnostic

## Claim

假设（中）：Unity 2022.2 在完整域重载后不再将改名后的 UniTask 私有回调报告为 `OnParticleUpdateJobScheduled` 消息。

互斥预测：成立时，最终 `Begin MonoManager ReloadAssembly` 之后该诊断出现次数为 0；不成立时，重载后仍至少出现一次。

机器判据：扫描 `%LOCALAPPDATA%\Unity\Editor\Editor.log`，统计最后一个 `Begin MonoManager ReloadAssembly` 之后的精确匹配 `Script error: OnParticleUpdateJobScheduled`；`0` 为 PASS。

## Result

`Assets/Plugins/UniTask.dll` 中目标私有方法已改名为 `onParticleUpdateJobScheduled`，签名未变。2026-11-25 的编辑器日志包含 4 条历史诊断，均在最后一次完整域重载（第 2638 行）之前；重载后的诊断数为 0。Unity 脚本编译成功，程序集重载完成。

## Scope

已验证 Windows Unity Editor 2022.2.23f1c1。Player/Android 启动和 UniTask 粒子触发器运行行为未验证。

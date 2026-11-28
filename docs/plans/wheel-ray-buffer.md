gate: passed | mode=直接做 | oracle=现有 PartGameplayQA 轮子场景与相同射线结果 | criteria=两次独立 Player A/A 建立帧时地板，候选版轮子场景全部通过且 WheelSystem p95 改善超过地板 | round=1 | status=running | date=2026-11-25

# Wheel Ray Buffer Reuse

## Claim

假设（中）：`WheelSystem.RaycastGround` 每个轮子、每个固定步创建并释放一个临时 `NativeList<RaycastHit>`；复用系统持有的缓冲区会减少轮子密集存档的原生分配器开销。若 Temp 分配开销可忽略或复用缓冲导致容量增长抵消收益，WheelSystem p95 不会超过独立 A/A 地板。

互斥预测：若成立，所有轮子射线输入、命中集合及最近有效命中保持一致，现有轮子玩法 QA 全部通过，且固定输入 Player 的 WheelSystem p95 降幅超过独立 A/A 地板；若不成立，物理/轮子状态发生变化，或耗时差异未超过 A/A 地板。

机器判据：先用同一存档与设置做两次独立 A/A，记录 WheelSystem p95 地板；再与候选版配对。`PartGameplayQA` 中 motorwheel、stickywheel、offroadwheel 与 cartwheel 全部通过，且候选版 p95 降幅严格大于 A/A 地板，才接受性能 claim。

## Scope

只复用 `WheelSystem` 的命中暂存缓冲，不改射线过滤器、命中选择、施力、模拟后端或物理质量设置。Unity Editor 验证须先由 Coplay MCP 读取 Editor 状态。

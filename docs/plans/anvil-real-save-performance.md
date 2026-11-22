gate: passed for shipped changes | oracle=independent Windows Player replays of four real saves, three backends, and isolated parity | evidence=production-regular-v4, production-large-v4, production-track-v4 | round=15 | status=verified; two regular saves still slower than references | date=2026-11-20

# Anvil Physics Real-Save Performance

## Scope

对 `savefile` 下四份 CSV 建立可重放的 Windows 基线，覆盖 Unity Physics、Havok Physics 和 Anvil/Box3D。Editor 用于定位，最终性能以独立 Windows Player 复测为准。测量固定物理步数下 `PhysicsSystemGroup` 的墙钟时间，并记录托管同步与原生阶段计时、输入/最终状态摘要、刚体与关节规模。不外推 GPU、整帧、带操作输入的玩法或其他平台。

旧 `spinperf.log` 与合成基准只用于定位仪器，不作为本轮实档基线；GC 零值不作为分配结论。用户存档只读加载，不调用存档保存接口；物理后端覆盖只在内存生效，探针结束时停止临时模拟并恢复原设置。

## Preregistered Claim

假设（中）：在同一存档、后端和固定 PhysicsStep 配置下，独立加载的部件/关节规模一致，物理结果 A/A 可量化，且 Anvil 的每步托管同步、原生求解或导出阶段至少有一个可重复地解释主要 step CPU 成本。置信度为中，因为真实存档是 oracle，但本轮尚未建立 A/A 地板。

互斥预测：若成立，两个独立报告的存档哈希、规模和物理步数一致，状态差异不超过同后端 A/A 地板，且稳定的 Anvil 阶段分布可以排序；若不成立，至少一个输入/规模/步数不一致、物理状态超出 A/A 地板，或阶段差异小于采样噪声，则不按该测量选择生产优化点。

机器判据：`python -X utf8 Tools/AnvilStutter/check_save_performance.py <24 reports>` → `PASS` 当且仅当四存档 × 三后端 × 两次独立运行齐全、后端实际生效、配置与加载规模匹配、采样步数完整且 A/A 状态差在预注册容差内；性能收益另要求超过同场景 A/A p95 地板。

## Work Sequence

1. 增强 Editor QA 探针：非持久化后端覆盖、固定步数采样、每个 PhysicsSystemGroup 的 CPU 样本和状态摘要；不改生产物理路径。
2. 先跑三份代表性存档验证输出形状与 A/A，再扩展到全部四份、三后端、独立重复。
3. 只按新鲜 A/A 稳定的阶段分布定位 Anvil 热点，登记具体生产改动的二值行为与性能判据后再改代码。
4. 修改后重跑实档矩阵、现有 15 workload 合成 A/A、physics parity A/A；任何噪声覆盖不足时只报告实测，不宣称整体胜出。

## Risks

- Havok 和 Unity Physics 在 Editor 中的 Burst/Native 执行模式可能不同；比较限定为此 Editor 配置，不能外推 Player 排名。
- 大型存档的关节拓扑和接触状态会导致分布长尾；每个 backend 都从同一 CSV 重载，记录实际 PhysicsStep backend、碰撞/关节规模与物理步数。
- 测试加载会替换当前临时 PartManager 仿真；执行前确认 Editor 不在 Play Mode，结束后退出测试模拟、恢复内存设置，不保存场景或存档。

## Change Log

- 2026-11-20: 初始 gate。现有真实存档探针没有后端参数与物理组逐步计时，旧文本日志不能回答实档 A/A 与阶段归因；先改造测量，不据旧数值动生产 solver。
- 2026-11-20: `aligned-baseline` 两存档 × 三后端 × 两次均完成固定窗口；Anvil 中位原生求解约 4.2 / 5.5 ms，子步内托管路径额外约 1.4 / 2.7 ms。`Level_Sandbox_04_4` 请求 Anvil 时实际运行 Havok；`天使座` 20,405 部件的加载使首个时钟安排失配，均不计作有效基线。

## Round 2: Native batch

- 假设（高）：将同一组子步的反力读取移到原生层，保持逐子步冲量顺序和接触采样，可以降低整步开销。可能错在原生重编译行为不一致，或新增遍历抵消 interop 收益。
- 成立：两个实档 p95 下降超过各自 A/A 漂移，有限性、拓扑和物理状态仍在容差内，断裂、接触、铰链和休眠检查无新增失败。不成立：任一场景状态越界或 p95 收益被 A/A 漂移覆盖；不宣称该场景提速。
- 判据：`check_save_performance.py --baseline aligned-baseline --candidate batched-after` 与私有 `Run-Private.ps1 -Suite Parity`。不通过降低子步数量或跳过接触/断裂事件来过关。

## Round 3: Weld kernel

- 假设（中）：焊接平移有效质量矩阵中，两个反对称矩阵的零项乘积可以省去，且非零项顺序不变；在约束密集的存档上减少原生求解时间。
- 判据：保持 Box3D 步长、约束和接触流程不变；所有私有物理检查通过，实档状态在同一容差内，且总步时或原生求解节省超过 A/A 波动。若状态越界，不安装该内核。

## Round 4: Contact impulse

- 假设（高）：单位质量的平底箱与地面多点接触时，取最深单点会低估整个接触的法向冲量。单点、单子步以及多个子步的输出应与速度变化对应的动量一致。
- 判据：同一套私有 QA 用原版 DLL 作负对照，候选 DLL 的 `contact-box-sub{1,2,4}` 冲量误差应小于等于 0.005 N·s，同时保留已有球体落地、休眠与断裂用例。

## Round 5: Startup topology

- 假设（中）：Anvil 可能在 `BuildPhysicsWorld` 填完世界之前同步裸指针，导致开场关节冲量与规模失配。若加载完成时关节已经缺失，或同步后仍复现同类断裂，该假设不能解释失配。
- 判据：在同一个隔离 Editor 内，用同一 DLL 对照每次 `StartSimulation` 返回时的关节数、前四个物理步的世界构建完成状态、已构建/原生关节数和断裂事件。只切换 Anvil 读写世界前的依赖屏障。保留全部启动轨迹，不按结果挑选样本。

## Round 6: Native code generation

- 假设（中）。Clang 的跨文件内联和循环优化可以缩短热路径的关节分发与约束求解。保持相同的浮点收缩设置、驱动、子步和事件路径。
- 验收使用同一构建源码的 O2 对照。候选必须通过固定窗口的实档拓扑和状态检查，且 p95 收益超过重复运行的波动。

## Round 7: Integrated Box3D substeps

- 假设（中）：每个固定步只构建一次碰撞约束，可节省重复 narrowphase、约束准备和 transform finalization。可能的失配是同一大步内后出现的接触、CCD、断裂和偏心锚点的积分差异。
- 只在隔离构建中，让 Box3D 内部子步逐次累积关节冲量，并读完整固定步的接触冲量。与同质量下的现有八次单步对照；有效门槛是所有强制 parity 项通过且真实存档中的误差不大于既定容差，不能用帧率代替行为验收。

## Round 8: Signed hinge limits

- Hypothesis (high, pending): Unity measures twist of A in joint frame B, while the pinned Box3D revolute solver measures B in A. Passing an asymmetric range without reversing its sign moves a correctly oriented hinge and can overload breakable track joints.
- Oracle: test both limit centers (+90° and -90°), both edges, and a nonidentity joint frame. A hinge at the authored center must drift no more than 0.01 rad at rest; under a moderate twist it must reach the selected edge and stay within 0.06 rad of it. Run the same fixtures on all three backends, before and after the adapter change.
- Acceptance: the isolated `Run-Private.ps1 -Suite Parity` must distinguish the old adapter from the fix. Separately retest the finite-strength track save; a passing fixture is not grounds to enable track support by default.

## Round 9: Track instability

- The first infinite-strength track run reached 1,037 joints after four steps and then crashed during body transform upload. The tree probe measured an invalid AABB with NaN bounds after island coordinates had reached approximately 10¹¹ m.
- Hypothesis (medium): Box3D's gyroscopic solve can amplify small out-of-plane angular components in these assemblies. Unity Physics integrates the authored angular velocity without that term. Compare identical saves with and without that calculation and record survival, joint counts, peak speed and deformation.
- Require fixed-window finite state and passing rigid-body impulse, hinge, impact and sleep contracts. Keep the tested solver settings and compare normal saves to the established state tolerances before installing any new solver mode.

## Round 10: Hinge wrap

- Hypothesis (high): a twist across ±π and within π of the authored range center should be corrected toward the nearest limit, as in the Unity `AngularLimit1DJacobian`. The pinned native revolute code compares directly with the [-π, π] representative.
- Oracle: for a +90° or -90° range, construct a hinge just under π away from its center on the far side of the principal-angle seam. The first tick must move toward the nearest boundary by at least 0.01 rad. Repeat both signs on all three backends.

## Round 11 — Unchanged joint descriptors

- Hypothesis (medium). In the large save, a bulk comparison can avoid resolving every body index and making one managed `MemCmp` call per steady-state joint. Invalidated native handles or modified joint descriptors still require synchronization.
- Compare with the same native DLL and step configuration. The prediction is lower `syncToMs` when topology matches and unchanged state, breakage and contact reports. Metadata changes, body remaps, replaced joints and stale native handles must reach the existing reconciliation path.
- Accept only with passing private parity, metadata contracts, and two real-save runs that pass topology/state checks. Treat timing improvements below the A/A floor as inconclusive.

## Round 12 — Track assembly at rest

- With gravity disabled, the Player still lost 881 Anvil joints over the startup and 8/32-step window. Unity Physics and Havok also produced high angular velocities in this experiment.
- Hypothesis (high for the frame mismatch, medium for its contribution to breakage). Matching the initial world-space joint frames makes the initial twist zero even for diagonals authored with ±90° centers. This drives torsional correction immediately on both references and on Anvil. Rotate the target reference frame about the hinge axis so the constructed pose lies in the middle of its range. Keep the signed limits, pivot and break threshold.
- Compare the original and centered frames at rest and in the track save. Look for retained joints, finite speeds, and stable anchor error on repeat runs. Keep this as an isolated experiment until the gameplay and import fixtures verify the intended rest-angle behavior.

## Round 13 — Solver task claims and initial weld frames

- A worker visits every block in a color even after the other workers have claimed it. Test a relaxed load before the atomic compare-and-swap so already claimed blocks incur no locked operation. Preserve color order, thresholds, and step length.
- A one-substep Box3D call warm-starts and performs the biased weld solve before integrating positions. Test whether reusing the prepared anchors and frames in those two phases reduces solve time. The relaxation phase and multi-substep path still use the integrated rotations.
- Test both variants against a same-compiler build and the installed DLL. Require identical topology, state within the save tolerances, paired p95 benefits above A/A drift, and unchanged contact and joint contracts. Keep this separate from the track stability experiments.
- Neither variant cleared the regular-save p95 gate. The separate `player-solver-serial-*` tests and the scratch patches under `_codex_work/anvil-native` are diagnostics, not part of the production DLL.

## Round 14 — First unstable track step

- With the plate colliders removed by the QA probe, two Anvil reloads have zero contacts through tick 12. Both spike from 10.46 to 8,991 m/s on tick 6 and emit 30 breaks. Unity Physics and Havok stay under 5 m/s on tick 6 with the same input and collider override.
- Hypothesis (medium). Warm starting the redundant weld constraints on the split plates amplifies solver impulses. Trace the affected constraints and test a cold start with the same joint graph. Require the first 12 ticks to remain finite with peak body speeds below 50 m/s and retain at least 99% of joints. Repeat with the original colliders and extend the observation window before enabling the backend for tracks.
- The weld-only and plate-endpoint-only cold-start variants still diverged. The latter traced subsequent impulses to welds outside the plate halves. Track simulations use Box3D cold starts through the normal backend selection; this disables warm starting for the entire track world, not just its hinges. Normal worlds retain their existing setting. The final-build replays below supersede the diagnostic cold-start runs.

## Round 15 — Symmetric weld matrix

- Test reducing `skew(r) I⁻¹ skew(r)` and its adjugate to six entries by symmetry. The expected benefit is less arithmetic in both weld solve passes, with roundoff potentially changing trajectories. Require repeatable p95 savings beyond A/A drift in both regular saves and unchanged topology and state within the replay tolerances before testing for release.
- The isolated Zig 0.13.0 builds differ only by `_codex_work/anvil-native/weld-symmetric.probe.patch`. The `symmetric-zig-control` and `symmetric-zig-probe` matrices pass the state tolerances. `bpsim1号机` improves by at least 0.223 ms, below its 1.402 ms A/A floor; `不败荣光` is slower by at least 0.239 ms. The patch was not installed. Keep the existing sparse weld kernel.

## Windows Player Results

Windows x64 Release Player built with Unity 2022.2.23f1c1 and Burst AOT 1.8.4. The `lib_burst_generated.dll` SHA-256 begins `491d36d3be9e`; the `Unity.Physics` assembly ID is `53ad31d4b8204a2ab08601adeba8b9ff` in all 24 backend runs and all six legacy DLL runs. Each timing is one independent process's p95 for the entire `PhysicsSystemGroup`. The two Anvil columns use the same final managed Player with only the native DLL changed. The legacy DLL SHA-256 starts with `a6172b234bdc` and the shipped DLL with `eeaf8f719f34`. All four inputs match the SHA-256 of their read-only source saves. Settings: 20 ms ticks, quality 8, four Anvil workers and finite connection strength.

| Save | Parts / initial ECS joints | Window (warmup / sampled) | Legacy Anvil (ms) | Optimized Anvil (ms) | Unity Physics (ms) | Havok (ms) |
|---|---:|---:|---:|---:|---:|---:|
| `bpsim1号机` | 594 / 3,050 | 32 / 128 | 10.424 / 10.656 | 6.457 / 6.288 | 3.888 / 4.070 | 4.722 / 4.239 |
| `不败荣光` | 1,648 / 5,254 | 32 / 128 | 17.022 / 15.986 | 13.198 / 12.331 | 9.171 / 5.766 | 7.215 / 7.871 |
| `天使座` | 20,525 / 192,012 | 8 / 32 | 203.774 / 199.797 | 64.658 / 63.432 | 250.608 / 235.585 | 228.143 / 231.551 |

The two DLLs match topology and the preregistered state tolerances in all three saves. Comparing the faster legacy run with the slower candidate gains 3.967, 2.788 and 135.139 ms; the respective maximum Anvil A/A drifts are 0.232, 1.036 and 3.977 ms. Both regular saves remain slower than the reference backends. The large save finishes with 192,024 joints on all three backends; Anvil is over 3× faster in this measured window. Final Anvil fixed-anchor RMS is approximately 1.67 μm, 15.0 μm and 49.9 μm. The large save's Unity Physics and Havok RMS values are 10.71 mm and 0.101 mm. Fixed-anchor RMS measures retained welds, not trajectory or collision equivalence.

Use labels `production-regular-legacy`, `production-regular-v4`, `production-large-legacy`, and `production-large-v4` in the sibling `_anvil_save_slot/Logs` directory. `check_save_performance.py` validates the input hashes, backend identity, fixed windows, report counts, p95 calculations and same-backend state. Use `--backends AnvilPhysics --minimum-bridge 4` for a legacy/candidate comparison, or validate the three-engine v4 matrix with `--baseline <v4 label> --require-comparable-topology`.

`Level_Sandbox_04_4` (`production-track-v4`): all three backends start with 1,037 joints after initialization. Anvil retains 1,037 in both runs, Unity Physics 1,023, and Havok 49. Do not rank timings across the different surviving workloads. Anvil alone records 4.831 / 3.747 ms p95, 35.42 mm final fixed-anchor RMS and a 40.19 m/s maximum final ECS part speed, without tracing.

Separate `production-track-v4-trace` replays cover 164 physics steps each. The full 1,037 joints are present from tick 2 onward, with zero non-finite physics bodies and identical phase state digests. Physics-world speed peaks are 24.85 m/s and 12.53 rad/s; the ECS part metric above samples a different state after export and gameplay updates. `production-track-extended-v4` repeats with 512 sampled ticks after startup and warmup (10.96 s simulated): both end with 1,037 joints, identical state digests, zero non-finite captured parts, 54.50 m/s maximum final ECS part speed and 34.35 mm fixed-anchor RMS. These are no-input runs, not a guarantee for driving or prolonged collision stress.

Final installed-fingerprint parity: `Logs/anvil-parity-production-v4-{a,b}.json`, each with 363/363 mandatory checks and four known Havok reference deviations. Metadata: `Logs/anvil-metadata-production-v4-{a,b}.json`, each 8/8 with identical measurements. BPIR: 136/136 in the private Editor. The native ABI and warm-start switch probes pass. A fresh Zig global cache allows rebuilding the pinned source; the rebuilt DLL is tested separately, not substituted for the shipped fingerprint. Restart the main Editor to load the installed Windows DLL.

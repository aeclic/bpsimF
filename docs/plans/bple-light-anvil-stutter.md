# BPLE light lifecycle and Anvil stutter

gate: passed | mode=直接做 | oracle=reported exception plus production Unity console, isolated component lifecycle checks | criteria=Tools/AnvilStutter/Invoke-Unity.ps1 -Action LightLifecycle | round=1 | status=done | date=2026-11-06

## Scope and constraints

- Fix the MaterialPropertyBlock constructor/serialization exception without replacing the concurrent BPLE implementation.
- Separate light preview/physics/rendering overhead from Anvil solver, synchronization, contact sampling and joint-break sampling. A native timing number alone is not a frame-time oracle.
- Preserve the completed Anvil parity fixes, especially rigid-weld semantics, momentum, full-frame impulse thresholds, four-or-more solver substeps, gravity/damping ordering and sleep/wake behavior.
- Do not change scenes or Play Mode while another editor lease is active; discover the actual bridge port and use the shared lease. Never clear the console.
- Keep benchmark scratch and retained reports on D:. No commits, branches, restore points or unrelated-file cleanup.

## Acceptance and tasks

1. Lifecycle: no constructor/initializer exceptions, properties initialized before rendering, repeated Ensure returns the same component, temporary fixture objects/subscriptions removed. Exercise inactive construction and explicit Awake separately, and actual Play Mode when safely available. Basis: F323.
2. Performance gate: construct deterministic private PhysicsWorld workloads and repeated independent runs before optimization. Report total sync/step/read time, native solver time, managed allocation, p50/p95/p99/max and stalls above 16.67/33.33 ms. Compare the same workload, solver quality and physical state digest; timing changes must exceed measured A/A variability. Basis: F324.
3. Measure light preview independently, including an empty scene and many light sources; any optimization must retain add/remove/scene-switch/pose/activation behavior. Use the existing light regression instrument only after reviewing its scene and simulation side effects.
4. Retain only measured optimizations. Run fresh Anvil parity A/A reports and the validator after physics changes; full project compilation must finish with zero errors. Basis: F324.

## Instruments and boundaries

- Production editor: Tools/AnvilStutter/Invoke-Unity.ps1, shared Bridge lease, no automatic retry of ambiguous writes.
- Physics: existing Tools/AnvilParity private project/dependency closure, extended with a separate benchmark entry point; reference Unity/Havok backends remain read-only.
- Reports: Logs/ and Tools/AnvilStutter/reports/. Timings are CPU timings in this Windows editor environment, not GPU/frame-pacing or arbitrary player builds.
- Live profiling is read-only with respect to player builds/settings; controlled scene tests require restoring the original scene/settings in finally.

## Open questions

- Is the reported stall dominated by the exception/logging, BPLE scans/allocations, managed Anvil interop/sampling, or the native solver?
- Which overhead can be removed while leaving the deterministic physical state and mandatory parity checks unchanged?

## Change log

- 2026-11-06: inserted this follow-up ahead of optional parity expansion because it touches the same light runtime and physics stepping paths. Completed parity results remain in anvil-physics-parity-results.md.
- 2026-11-06 (F323): two independent lifecycle invocations pass 113 checks each, 16 inactive constructions per invocation, zero constructor exceptions, idempotent Ensure and callback/singleton cleanup. This is edit-mode explicit lifecycle coverage; actual Play Mode remains a separate check.
- 2026-11-06 (F324/F325/F326): the independent 15-workload baselines have exact state/contact trajectories. Native solve is only part of the total; contact-stack sampling dominates the remainder, and the finite/infinite-threshold control separates event overhead. Preregister bounded pointer/predicate cleanup and native collector saturation/purity testing before editing these production paths. Quality-8 assembly p95 A/A noise is 3.531 ms, so speed claims must not rely on a small/noisy timing change.
- 2026-11-06 (F325): the pointer/predicate intermediate preserves all 15 exact state/contact trajectories. Quality-8 finite-threshold sampling remainder falls from 1.862 to .912 ms (means), and event export from .583 to .091 ms. Its total p95 change alone does not exceed the wide independent baseline floor.
- 2026-11-06 (F328/F329): actual-contact hash flooding is proven in two independent processes (1097 pairs, only 43 default hashes, largest group 255). Promote the exact-equality avalanche comparer, then validate full-step improvements. Also preregister exact-byte caching of the inline Joint descriptor, with mandatory no-version threshold/frame/collision mutation controls; native validity and body generations remain checked.
- 2026-11-06 (F326): saturation/purity native probes pass all six capacities/edge workloads independently. Full counters are sub-microsecond in the native microprobe; do not claim they explain the multi-millisecond stall or promote an adaptive-buffer rewrite merely because it is possible.
- 2026-11-06 (F329/F330/F331): metadata threshold/frame controls pass, but the unchanged collision-enable control fails in both processes. Keep the failure and its criterion. Native source and independent sleeping-world probes prove b3DestroyJoint's missing wakeAttached argument. Supply explicit teardown/runtime wake values and notify the existing joint's collision setter before recreation so unmoved proxies are refiltered; then rerun the same metadata controls before performance acceptance.
- 2026-11-06 (F324): final full-step state/contact trajectories remain exact in all 15 workloads and four independent reports. Contact stack, rotating grid, quality-4 assembly and infinite-threshold control exceed conservative total-p95 A/A floors. Quality-8 finite assembly total-p95 improves numerically, but its 2.039 ms conservative gain is below the 3.531 ms baseline floor; the original broad two-quality speed gate is NOT accepted. Preserve it and do not claim universal p95/stall elimination.
- 2026-11-06 (F327): actual Play Mode singleton and all 15 preview state/pose/add/remove/restoration checks pass. A preview query/scratch/renderer-cache trial preserves exact geometry/pose/color, but the thread allocation API is unusable here: allocating 1 MiB returns zero thread-counter delta while Unity's Mono-used heap rises 1052672 bytes. Reject any zero-to-zero GC claim; revert the optional preview-cache production changes rather than adding lifecycle complexity without the registered evidence. Keep the required Awake fix. Preview CPU windows are not the multi-millisecond Anvil hotspot.
- 2026-11-06 (F332–F335, completed scoped follow-up): retained fixes pass 297 mandatory checks in two private processes and the actual production editor, all A/A numerical floors zero; no-version metadata 8/8, light regression 144/144 and lifecycle checks pass. Compile is idle with no errors/warnings; control/pending are released. Report only the accepted specific-root CPU evidence, not the unaccepted universal two-quality P95 claim or unsupported GC measurements. Optional preview caches and adaptive contact-capacity changes are not retained. Full result/boundaries: bple-light-anvil-stutter-results.md.

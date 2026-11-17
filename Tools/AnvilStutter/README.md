# BPLE lifecycle and Anvil stutter instruments

All commands run from the project root in PowerShell. Output and private builds remain on D:.

## Lifecycle

```powershell
& ./Tools/AnvilStutter/Invoke-Unity.ps1 -Action Status
& ./Tools/AnvilStutter/Invoke-Unity.ps1 -Action LightLifecycle -Label awake-a
& ./Tools/AnvilStutter/Invoke-Unity.ps1 -Action LightLifecycle -Label awake-b
```

The edit-mode instrument creates only its own inactive temporary hosts, explicitly invokes Awake/OnDestroy on the editor thread, checks the native property block, repeated Ensure and callback cleanup. With a live singleton it performs read-only checks instead. It does not change scenes/Play Mode, clear logs or prove rendered-pixel/GPU equivalence. Newly added scripts must be imported before invoking their type; compile requests alone do not force asset discovery.

## Independent CPU baselines and comparison

```powershell
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Performance -Label stutter-before-a -TimeoutSeconds 360
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Performance -Label stutter-before-b -TimeoutSeconds 360
python -X utf8 ./Tools/AnvilStutter/check_performance.py --before Logs/anvil-performance-stutter-before-a.json Logs/anvil-performance-stutter-before-b.json
# Only after profiling and a justified physics adapter edit:
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Performance -Label stutter-after-a -TimeoutSeconds 360
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Performance -Label stutter-after-b -TimeoutSeconds 360
python -X utf8 ./Tools/AnvilStutter/check_performance.py --before Logs/anvil-performance-stutter-before-a.json Logs/anvil-performance-stutter-before-b.json --after Logs/anvil-performance-stutter-after-a.json Logs/anvil-performance-stutter-after-b.json
```

Five fixed workloads across all three backends: free flight, 1,024-body finite-threshold assemblies at quality 4/8, infinite-threshold control at quality 8, and a 512-body contact stack. Sleep is disabled deliberately; each fixture has 64 warmup and 256 measured steps at 0.02 s. Native substeps and reference solver iterations use the same recorded quality setting. This does not imply the solvers perform identical operations.

Raw timings include the fixture's body-pose/index-map preparation, sync, solver/interop/sampling, readback and native event export. Native profile time is also retained. The step-minus-native figure is a diagnostic remainder, not an isolated proof of one native getter's cost. This fixture excludes production BuildPhysicsWorld, gameplay, rendering and GPU/frame pacing. Full state trajectories are hashed outside timing using reused buffers; per-step contact counts are retained. CPU outliers above 16.67/33.33 ms are counts of physics steps, not display frames.

The validator refuses missing/duplicate workloads/samples, stale benchmark sources, quality/input/native changes, repeated run IDs, inconsistent raw statistics, non-finite states or changed exact state/contact trajectories. Improvement requires both finite-threshold assembly quality levels to beat a conservative 10% and independent A/A timing floor. Retain a separate fresh mandatory parity run; timing equality is not an impulse/strength oracle.

Current result: the broad two-quality total-p95 gate is intentionally **not accepted** (quality-8 baseline noise). The accepted contact-stack/quality-4/rotating-grid/infinite-threshold results and unchanged physics trajectories are documented in docs/plans/bple-light-anvil-stutter-results.md. Do not reinterpret a failing global speed gate as a global PASS.

Allocation caveat: the shipped Unity Mono thread-allocation API returns zero even for the explicit 1 MiB positive control. All recorded zero `allocatedBytes` values are **unsupported measurements**, not proof of zero allocation/no-GC. Optional preview caches were reverted. `check_preview.py` now rejects zero-to-zero allocation claims; its CPU/semantic baseline remains useful.

## Additional contracts and real Play Mode

```powershell
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Sampling -Label hash-a
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Sampling -Label hash-b
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Metadata -Label metadata-fixed-a
& ./Tools/AnvilParity/Run-Private.ps1 -Suite Metadata -Label metadata-fixed-b
python -X utf8 ./Tools/AnvilStutter/probe_joint_lifecycle.py wake-a
python -X utf8 ./Tools/AnvilStutter/probe_joint_lifecycle.py wake-b
& ./Tools/AnvilStutter/Invoke-Unity.ps1 -Action Refresh
# After compile status is stable, and without any simultaneous physics benchmark:
& ./Tools/AnvilStutter/Run-Preview.ps1 -Label preview-final -VerifyLights
```

Run-Preview obtains an exclusive lease, reads actual readiness (DontSave hosts are invisible to ordinary FindObjectsOfType), runs temporary build fixtures, optionally runs the existing light regression, and restores the original scene/settings/Play Mode. Never run it while the user's simulation is active. It retains uncertain writes instead of blindly retrying them.

The metadata contracts keep the exact pre-fix collision-enable failure fixture/criterion and test thresholds, frames and collision toggles without relying only on Version. Native lifecycle probes prove the required destroy-joint wake argument using sleeping positive controls. Retained final reports are under reports/.

Source/native changes invalidate old performance data for current-build claims. Two copies of one report are not independent A/A runs. Do not overlap benchmark processes, or compare runs made under different background workloads.

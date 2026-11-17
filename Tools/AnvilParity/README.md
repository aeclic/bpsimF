# Physics parity instrument

`AnvilParityQA` constructs private PhysicsWorld instances directly. It does not open/save scenes, enter/exit Play Mode, initialize gameplay systems, change the active backend, or access player saves. Each run disposes its reference simulation, native world, collider blobs, event lists and static-change marker.

Inputs: the same deterministic body/constraint arrays, world-space impulses, geometry, masses and fixed time steps for all three engines. Output: `Logs/anvil-parity-<label>.json`, with backend/case/metric/tolerance and pass/fail values. The source and native DLL fingerprints are included so reports cannot be mistaken for current-build evidence.

PowerShell, from the repository root:

```powershell
& ./Tools/AnvilParity/Invoke-Unity.ps1 -Action Status
& ./Tools/AnvilParity/Invoke-Unity.ps1 -Action Compile
& ./Tools/AnvilParity/Invoke-Unity.ps1 -Action Run -Label baseline
& ./Tools/AnvilParity/Invoke-Unity.ps1 -Action Run -Label fixed-a
& ./Tools/AnvilParity/Invoke-Unity.ps1 -Action Run -Label fixed-b
python -X utf8 ./Tools/AnvilParity/check_report.py Logs/anvil-parity-fixed-a.json Logs/anvil-parity-fixed-b.json
```

The CLI discovers the live bridge's port and checks the absolute project identity, then uses the same cross-process control lease as the project's MCP client. It never clears console logs or retries an uncertain editor write. Wait for compilation to finish using `Status` before requesting another write.

For isolation from active gameplay or other editor tests, the recommended command is `& ./Tools/AnvilParity/Run-Private.ps1 -Label fixed-a` (then `fixed-b`). It creates/reuses the ignored `_codex_work/anvil-parity/project`, compiles the current physics package into that project only, copies the actual core dependency closure and native DLLs, and launches a hidden batch-mode editor. It neither copies game scenes/saves nor opens or stops the production editor. Fresh reports are copied back to `Logs/`; a nonzero exit, old report, already-running private editor or timeout is an error, not a pass.

The final retained evidence is `reports/final-a.json`, `reports/final-b.json`, `reports/baseline-v4.json` and `reports/main-verified.json`. Run the validator on these files to inspect the saved final build. The final matrix has 301 measurements / 297 mandatory checks; four measured Havok limitations remain visibly failed/reference-only. The declared native quality is four substeps (the production PhysicsStep mapping permits 4-16), not the original one-substep adapter.

Self-checks: analytic free-fall and impulse contracts must pass in the two reference engines; every result must be finite; two independent runs establish the A/A floor; above/below-threshold connections and untargeted bodies are negative controls.

Visibility limits: this instrument measures physics state and event delivery, not rendering/input latency. Cross-solver contact trajectories need numeric tolerances; they are not expected to agree bitwise. It does not prove equivalence for every construction, unsupported collider type or native platform.

`python -X utf8 ./Tools/AnvilParity/native_probe.py` separately checks the shipped native API defaults and singular-inertia contract in a private process. Its world is destroyed in `finally`; it does not use the editor bridge. Read-only exact-revision source is downloaded into the ignored `_codex_work/anvil-parity/oracle/box3d-e77352c` directory, not substituted for the shipped DLL.

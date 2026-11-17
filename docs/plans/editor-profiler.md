# Editor-only AI profiler

gate: passed | mode=直接做 | oracle=installed Unity 2022.2.23f1c1 ProfilerRecorder API, real Editor MCP, deterministic JSONL fixtures | criteria=Tools/UnityMcp/test_profiler.py + EditorProfilerData/Log Mono controls + 13 live/log MCP checks + actual Play Mode samples | round=1 | status=done | date=2026-11-06

## Contract

- Runs only in the Unity Editor, including Editor Play Mode. No player code or UI.
- Does not enable the global Profiler, Deep Profiling, or force ECS job completion.
- Native profiler buffers are summarized once per wall-clock second. Missing metrics are explicitly unavailable; no unsupported allocation counter is used (F335).
- Read-only MCP returns latest/recent samples, a session-scoped sequence cursor, and freshness information.
- UTF-8 JSONL is written by a bounded background worker below the project's Logs/Profiler directory. Files and retained history are bounded.
- Offline queries remain possible while Unity is stopped/compiling; incomplete final lines are ignored. No unbounded file reads or arbitrary paths.
- Automatic bridge discovery is pinned to the configured project and loopback; explicit --bridge-url remains authoritative.

## Work

1. Add Editor-only sampler, deterministic history/statistics helpers, and bounded JSONL writer.
2. Add DshBridge profiler query and read-only MCP live/log/auto interface.
3. Verify recorder availability/units, session reset, history truncation, log rotation, partial writes, bounds and project/lease isolation.
4. Smoke-test live and persisted data through the actual Editor without changing scenes or Play Mode, and document usage.

## Binary acceptance

- All 51 Python bridge/profiler tests pass (50 passed, one host-permission-dependent symlink negative control skipped).
- The actual Unity 2022.2 Editor returns advancing Play Mode samples; the independent MCP smoke passes 13/13 checks, including matching live/log sequences.
- EditorProfilerData and EditorProfilerLog pass 20 standalone Unity-Mono checks for percentiles, unavailable metrics, cursors, rotation, byte/file limits, complete JSONL, disk errors and unrelated-file preservation.
- All four Editor sources compile against the installed Unity 2022.2 and .NET Standard APIs; the data/log/QA sources are Editor-guarded, and compiling the guarded sources without UNITY_EDITOR produces zero types.
- Every new C# source is both below Assets/Editor and guarded with UNITY_EDITOR; compiling without that define produces no profiler types.
- No runtime gameplay assets, scenes, global profiler settings or existing console logs are changed.

Performance overhead is not assumed to be zero. Collection duration, recorder overflow and log drops/errors are exposed. Formal gameplay A/B profiling is outside this integration task.

EditorProfilerQA.Run and its explicit positive-control probe were not invoked: the shared Unity Editor was already running another Play Mode performance QA and reporting `compiling=true`. No compile, Play Mode, scene, or global Profiler action was forced into that session. The real read-only MCP smoke was verified in Edit Mode first and again during that existing Play Mode; `Logs/editor-profiler-validation.json` records the 13 passing checks. The optional editor-side QA entry point remains available for an idle, leased editor.

## Change record

- 2026-11-06 / F336: Actual bridge uses 8790, while the old MCP default was 8791; add identity-pinned discovery with explicit overrides preserved.
- 2026-11-06 / F336: Live native buffers contain unfinished slots with zero invocation count. Exclude these slots and emit null for unobserved markers; do not claim zero cost.

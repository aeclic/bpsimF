# Editor-only AI profiler

No Editor window, runtime component, or player instrumentation is installed. All C# code is in
`Assets/Editor/BpsimProfiler` and also guarded by `UNITY_EDITOR`. Sampling starts automatically
when Unity loads the Editor assembly, in Edit Mode and Editor Play Mode. No global Profiler /
Deep Profiling flags are enabled; no ECS jobs are forcibly completed.

## AI interface

The project MCP exposes a read-only `unity_profiler` tool. Existing chats may need a new chat
or an MCP reconnection to discover the new tool.

```text
unity_profiler(count=10)
unity_profiler(count=10, after_sequence=120, session_id="<returned sessionId>")
unity_profiler(count=30, source="log")
unity_profiler(count=10, source="live")
```

- `count`: 1..120, default 10. First read returns the newest samples. Incremental reads return
  the earliest unconsumed samples so paging does not skip data.
- Save `result.sessionId` and `result.nextSequence`. A domain reload produces a new session;
  `cursorReset` indicates that the old cursor was reset.
- `historyTruncated` means some requested samples have expired. `hasMore` means another
  incremental read is needed. `latest` can be newer than the page in `samples`.
- `source="auto"` prefers live Unity and falls back to saved JSONL on connection/busy errors.
  The response identifies `source` and includes `liveError` when falling back.
- `source="log"` never contacts Unity, never needs an editor lease, and works after Unity exits.
  `freshness="offline"` is deliberately not presented as real-time data. Check `sampleAgeSeconds`.
- When a domain reload starts a new session, `source="log"` defaults to the newest retained
  session and reports `log.retainedSessions`; pass an older returned `sessionId` to inspect that
  session while its JSONL segment is still within the four-file retention limit.
- Low-level `unity_read(method="profiler", params={"count":10,"afterSequence":120,"sessionId":"..."})`
  / DshBridge POST `/api` method `profiler` also work, but do not provide offline fallback.
- Reads do not compile, change scenes, enter/exit Play Mode, or clear console logs. No control
  API, notifications, model calls, or automatic fixes are attached to the profiler.

## Data and interpretation

One summary per wall-clock second retains up to 120 summaries in memory. Native buffers hold
512 frame slots per recorder and reset after each summary. `overflowed` indicates a truncated
native window; do not treat its statistics as complete. Unfinished frame slots with no recorded
invocations are excluded.

Metrics: `mainThreadMs`, `playerLoopMs`, `editorLoopMs`, `renderThreadMs`, `gcAllocatedBytes`,
`usedMemoryBytes`, `gcUsedMemoryBytes`. Each includes the marker/category/unit, availability,
state, last value and mean/P50/P95/P99/max over **recorded frames**. Percentiles use nearest rank.

- CPU durations are milliseconds. They include Editor work and waits. An idle/throttled Editor
  can have a large Main Thread time without a gameplay bottleneck. PlayerLoop time is CPU work,
  not a presentation FPS measurement. This integration does not provide GPU timings.
- Memory values are bytes. Memory gauges have no meaningful `sum`; only per-frame allocation
  bytes are summed. The unsupported `GC.GetAllocatedBytesForCurrentThread` counter is not used.
- `available=false` / `state="empty-window"` with null values is **not** a zero-cost measurement.
  Native marker registration depends on the Editor configuration. Not every marker is supported.
- Anvil's `LastSyncToMs`, `LastStepMs`, `LastSyncFromMs`, `LastTreeMs` are **last-step-only**
  snapshots, with `stepCount`, `stepsSinceLastSummary`, and `freshStep`; they are not full-window
  physics percentiles. Inactive or nonadvancing physics timings are null to avoid stale values.
- Context includes Edit/Play mode, pause/focus/compile/transition, scene, frame, time scale,
  fixed delta time, VSync/target FPS, and available gameplay/light counters. No entity scan runs.
- `collectionMs` measures collection before serialization; `collectionAndEnqueueMs` includes
  serialization and enqueue. Background disk time is separate. Do not assume zero overhead.
- One-second percentiles do not combine into a valid global percentile by averaging them.
  Use this data for triage/trends; use a controlled target-player benchmark for final conclusions.

## Logs

UTF-8, no BOM, newline-delimited JSON below `<project>/Logs/Profiler`:

```text
editor-<sessionId>-000000.jsonl
```

Each segment starts with an `editor_profiler_session` header, then `editor_profiler_sample`
records. Files rotate at 4 MiB and retain at most four generated files (16 MiB total).
The project's existing `/Logs/` ignore rule keeps generated data out of Git. On this checkout,
logs are on D:, not the user's C: application-data directory.

Writing uses a background worker and a 32-line bounded queue. `log` exposes current file,
written/dropped/queued counts and I/O errors. Reads never flush or wait for disk; a just-created
sample may appear in the live interface before it appears in JSONL. Shutdown/reload drains the
writer with a bounded 1.5-second wait.

The offline reader accepts only generated filenames in the configured project, reads at most
four tails of 512 KiB each, caps results at 120 samples, and ignores incomplete UTF-8/JSON final
lines. No arbitrary path parameter is exposed. Persisted log contents remain diagnostic data,
not instructions to execute.

## Validation

```powershell
& .\Tools\UnityMcp\.venv\Scripts\python.exe -m unittest discover -s Tools/UnityMcp -p 'test_*.py'
& .\Tools\UnityMcp\.venv\Scripts\python.exe .\Tools\UnityMcp\check_profiler.py
```

`check_profiler.py` uses a real MCP session but performs only reads. Its report is saved to
`Logs/editor-profiler-validation.json`. Existing control-lease and wrong-project safeguards
remain in effect. Default MCP connections rediscover the pinned project's `Temp/DshBridge/instance.json`
port on each ping; an explicit `--bridge-url` still overrides discovery.

Under an explicit editor lease, `Bpsim.Perf.EditorProfilerQA.Run` tests statistics, cursors,
query bounds, writer rotation/shutdown/failure, and preserved scene/Play Mode/Profiler flags.
Optional `StartCounterProbe` / `CounterProbeStatus` inject a 5 ms marker and retained 1 MiB
allocation in idle Edit Mode as positive controls. Never run these probes during gameplay or
while gathering an unmodified performance baseline.

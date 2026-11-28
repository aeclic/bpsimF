gate: passed | mode=改造后做 | oracle=source-visible allocations and marker-team state plus Unity ProfilerRecorder | criteria=existing preview/team gameplay QA passes; two independent A/A runs establish GC/CPU floors; candidate removes recurring preview allocation and marker refresh CPU exceeds A/A floor | round=1 | status=running | date=2026-11-25

# Runtime Stutter and GC

## Evidence

The 2026-11-25 Editor Profiler sample records two PlayerLoop maxima above 800 ms while simulating. GC-used memory grows from about 2.28 GB to 3.47 GB before a collection reduces it. The sample has no allocation call stacks. Its runtime context reports 10-11 parts, two wheel behaviors, no light sources and no active Anvil step.

The active Editor.log contains no runtime exception entries. An older playtest log contains 253 repeated NullReferenceExceptions from Unity Physics/render registration, but the current Editor.log does not reproduce them.

## Changes

1. Reuse the light-preview entity set instead of allocating a new HashSet on every LateUpdate.
2. Refresh marker-team data when simulation starts, part/joint counts change, or part/topology mutation APIs invalidate the cache. Skip unchanged component writes.

## Acceptance

- Existing light preview and marker-team behavior checks pass.
- Two independent Play Mode A/A runs establish frame and GC allocation floors for matching scenes.
- Candidate preview allocation and marker refresh CPU improve beyond their A/A floors.
- Android validation records managed heap and frame-time behavior over the same fixed workload. This plan does not claim that the current Editor heap growth has been attributed to either change.

Unity Editor interaction requires the configured Coplay Unity MCP Editor status check before compile or Play Mode testing.

## Current Status

The source changes are present and `git diff --check` passes. Coplay reports a successful Tundra build after the source edits and the Editor Console returns zero errors. A Play Mode QA attempt did not reach a settled state: the Editor remained in its play-mode transition and `PartManager` was null. Play Mode was stopped and `Application.isPlaying` was verified false. The existing gameplay QA log's SHA256 was unchanged. Play Mode behavior QA and Android profiling remain pending.

# ECS elastic-part completion — 2026-11-08

gate: passed | mode=直接做 | oracle=APK metadata31 + ARM64 instructions + local ECS contracts | criteria=inspect_archive_elastic_parts.py; Bpsim.QA.ElasticPartsEcsQA.Run | round=1 | status=planned | date=2026-11-08

## Scope and acceptance

依据：F_ECS_20261108_01, F_ECS_20261108_02, F_ECS_20261108_03.

- Spring: passive elastic joint instead of touch-driven radial impulse; native Unity.Physics constraints, separately owned free endpoint when no lower partner, stretching visualization and cleanup. Existing upper weld remains a support. Preserve topology/layers and reject mega merging for assemblies requiring elastic relative motion.
- Bellows: component-held trigger/cooldown state, fixed-step ECS impulse, real compression/reinflate applied to visualization without discarding user free-scale. Force profile from APK math; input direction follows the already-implemented local +Y contract. Regular/alien cooldown; no repeated trigger during a cycle.
- Existing gameplay objects remain only compatibility input/description adapters; no new per-part MonoBehaviour/Update loop.
- Prefab components make generated parts take the same path. No changes to save formats or part IDs.

Binary criteria: APK instrument PASS; host assembly compilation without new errors; numerical bellows profile/integration/cooldown tests PASS; runtime bellows gains momentum over an inactive mass-matched control, changes scale and restores it; spring uses soft constraints (not fixed weld), stretches after a relative kick without a touch event, has live visible geometry, and ends with no owned endpoint/joint/Blob leaks; stop/restart and generated-part initialization covered.

## Boundaries

No claim of APK end-to-end simulation/render parity. Boxing glove, balloon collision/popping, fuel-box explosion and remaining electric/visual special variants are outside this vertical slice. Keep their current implementations. Collider compression and APK particle/audio reproduction are deferred until physical/visual assets have their own oracle.

## Coordination and instruments

- Facts: docs/facts.md (append only). Scratch: _codex_work/ecs-part-effects (ignored). Instrument: Tools/BPSim/inspect_archive_elastic_parts.py. Read-only oracle: bpsimarchive.apk.
- Do not take another chat's editor lease. Run live scene-changing QA only after acquiring control; QA restores the pre-test scene via PartManager backup where supported. No shared save-file writes.
- Preserve pre-existing uncommitted changes; no commits, branches or restore points.

## 2026-11-08 implementation decisions

- 依据 F_ECS_20261108_04: regular bellows default force 30, alien index 6 force 120; index 7 remains regular. These are direct BPSS prefab data, not a claim that APK assets are byte-identical.
- Runtime endpoint joints use PartJointInfo.State=1: the host's State=0 path auto-registers every joint in the part union-find, where a free endpoint is not a valid part index. Endpoint joints still participate in physics and break events, but never in the part graph.
- Current host uses PostTransformScale.float3x3 and Unity.Physics.Math.FloatRange; code targets these actual bundled APIs, not a newer Unity package.
- Offline full host-source Roslyn compile passed; production BellowsMath standalone probe passed 9 contracts (force endpoints/quadratic, regular/alien compression, four timesteps, clipped impulse). Live physics/presentation QA remains required.
## 2026-11-08 current handoff / runtime gate not yet satisfied

Implementation and both source compilation modes passed; offline production math probe 9/9 PASS. Fingerprints and explicit unrun runtime status: Logs/elastic-parts-verification.json. The main editor is playing and compiling; the compile request returned started, not completed. MCP rejected the requested Play Mode exit before dispatch. Computer Use failed to connect its native pipe on both initial inspection and one retry. No raw bridge or OS UI bypass used. Human stop of Unity Play Mode is the required next external change; then acquire a fresh lease, confirm compilation and run ElasticPartsEcsQA.Run/Status. Do not mark this work fully runtime-accepted until that report is PASS. New tests preserve the prior build scene and require the user simulation to be stopped.
## 2026-11-08 continuation validation

The balloon/sandbag continuation imported all new assets and finished the prior live gate: ElasticPartsEcsQA 26/26 PASS (`_codex_work/balloon-sandbag-qa/prior-elastic-final-isolated.log`). Fixed right-side bellows fixture topology, added visual PostTransformScale where absent, initialized spring endpoints before first physics and registered initial welds before looking up partners. The fixture runs above imported terrain; >.3 displacement threshold retained. See F_SUSPENSION_20261108_09 and Logs/suspension-verification-final.json for source fingerprints and scope. Prior pending runtime acceptance is superseded by this recorded run, not erased.
# APK/BPLE balloon and sandbag correction — 2026-11-08

gate: passed | mode=改造后做 | oracle=APK metadata/ARM64 + read-only BPLE prefabs/settings/compiled IL + matching runtime controls | criteria=verify_suspension_reference.py; Bpsim.QA.BalloonSandbagEcsQA.Run/Status | round=1 | status=done | date=2026-11-08

## Gate and evidence

依据：F_SUSPENSION_20261108_01, F_SUSPENSION_20261108_02, F_SUSPENSION_20261108_03.

Before gameplay writes, promote a read-only contract instrument and record baseline failures. Decompiled BPLE source guides inspection; only serialized fields, compiled IL/native instructions and numerical/runtime probes authorize claims. Absence of a named APK Sandbag type does not establish absence of all generic sandbag behavior.

## Scope

1. Gas balloons: actual .1 kg balloon bodies, source force/speed-limiter, explicit target and soft tether, per-target balancer and irreversible touch/ground/sharp popping; no radius-based lift. Correct building-vs-simulation visualization.
2. Sandbags: source mass including heavy skin, original build-position offset, rotatable/10-cell eligible target search, real soft tether, visible string, one-shot release and drop impulses. Do not teleport to the hanging frame or simulate a multi-bag prefab as one body.
3. Multi variants: materialize one runtime part/body per bubble/bag, share the source slot identity without overwriting its spatial-map entry, preserve independent interaction and removal. Register all real part-part joints in the part graph; world-only balancer joints stay outside it.
4. Generated parts use the same initialization; simulation teardown owns cloned colliders and runtime bodies/joints. Disable giant rigid merging for configurations requiring independently moving suspension bodies without changing saved user settings.
5. New state belongs in ECS components; physics and visualization belong in systems. Existing PartBehavior objects are input/diagnostic adapters only.

## Acceptance

- Two independent oracle reads: same serialized data, IL hashes and native pure-method instructions. Include source fingerprints; no APK/BPLE pixel/whole-game parity claim.
- Baseline probe distinguishes attached chassis from close-but-unattached control and checks repeated touch/multi-body count.
- Runtime: force limiter and mass/count assertions; source lookup rotation/range/layer; non-neighbor lift; real joints/rope geometry; irreversible pop/release; collision versus unpoppable skin; orphan/stop/restart/generated cleanup. Restore the prior build scene/settings after every suite.
- Host and non-Editor source compiles; focused prior elastic-part regression. Old tests that checked approximation are replaced with oracle-derived predicates, not weakened to make a new output pass.

## Instruments/coordination

- Facts: docs/facts.md (append only). Scratch: _codex_work/balloon-sandbag-reference and _codex_work/balloon-sandbag-qa, both ignored. Tools: Tools/BPSim.
- APK is read-only. BPLE oracle root: D:/rpgvx + magpie/BPunity Project/BPLE 2022.1.9[工程文件], read-only including Library/ScriptAssemblies/Assembly-CSharp.dll.
- Use only the project Unity lease for editor writes. No other chat messages, branches, commits, restore points, user-save writes or cache deletion.

## 2026-11-08 gate refinement

- 依据 F_SUSPENSION_20261108_04 / _05: immutable profiles + compiled IL + native limiter verified; five compiled-structure baseline predicates all fail. Production work can now proceed within these mechanisms. The main editor is leased by sai-build-ui-20261108, so physical baseline is deferred until control is available; no scene/control bypass is permitted. This replaces the blocked scene-only baseline step with a compiled-structure baseline, not with an assertion of observed physical behavior.
- Source data is separate from live source physics: prefab balloon m_mass=0 is a build-time placeholder, while compiled Initialize sets each body to .1 kg. Multi bags preserve their source mass per cloned body, including Sandbag #4=5.
## 2026-11-08 implementation / expanded oracle

- 依据 F_SUSPENSION_20261108_06: BPLE final balancer stiffness is 20; oracle now includes 12 compiled methods, independently identical.
- Initial bodies/siblings/soft joints are assembled before the first physics step; sandbag original position plus variant offset and its Z-plane/interpolation seed are kept consistent.
- Runtime siblings enter simulator.Parts and real part-part tethers enter simulator.Joints without overwriting the shared grid slot. World-only balancers never enter the part graph; external retirement reconciles balancer counts from live ECS bodies.
- Input dispatch writes components directly, including before managed adapter adoption. HUD single-shot groups now dispatch exactly the first enabled entity; no changes to multi-toggle groups.
- Host Roslyn compile passed; Unity compile_status has no errors/warnings. Live probe now proceeds with transient scenes and no user-setting changes.
## 2026-11-08 runtime round 1 / instrument corrections

Native automatic anchors verified in four rotations. Round 1: 14 pass / 8 fail, saved separately; pinning threw on an absent optional PhysicsGravityFactor and cleanup incorrectly counted build entities carrying state without rigid bodies. Corrected the instrument, not acceptance thresholds. A plain compile request does not import newly created scripts; explicit leased AssetDatabase.Refresh is now available through Assets/Editor/Bpsim/SuspensionQaAssets.cs. The failed type-resolution invocation created no QA host/scene; inspected and acknowledged before continuing.
## 2026-11-08 runtime rounds 2/3 / actual defect

35 pass / 6 fail after instrument repair. Focused multi run: initial 3 bodies but 5 remained after one pop; stop left physics state on one build gas/bag. Root cause: queries included build entities while the manager was simulating. Added PartSceneID.Value == SimulationSceneID guards to initialization/update/count reconciliation, plus pristine-build assertions. Retained failing logs; no relaxed cardinality/cleanup tolerances.
## 2026-11-08 accepted final snapshot

F_SUSPENSION_20261108_09: current suspension suite 46/46 PASS and prior elastic suite 26/26 PASS. Final fixture starts above imported level terrain; displacement observation occurs after the force window, with the original >.3 threshold retained. Startup now registers existing State=0 part-part welds before suspension bootstrapping so lower spring partners and initial connectivity are not lost. Host Unity compilation and non-Editor conditional source compilation passed; structural baseline 5/5 PASS. Fingerprints and explicit bounds: Logs/suspension-verification-final.json. Complete for the documented fixture/mechanism scope; arbitrary mid-run injection into an already merged mega body, exact RNG/Z/pixel/audio/particle equivalence and precise compound Sharp-leaf classification remain outside this acceptance.

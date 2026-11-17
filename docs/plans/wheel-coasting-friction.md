gate: passed | mode=改造后做 | oracle=read-only BPLE serialized wheel materials and current compiled factory IL; isolated Unity/Havok/Anvil coasting contracts | criteria=Tools/BPSim/verify_wheel_friction.py + Bpsim.QA.AnvilParityQA.RunWheelFrictionBatch | round=1 | status=running | date=2026-11-08

# Wheel coasting / collider material conversion

- P1 Read BPLE wheel prefab/material contracts twice; retain the compiled pre-fix factory IL. The existing templates already contain low wheel friction, but CreateColllider never reads sharedMaterial.
- P2 Preserve authored friction/restitution at each newly created leaf collider, before compound construction. Do not tune the ground, engine force, body damping or sticky-wheel adhesion.
- P3 Use a documented portable mapping: PhysX Multiply -> DOTS Minimum (DOTS has no Multiply); PhysX Average -> the existing DOTS GeometricMean so it cannot override the low-friction policy. Preserve Minimum/Maximum. This is not exact PhysX mixing or a full BPLE trajectory-parity claim.
- P4 Test null-material compatibility, all policies, box/sphere/capsule conversion, support-vs-tread materials, compounds and scaling. With rotation locked like the game's visually rotating wheel colliders, impart 5 m/s once on a flat floor and sample after 3 s: low-friction wheel > 3.3 m/s, default-friction negative control < 0.5 m/s, nonincreasing horizontal speed and grounded vertical position. Repeat independently on Unity/Havok/Anvil. A/A tolerance 1e-4 m/s and 1e-4 m.
- P5 Compile current full host sources and non-Editor sources without writing production Library; inspect Unity compilation when its existing controller has released it. The live editor is occupied by balloon/sandbag QA; do not interrupt it or overwrite its scene/settings.

Isolation: scratch `_codex_work/wheel-friction`; instruments `Tools/BPSim/verify_wheel_friction.py` / `Tools/WheelFriction`; oracle `D:/rpgvx + magpie/BPunity Project/BPLE 2022.1.9[工程文件]` is read-only. Facts use unique `F_WHEEL_20261108_*` IDs in the shared append-only ledger.

## Execution amendments (2026-11-08)

- P4: The physics-only dependency slice cannot initialize a full Entities World: TypeManager.GatherSharedComponentMethods fails on the deliberately absent game metadata. Do not patch/stub TypeManager. Move the existing authored shape conversion into the reusable production PartPhysics.CreateCollider entry point; the factory delegates to it and tests call that exact entry directly. Complete production source compilation still checks the ECS integration. This also removes the temporary rendering-Context stub from the final instrument.
- P5: An unrelated, in-progress BalloonSystem edit briefly prevented full compilation. Do not edit that other task's file; retry the full source compile after it settles. QA is Editor-only like the existing AnvilParity harness.

## Completion — 2026-11-08

gate: passed | mode=改造后做 | oracle=unchanged scoped BPLE material data and real isolated solver contracts | criteria=unchanged | round=1 | status=done | date=2026-11-08

- P1-P5 complete: two oracle reads agree; two private Unity processes pass 60/60 checks each, A/A maximum metric difference 0; seven checker corruption self-tests pass; current full host/non-Editor source compiles pass (425 source files).
- Final scope and compatibility limits are recorded in `wheel-coasting-friction-results.md`. The active Unity controller was never interrupted. Metric clarification: coasting monotonicity uses speed magnitude, so deceleration from a tiny negative velocity to rest is not reported as acceleration; no tolerance changed.

- Cleanup: the runtime policy rejected guarded private-project recursive cleanup before dispatch. Do not bypass with another shell/helper; D-drive ignored test scratch is retained. Final reports and source fingerprints are already in Tools/WheelFriction/reports.

## Round 2 — actual provider, 2026-11-08

gate: passed | mode=改造后做 | oracle=actual PartResources.PartGroup assets + exact JSON/Innovation profile correspondence + real prefab/vehicle solver output | criteria=Tools/WheelFriction/verify_prefabs.py --expect-fixed + real-asset coasting + WheelCoastingRuntimeQA | round=2 | status=running | date=2026-11-08

- User reports wheels still stick. F_WHEEL_20261108_06 proves the previous fixture bypassed actual prefab material supply: all 76 actual colliders have null materials and PartFactory instantiates those prefabs rather than templates. Restore three PhysicMaterial assets and exact per-leaf references in the five wheel groups. Retain existing conversion helper; do not alter global friction, geometry, damping or wheel propulsion.
- Before/after acceptance must load actual PartGroup prefabs and production baked compound leaves; include Innovation motor #7 and welded chassis. Live editor control was reacquired by the suspension task between status inspection and our acquire request; respect its lease and continue with independent real-asset tests until it releases.

- F_WHEEL_20261108_08: actual-asset material-only run fails NormalWheel on all backends because identity compound frames discard the .3402374 support offset and -.2106609 tread offset. Amend P2/P4: retain source hierarchy-relative positions/rotations for wheel leaves only; retain non-wheel behavior. Preserve the failed material-only report. Source shapes/material coefficients/geometry values remain unchanged.

## Round 2 verification — 2026-11-08

- The actual resource and wheel-only compound-frame corrections are implemented. Exact asset profiles pass 76/76; two independent actual-prefab solver runs pass 194/194 each with A/A floor 0, including six wheel types, stopped old-material controls, and a five-body/four-weld chassis with production .2 damping. Seven negative checker self-tests pass; current full host/non-Editor compiles pass (425 sources).
- A probe-only two-axis inverse-inertia lock caused the reference chassis to collapse onto the floor. Retain the source body's normal physical inertia for welded tests; this is a harness correction, not a gameplay change or relaxed tolerance.
- Source/solver validation is complete for the scoped corrections. The separately proposed live gameplay-session QA remains pending because another controller still owns Unity; no scene, editor control, running regression or user settings were forcibly changed. Details and cache-reload instructions: `wheel-coasting-friction-round2-results.md`.

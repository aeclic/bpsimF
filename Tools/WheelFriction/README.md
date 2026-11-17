# Wheel material / coasting instrument

The source templates and BPLE materials specify tread friction 0.025 or 0.05, but the old factory discarded the material and used DOTS' default 0.5. The production fix supplies the authored material to each leaf constructor before compound creation; original material-less helper overloads retain their default behavior.

## Run

```powershell
python Tools/BPSim/verify_wheel_friction.py --out Logs/wheel-oracle-a.json
python Tools/BPSim/verify_wheel_friction.py --out Logs/wheel-oracle-b.json
python Tools/WheelFriction/run.py --prepare
python Tools/WheelFriction/run.py --label a
python Tools/WheelFriction/run.py --label b
python Tools/WheelFriction/check_reports.py Logs/wheel-friction-a.json Logs/wheel-friction-b.json --self-test
```

Preparation compiles the current full host and non-Editor source sets into D-drive scratch, then creates an isolated physics-only Unity project using the existing AnvilParity dependency preparer. The complete production PartPhysics file is copied into that slice; the factory delegates its authored leaf creation directly to the tested PartPhysics.CreateCollider entry point. No type-registry stubs or live-editor input, scene, saved construction, physics-backend preference or global ground setting is changed.

Contracts: null material, all combine policies, actual box/sphere/capsule factory paths, scaling, independent tread/support compound leaves, and three-second impulse-only coasting versus the old default-material negative control on Unity/Havok/Anvil. Rotation is locked because the game's WheelPivot turns visually rather than providing an independent physical axle. Speed, distance, grounded height and speed gain are read from the real physics solvers. All 60 checks are mandatory; two independent Unity processes must agree within 1e-4 m/s and m. The checker must reject its seven corrupted negative reports.

## Bounded compatibility

DOTS has one friction coefficient, not separate static/dynamic friction, and no PhysX Multiply policy. Conversion preserves BPLE's dynamic coefficient and uses Minimum as a portable Multiply approximation. Average retains DOTS' existing default GeometricMean to keep the intended policy ordering (DOTS ArithmeticMean outranks Maximum). Minimum and Maximum remain themselves. This corrects coasting but does **not** establish exact BPLE contact mixing or complete assembled-vehicle trajectory parity. Sticky adhesion/drag, powered-wheel forces and rigidbody damping are intentionally unchanged.

Changing production factory/helper code, dependency DLLs/native solver, template data or the oracle invalidates the captured reports. Do not call a copied JSON an independent run. Private scratch is `_codex_work/wheel-friction`; retained reports are under `Tools/WheelFriction/reports`. Unity test failures must be inspected, not masked with reference exemptions or increased thresholds. `--skip-host` is only for preparing the independent slice while unrelated source edits are in progress; a final full source compile remains required.

## Actual provider regression (round 2)

The first fixture exposed material conversion but missed the actual provider: PartFactory instantiates PartResources.PartGroups, whose 76 wheel colliders had null material references. The restored three PhysicMaterial assets are assigned per collider in all five actual wheel groups, including Innovation motor #7. The factory also composes wheel collider ancestor positions/rotations instead of placing all leaves at the part origin; otherwise NormalWheel's high-friction support hits the ground below its tread.

```powershell
python Tools/WheelFriction/verify_prefabs.py --out Logs/wheel-prefabs-after.json --expect-fixed
python Tools/WheelFriction/run.py --prepare
python Tools/WheelFriction/run.py --actual --label a
python Tools/WheelFriction/run.py --actual --label b
python Tools/WheelFriction/check_actual_reports.py Logs/wheel-actual-a.json Logs/wheel-actual-b.json --self-test
```

ActualPrefabQA loads the real serialized PartGroup assets in Unity, converts their actual collider components through production PartPhysics, composes wheel-only hierarchy-relative frames in the same order as the compiled factory, and tests the complete compounds. It does not substitute ideal spheres or geometry from JSON. All 76 materials plus six representative wheel types and a five-body/four-weld chassis are checked on Unity/Havok/Anvil. A single 5 m/s push with the production .2 linear damping must still leave >1.4 m/s and >7 m displacement after 3 s; old-friction controls must stop. All 194 checks are mandatory; independent A/A floor is <=1e-4. The chassis retains full physical inertia; zeroing two inertia axes was a probe-only overconstraint that dropped the reference chassis onto the floor and is not a fix to gameplay physics.

Scope remains real-asset/real-solver verification, not a claim of full BPLE assembled-gameplay timing. The live editor's lease must be respected. Re-enter Play Mode or relaunch the game to rebuild PartFactory's cached managed and ECS prefabs after resource changes; restarting an already cached simulation alone is insufficient.

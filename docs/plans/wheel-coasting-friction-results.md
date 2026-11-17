# Wheel coasting result — 2026-11-08

Root cause: BPLE-derived templates already contained low tread friction, but PartEntityFactory constructed every leaf with DOTS' default 0.5. Material conversion now lives in PartPhysics.CreateCollider and is used by the factory; tread/support materials remain separate through compounds and free scaling. Existing material-less shape helper overloads are preserved.

Read-only oracle: 38 tread colliders across CartWheel / NormalWheel / SmallWheel / MotorWheel / StickyWheel families; BPLE materials and local templates agree on dynamic friction 0.025/0.05, zero static friction, Multiply. Two independently launched oracle readers produced identical JSON.

Two independent private Unity processes passed **60/60 contracts each**, including the same floor, one initial 5 m/s push, no subsequent input, locked body rotation and a three-second observation window. All corresponding A/A metric differences were **zero**. Seven checker negative self-tests passed.

| Backend | Regular/powered/sticky tread final speed | Wooden tread final speed | Old-default control final speed | Regular / wooden / control distance |
|---|---:|---:|---:|---:|
| Unity | 4.26414 m/s | 3.52838 m/s | 0 m/s | 13.8889 / 12.7778 / 2.49845 m |
| Havok | 4.26757 m/s | 3.53449 m/s | 0 m/s | 13.9008 / 12.8004 / 2.54406 m |
| Anvil | 4.26421 m/s | 3.52846 m/s | 0 m/s | 13.8890 / 12.7779 / 2.49845 m |

The monotonicity check measures **speed magnitude**, not signed velocity: the stopped Havok negative control briefly crosses zero; returning from a tiny negative velocity to zero is deceleration, not acceleration. No threshold was increased or check exempted.

Final full host and non-Editor source compiles both passed (425 sources). This is a source/conditional compile, not a packaged Player build. Runtime QA is Editor-only. No existing editor session, scene, preference, saved construction, global terrain friction, powered force, sticky adhesion/drag or damping was changed.

Compatibility boundary: DOTS lacks separate static friction and PhysX Multiply. The portable mapping uses Minimum for Multiply and retains DOTS GeometricMean for Average so authored average surfaces cannot override the wheel's low-friction policy. Minimum/Maximum are preserved. **Not a claim of exact BPLE material mixing or full assembled-vehicle trajectories.** The live editor remains under another QA task's control; private real-solver testing avoids interrupting it. Re-enter simulation after the normal Unity compile/reload to instantiate updated colliders.

Reproduction and invalidation rules: `Tools/WheelFriction/README.md`. Source/native/template changes invalidate the reports; independent runs, mandatory coverage, bounds and negative-control results must be rechecked. Retained JSON and full source hashes: `Tools/WheelFriction/reports`.

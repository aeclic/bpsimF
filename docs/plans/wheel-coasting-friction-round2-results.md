# Actual wheel provider and support-contact correction — 2026-11-08

The user's "still sticks" report was valid. The first test supplied a material to an ideal collider and therefore bypassed two actual production failures:

1. PartFactory creates wheels from PartResources.PartGroups, not JSON TemplateCollection. All **76 actual wheel colliders** in the five PartGroup prefab assets had null material references. Three PhysicMaterial assets now restore the exact regular/Innovation template tread and support profiles per leaf.
2. PartEntityFactory.BuildCompoundCollider previously assigned identity to every child. For NormalWheel this put the support bottom at **-0.49 m**, below the tread at **-0.325 m**. Composing the source child poses instead gives support **-0.1497626 m** and tread **-0.5356609 m**. The high-friction support no longer becomes the tire. The factory now composes LocalTransform/Parent poses for wheel leaves only; it excludes the root's build/world pose. Non-wheel behavior and source geometry values are unchanged.

Two independent serialized reads agree exactly: **0/76** valid prefab materials before repair, **76/76** afterward. The motor #7 off-road profile is included from Innovation templates.

## Real-asset / real-solver results

ActualPrefabQA loads the actual Unity PartGroup assets, creates complete collider compounds through production PartPhysics, and tests six wheel types plus a five-body/four-weld chassis. Bodies use the production **0.2 linear damping**, unlike the original isolated tread test. One initial **5 m/s** push, no subsequent input, **3 s** observation:

| Backend | NormalWheel speed / travel | Wooden wheel speed / travel | Welded chassis speed / travel |
|---|---|---|---|
| Unity | 2.18881 m/s / 10.3766 m | 1.63703 m/s / 9.45727 m | 1.85634 m/s / 9.82299 m |
| Havok | 2.19115 m/s / 10.3872 m | 1.64143 m/s / 9.47792 m | 1.64120 m/s / 9.47694 m |
| Anvil | 2.18887 m/s / 10.3768 m | 1.63706 m/s / 9.45732 m | 1.62440 m/s / 9.45731 m |

Old-friction controls stop after approximately **2.21-2.25 m**. The intermediate material-only correction still stopped NormalWheel after **1.895-1.942 m**, proving material restoration alone was insufficient; that failed report is retained.

Final independent processes pass **194/194 checks each**, A/A maximum metric difference **0**; seven actual-report checker corruption self-tests pass. Full current host and non-Editor source compiles both pass (425 sources). Reports, before/after data and source fingerprints are retained under `Tools/WheelFriction/reports`.

The chassis test retains normal physical inertia. A probe-only attempt to lock two inverse-inertia axes overconstrained Unity/Havok weld solving, dropping the chassis onto the ground; removing that artificial probe modification fixes the test without changing gameplay, tolerances or exemptions.

## Limits / apply the fix

The live editor remains leased by another task's suspension regression; it was not interrupted, and a separate full gameplay-session test has **not** run. The mandatory real-asset/real-solver tests above are independent of that session. They are not full BPLE timing or exact PhysX Multiply parity. Current wheel collider ancestry has unit scale; existing free-root scaling still uses CreateScaled. No global floor friction, propulsion, adhesion/drag or damping value was lowered.

**Fully exit and re-enter Play Mode, or relaunch the game after rebuilding, to refresh PartFactory's managed/ECS prefab caches. Merely stopping and starting a cached simulation does not rebuild the catalog.**

Reproduction: `Tools/WheelFriction/README.md`, `verify_prefabs.py --expect-fixed`, `run.py --actual --label a/b`, `check_actual_reports.py ... --self-test`. Source/data/native changes invalidate these snapshots.

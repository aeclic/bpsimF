gate: passed | mode=直接做 | oracle=managed source and existing Anvil benchmark logs | criteria=Unity compile succeeds; a batch Anvil run completes with no world-init errors, belowGround=0, and no native crash | round=1 | status=running | date=2026-11-06

# Anvil Stability and Performance

## Tasks

- [x] T1 Release native mesh handles when their static body is replaced or removed. Evidence: F01.
- [x] T2 Disable the unused Unity.Physics broadphase tree refresh by default in Anvil mode. Evidence: F02.
- [ ] T3 Run the Unity compile check and existing Anvil batch benchmark, then update the evidence and measurements.

## Acceptance

Unity reports no compile errors. The benchmark reaches `=== done ===`, reports `belowGround=0`, and produces no Anvil native crash. Compare `tree=0` timings with the saved baseline; the broadphase phase should remain zero unless explicitly enabled.

## Decision

The retained project log contains ECS benchmark-world initialization and teardown exceptions, not an Anvil native access violation. The native DLL has no source in this checkout, so changes are restricted to verified managed lifecycle and broadphase behavior.

# BPIR import fidelity and cost

gate: passed | mode=直接做 | oracle=BPIR Gitee b1c3f9c16b8691612342a31d074933ff7252dfe5, 10.4.1 assets, bpsim ECS readback | criteria=Bpsim.QA.BpirImportQA.Run and MeasureImport | round=1 | status=done | date=2026-11-18

Local ledger for this round (`docs/facts.md` is claimed by another task):

| ID | Claim and confidence | Falsifiable prediction / check | Status |
|---|---|---|---|
| BPIR-F1 | High: bpsim preserves AutoControlLight mode 4 in its grid state but shows the regular model in build mode. | Import 44/6 with modes 0 and 4. The visible inactive child must differ, and both root facing vectors must point right. Inspect the actual linked render entities. | 已证（行为） |
| BPIR-F2 | High: 26/4..7 loses the BPIR offset-layout bit when converting it to an angle; flip and continuous rotation do not commute. | Read back hinge grid code, both plate positions, and root facing/mirror axes after loading; compare to Gitee `HingePlate.SetRotation`, `SPDTSwitchPart.SetRotation` and `PartPhysicsOverride`. | 已证（行为） |
| BPIR-F3 | High: CSV splitting allocates per column on every row and synchronous full scene import repeats attachment refreshes. | Measure seven warm imports of the same save and compare median parser and import time; verify placed counts and use a final-state attachment control. This is Editor import cost, not Player FPS. | 已证（耗时与数量；GC 字节不可用） |

## Verification (2026-11-18)

- BPIR-F1: verified in Play Mode by `Bpsim.QA.BpirImportQA.Run`. Modes 0 and 4 select different inactive sprites for 44/6, both facing right. Kicker selects the marker child without rotating the whole root. Compare Gitee `AutoControlLight.SetRotation` and `Kicker.SetColorMark` at the pinned revision.
- BPIR-F2: verified by the same QA. Both plates move down 0.5 units for the offset layout, the encoded mode survives export, and the mirrored switch's 30-degree fine rotation is applied in the parent plane. This validates ECS transforms and visibility, not pixel or hinge-physics parity.
- BPIR-F3: verified for elapsed time and placed count. CSV fields are span slices, map capacity is reserved for fresh imports, and attachment direction is baked once rather than scanned by entity name. The attachment control puts the same-layer neighbour after the hook and the opposite neighbour on another layer. The Unity Mono thread-allocation counter returns 0 and is unavailable; no measured GC-byte improvement is claimed.
- Skin indices: no evidence for a blanket `+1` or `-1` remap. The source factory uses serialized `customPartIndex`, not the prefab filename suffix; all type/index pairs in the four local fixtures exist in bpsim. Appearance checks above cover mode-dependent artwork, not every custom skin or add-on.

`Bpsim.QA.BpirImportQA.MeasureImport("天使座")`, same Unity Editor/project, one warmup and median of seven measured runs per call. Input is read into memory; the total covers synchronous `PartManager.LoadScene`, not rendering, physics, or unloading. 20,405 rows parsed and 20,405 parts placed in each run.

| Build | Parser median (ms) | Total import median (ms) |
|---|---:|---:|
| Before, run A | 21.11 | 211.62 |
| Before, run B | 24.85 | 210.13 |
| After, run A | 13.38 | 126.51 |
| After, run B | 13.21 | 127.30 |

Regression result: 67 BPIR checks and 28 layer-connection checks. Unity compile status reports no C# errors or warnings. No save files changed.

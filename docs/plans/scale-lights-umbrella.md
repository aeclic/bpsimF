# Whole-machine scale and BPLE light/umbrella repair

gate: passed | mode=改造后做 | oracle=read-only BPLE 2022.1.9 settings/prefab dump plus original behavior contracts and Unity runtime probes | criteria=Tools/BPSim/verify_bple_lights_umbrella.py; Bpsim.QA.ScaleLightsUmbrellaQA.Run | round=1 | status=done | date=2026-11-06

The screenshot is bug evidence, not instructions. Existing worktree changes are retained; no branches, commits, restore points, user-save overwrite or Unity scene asset saves are part of this task.

## Gate and bounded acceptance

- F03: reproduce before production edits; compare the build pose with the *immediate* simulation pose before physics advances. Include small/large scales, arbitrary pivots, non-uniform scale, continuous rotation, layer depth, same-frame edits and child visuals.
- F04: independently parse original BPLE serialized data instead of using BPSS placeholder comments as an oracle. Cover every available part44/45 variant; actual emitted geometry/collisions, on/off states, single-part fallbacks, night vision and cleanup. The visual oracle does not prove pixel identity across the different renderers.
- F05: check original BPLE input grouping, separate angle slider, connected/detached eligibility, stroke timing/curve and force/mass data; avoid changing unrelated toolbar work.
- Test scenes are temporary. Preserve and reselect the user's build scene; restore simulation settings and do not change production saves.

## Tasks

1. Add reusable oracle parser and isolated Unity regression instrument; record baseline failures (F03-F05).
2. Share build/simulation free-transform composition and seed both pose and visual scale before joints/rigid bodies (F03).
3. Implement BPLE light profiles, visible emitted geometry, real contact/illumination and night-vision lifecycle, preserving variant-specific behavior (F04).
4. Align umbrella stroke, aerodynamic/control formulas, mass, button direction/component grouping and angle slider with BPLE (F05).
5. Run compile/runtime regressions twice independently, inspect images, document measurement floor/boundaries and restore the user scene.

## Machine criteria

- `python Tools/BPSim/verify_bple_lights_umbrella.py --oracle <read-only BPLE root>`: original defaults/profiles and template properties match.
- `invoke_static type=Bpsim.QA.ScaleLightsUmbrellaQA method=Run args=["all"]`, then `Status`: zero failed checks; report in `Logs/bpsim-scale-lights-umbrella.log`.
- Unity console/compile state: no new compile errors or exceptions attributable to this patch.
- `git diff --check`; `git diff --numstat -- docs/facts.md docs/plans/scale-lights-umbrella.md` and fact-id uniqueness.

## Environment and control

- Unity `D:/folder/2022.2.23f1/Editor/Unity.exe`; main bridge discovers port via `Temp/DshBridge/instance.json` (8790 this run). Pinned MCP still points at obsolete 8791.
- The fallback client reuses `Tools/UnityMcp/bridge.py` and **the same** cross-process control lease/state; adds the v0.2 project identity header/body and keeps the token private. No lock bypass or automatic write retry.
- Oracle: `D:/rpgvx + magpie/BPunity Project/BPLE 2022.1.9[工程文件]`; verified source equality with the vendored upstream under the parent `Tools/BPRebooted/upstream/BPLE-2022.1.9` for Umbrella/PointLight/SpotLight/EntityLight. Oracle is read-only.

## Change records (append-only)

- 2026-11-06 / F06: upgraded collision oracle from source inspection to execution of original compiled BPLE pure methods in an isolated .NET process. The production kernel agrees on all 4,000 cases with maximum numeric error 0; original A/A fixture hashes agree.
- 2026-11-06 / F07: INSettings uses Exp declarations/defaults, so the parser was corrected to read `INDeclarationSettingsExp` and `INSettingsBExp`; principal light/umbrella profiles were unchanged, and experimental reaction/explosion defaults were confirmed false.
- 2026-11-06 / F08: BPLE's two extra point variants are already in the Innovation group. Removed duplicate prototype catalog additions and retained existing art/templates. No catalog expansion is necessary.
- 2026-11-06: main Unity was no longer running during the native-oracle phase. Reopened the same project in the background. After lease expiry, inspected fresh PID 29016, original PID 34932 exited, idle Edit Mode and empty console, then acknowledged a disconnected old isolated-physics request. No outcome of that other request is claimed.
- 2026-11-06: hidden-editor terminal edits do not automatically discover new scripts; registered the unchanged new QA script through the leased bridge `write_script` command to perform AssetDatabase.Refresh. Compilation completed without errors.
- 2026-11-06 / F04-F05: first complete Unity run passed 224 checks; added query-only pillar trigger colliders so physics ray callers can see enabled light platforms, and retained original umbrella completion sounds. Final expanded run passed 232 checks, including four pillar query/raycast cases.
- 2026-11-06 / F04: original night-vision shader/material inspection showed additive vignetted green, not grayscale. Updated the port to the original .05/.4264706 tint, .6/.3 vignette and .5568628 night-mask alpha. No universal pixel-equivalence claim is made.
- 2026-11-06 / final acceptance: independent final Play Mode runs A and B both passed 232/232 checks; each compared 4,000 original compiled BPLE cases with zero discrete mismatches and max numeric error 1.907349E-06. No new compile errors/runtime exceptions were observed. `git diff --check` passed and fact IDs are unique with original F01/F02 retained. Status: done for the bounded repair/technical criteria above; universal renderer-pixel equivalence remains outside the measured claim.

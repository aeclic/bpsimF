gate: passed | mode=直接做 | oracle=running level plus frozen-time GPU near-edge vertices | criteria=node Tools/LevelEditor/verify_water_wave_anchor.mjs | round=2 | status=done | date=2026-11-15

The user clarified that the bright cyan wave line adjoining the deep blue water must stay fixed against terrain. The previous change anchored the distant flat edge instead.

- WWA01: Reproduce the near-edge drift before changing production shaders.
- WWA01: Anchor the original z=0 wave edge at authored water height. Let the distant edge project up/down with signed camera height; preserve finite X bounds and underwater coverage.
- WWA02: Freeze wave time and test corresponding near-edge world vertices across camera heights, pan, zoom and crossing the bank. Keep nonzero wave amplitude.
- Rebuild and install the Mod after preserving and validating any current construction. Restore camera/session after validation.

Facts: docs/water-wave-facts.md. Probe output: _codex_work/water-wave-anchor/. Unity control is through the exclusive bpsim-unity lease. Runtime camera restoration uses SceneCamera.MoveTo/ScaleTo with the saved camera values; no Play Mode exit before verified construction capture.

Completed: WWA01/WWA02. The near wave edge is anchored, while the far surface and underwater seam use signed camera height. WebGL camera cases (10), frozen wave checks, ray parity (max 1 RGBA8 step), Unity bounds/sorting checks (23) and canvas checks (29) pass. Production package and installed Mod have been updated. Before leaving Play Mode the current construction had 0 parts and its saved CSV had 0 part rows; the current level and camera were restored afterward.

The attempted same-invocation URP render request reused capture state, so its images were rejected and the helper was removed. Actual runtime screenshots use the normal game loop (`wave-anchor-after-above.png`, `wave-anchor-after-below.png`); the exact frozen-time drift metric comes from GPU transform feedback, not those animated screenshots.

2026-11-15 correction (WWA03): The below-water runtime screenshot exposes a second cyan line on the displaced underwater pass. Split its original meniscus shading into an anchored early pass below water, behind parts, and suppress the displaced copy. Extend verification to include the meniscus pass before final acceptance.

Completed WWA04: nonzero cyan raster coverage is anchored, displaced copy absent, and Unity confirms parts draw above both surface and meniscus. Final checks: 16 wave/meniscus tests, 10 camera cases, 25 Unity checks. Final source/package/installed hashes match for 14 changed core files (_codex_work/water-wave-anchor/install-check.json).

Final editor state: live level is running with the final installed Mod. Camera is at waterY-4 for the final below-water screenshot. Unity then entered a pending compilation state (isCompiling=true, no compilation errors recorded); the MCP correctly rejected camera restoration without dispatching it. Saved camera remains in runtime-session.json: x379.2329,y48.12903,size12.5467052. RestoreCamera() can restore it once Unity is available. No pending uncertain writes.

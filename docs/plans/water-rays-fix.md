gate: passed | mode=直接做 | oracle=recorded WebGL draw inputs and captured textures | criteria=node Tools/LevelEditor/probe_water_rays.mjs | round=1 | status=done | date=2026-11-15

The ray shader is replayed against the original captured shader with identical inputs. Its output already agrees within one RGBA8 step. The missing contribution is in composition: the captured demo's local underwater surface map contains rays, while the port supplies a ray-free downsample of the scene and only adds rays to the underwater pass's sorting-layer texture.

- Preserve the ray shader arithmetic and geometry (WR01).
- Include the ray layer when sampling the local underwater surface map, on both backends (WR02).
- Verify isolated ray parity and a controlled local underwater texture contribution.

2026-11-15: The host centres the surface around its physical waterline. Match this Y translation in the ray projection and use signed camera height; isolated source parity remains within one RGBA8 step after the documented translation (WR03). Unity compilation and whole-scene capture remain part of the parent task's validation.

FACTS_FILE: docs/water-ray-facts.md. Probe outputs: _codex_work/water-rays-probe/. The source oracle is read-only at _codex_work/water-reference/draw-inputs.json.

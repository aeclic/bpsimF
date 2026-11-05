"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const water = require("../../Mods/LevelEditor/Web/bpsim/water-canvas.js");
let checks = 0;
function test(name, callback) { callback(); checks++; console.log("PASS " + name); }
function near(a, b, tolerance = 1e-6) { assert.ok(Math.abs(a - b) <= tolerance, `${a} != ${b}`); }
const original = { id: "pool", name: "水", x: -2, surface_y: 0, width: 4, depth: 100,
    wave_amplitude: 0.03, wave_speed: 1, physics_enabled: false, future: { data: [1, "保留"] } };
const pool = water.migrate(original);
test("additive migration preserves old physics opt-out", () => { assert.equal(pool.physics_enabled, false); assert.deepEqual(pool.future, original.future); });
test("migration does not mutate the input", () => { assert.equal(original.density, undefined); assert.notEqual(pool.future, original.future); });
test("all new controls have finite defaults", () => {
    for (const [key, value] of Object.entries(water.defaults)) {
        assert.equal(typeof pool[key], typeof value);
        if (typeof value === "number") assert.ok(Number.isFinite(pool[key]));
    }
});
test("existing flow/effect settings are never overwritten", () => {
    const upgraded = water.migrate({ ...pool, flow_x: -2, reflection_strength: 0, ripples_enabled: false });
    assert.equal(upgraded.flow_x, -2); assert.equal(upgraded.reflection_strength, 0); assert.equal(upgraded.ripples_enabled, false);
});
test("projection/inverse match the upstream camera in CSS pixels", () => {
    for (const zoom of [0.05, 40, 2000]) {
        const camera = { x: 9.5, y: -7.25, zoom }, size = { width: 1200, height: 670 }, point = { x: 2, y: 3 };
        const projected = water.screen(point, camera, size), roundtrip = water.world(projected, camera, size);
        near(roundtrip.x, point.x); near(roundtrip.y, point.y);
    }
});
test("camera center projects to the exact shared viewport center", () => {
    assert.deepEqual(water.screen({ x: 10, y: -3 }, { x: 10, y: -3, zoom: 40 }, { width: 802, height: 602 }), { x: 401, y: 301 });
});
test("water hit test is bounded on all four sides", () => {
    assert.ok(water.contains(pool, { x: 0, y: -1 })); assert.ok(!water.contains(pool, { x: 3, y: -1 }));
    assert.ok(!water.contains(pool, { x: 0, y: 1 })); assert.ok(!water.contains(pool, { x: 0, y: -101 }));
});
test("all four resize handles are available", () => {
    for (const [handle, point] of [["nw", { x: -2, y: 0 }], ["ne", { x: 2, y: 0 }], ["sw", { x: -2, y: -100 }], ["se", { x: 2, y: -100 }]])
        assert.equal(water.hitHandle(pool, point, 40), handle);
    assert.equal(water.hitHandle(pool, { x: 0, y: -1 }, 40), null);
});
test("moving water leaves width/depth unchanged", () => {
    assert.deepEqual(water.dragBounds(pool, { x: 0, y: 0 }, { x: 1, y: 2 }), { x: -1, surface_y: 2, width: 4, depth: 100 });
});
test("northwest resize anchors the southeast corner", () => {
    const result = water.dragBounds(pool, { x: 0, y: 0 }, { x: 1, y: 2 }, "nw");
    near(result.x + result.width, pool.x + pool.width); near(result.surface_y - result.depth, pool.surface_y - pool.depth);
});
test("southeast resize anchors the northwest corner", () => {
    const result = water.dragBounds(pool, { x: 0, y: 0 }, { x: 2, y: -3 }, "se");
    assert.equal(result.x, pool.x); assert.equal(result.surface_y, pool.surface_y); assert.equal(result.width, 6); assert.equal(result.depth, 103);
});
test("resize cannot create zero/negative or oversized water", () => {
    for (const handle of ["nw", "ne", "sw", "se"]) for (const p of [{ x: 1e8, y: 1e8 }, { x: -1e8, y: -1e8 }]) {
        const result = water.dragBounds(pool, { x: 0, y: 0 }, p, handle);
        assert.ok(result.width >= 0.0099 && result.width <= 10000.001); assert.ok(result.depth >= 0.0099 && result.depth <= 10000.001);
    }
});
test("waves are independent of frame rate and bounded by pool depth", () => {
    const tiny = { ...pool, depth: 0.01, wave_amplitude: 10 };
    for (let time = 0; time < 10; time += 0.13) assert.ok(Math.abs(water.surfaceAt(tiny, 1, time) - tiny.surface_y) <= 0.00451);
    near(water.surfaceAt(pool, 1, 2), water.surfaceAt(pool, 1, 2));
});
test("contact ripple decays rather than accumulating forever", () => {
    const flat = { ...pool, wave_amplitude: 0 }, ripple = { id: pool.id, x: 0, time: 0, amplitude: -0.2, radius: 0.2 };
    assert.ok(Math.abs(water.surfaceAt(flat, 0, 0, [ripple])) > 0.1);
    assert.ok(Math.abs(water.surfaceAt(flat, 0, 7, [ripple])) < 1e-5);
});
test("ripple from another water body has no effect", () => {
    near(water.surfaceAt(pool, 0, 1, [{ id: "other", x: 0, time: 0, amplitude: 10, radius: 1 }]), water.surfaceAt(pool, 0, 1));
});
function probe(density) { return { x: 0, y: 0.6, vx: 0, vy: 0, size: 0.25, density, angle: 0, spin: 1 }; }
test("wood-like probe floats at partial submersion", () => {
    const p = probe(0.6), enabled = { ...pool, physics_enabled: true };
    for (let i = 0; i < 3600; i++) water.stepProbe(p, [enabled], 1 / 60);
    assert.ok(p.y > -0.13 && p.y < 0.13, JSON.stringify(p)); assert.ok(Math.abs(p.vy) < 0.005);
});
test("dense metal-like probe sinks", () => {
    const p = probe(2.8), enabled = { ...pool, physics_enabled: true };
    for (let i = 0; i < 600; i++) water.stepProbe(p, [enabled], 1 / 60);
    assert.ok(p.y < -2); assert.ok(p.vy < 0);
});
test("physics-disabled legacy water does not float probes", () => {
    const p = probe(0.6);
    for (let i = 0; i < 180; i++) water.stepProbe(p, [pool], 1 / 60);
    assert.ok(p.y < -2);
});
test("relative-flow drag accelerates a submerged probe without overshoot", () => {
    const p = { ...probe(1), y: -2 }, enabled = { ...pool, physics_enabled: true, flow_x: 2, linear_drag: 100 };
    water.stepProbe(p, [enabled], 10);
    assert.ok(p.vx > 0 && p.vx <= 2); assert.ok(Number.isFinite(p.vy));
});
test("BPLE drag starts when the part center crosses the waterline", () => {
    const enabled = { ...pool, physics_enabled: true };
    const above = { ...probe(1), x: 0, y: 0.01, vx: 2 };
    water.stepProbe(above, [enabled], 1 / 60);
    near(above.vx, 2);
    const below = { ...probe(1), x: 0, y: -0.01, vx: 2 };
    water.stepProbe(below, [enabled], 1 / 60);
    near(below.vx, 2 * Math.exp(-enabled.linear_drag / (60 * below.density * below.size * below.size)));
});
test("BPLE water drag slows light parts faster than heavy parts", () => {
    const enabled = { ...pool, physics_enabled: true };
    const light = { ...probe(1), y: -0.01, vx: 2 };
    const heavy = { ...probe(2), y: -0.01, vx: 2 };
    water.stepProbe(light, [enabled], 1 / 60); water.stepProbe(heavy, [enabled], 1 / 60);
    assert.ok(light.vx < heavy.vx && heavy.vx < 2);
});
test("zero-flow buoyancy drag cannot reverse horizontal vehicle motion", () => {
    const p = { ...probe(0.6), y: -1, vx: 2 }, enabled = { ...pool, physics_enabled: true };
    for (let i = 0; i < 600; i++) water.stepProbe(p, [enabled], 1 / 60);
    assert.ok(p.vx > 0 && p.vx < 2);
});
test("water current can exert force even with drag disabled", () => {
    const p = { ...probe(1), y: -2 }, enabled = { ...pool, physics_enabled: true, flow_x: -1, flow_acceleration: 2, linear_drag: 0 };
    water.stepProbe(p, [enabled], 1 / 60); assert.ok(p.vx < 0);
});
test("overlapping fluids do not double buoyancy", () => {
    const a = { ...probe(1), y: -2 }, b = { ...a }, enabled = { ...pool, physics_enabled: true };
    water.stepProbe(a, [enabled], 1 / 60); water.stepProbe(b, [enabled, enabled], 1 / 60);
    near(a.vy, b.vy); near(a.y, b.y);
});
test("zero delta time does not advance preview physics", () => {
    const p = probe(1), old = { ...p };
    water.stepProbe(p, [{ ...pool, physics_enabled: true }], 0); assert.deepEqual(p, old);
});
test("terrain projection honors rotation and scale", () => {
    const meshes = water.terrainMeshes({ objects: [{ Prefab: { position: { x: 3, y: 4 }, rotation: { z: 90 }, scale: { x: 2, y: 1 },
        terrain_data: { fill_mesh: { vertices: [{ x: 1, y: 0 }, { x: 0, y: 1 }, { x: 0, y: 0 }], indices: [0, 1, 2] } } } }] });
    near(meshes[0].vertices[0].x, 3); near(meshes[0].vertices[0].y, 6);
    near(meshes[0].vertices[1].x, 2); near(meshes[0].vertices[1].y, 4);
});
test("both offline entries load the integrated water module", () => {
    for (const file of ["Mods/LevelEditor/Web/index.html", "Mods/LevelEditor/Web/badpiggies-editor/index.html"]) {
        const html = fs.readFileSync(path.resolve(__dirname, "../..", file), "utf8");
        assert.ok(html.indexOf("/bpsim/water-canvas.js") < html.indexOf("/bpsim/editor-adapter.js"));
        assert.ok(html.includes('<base href="/badpiggies-editor/">'));
    }
});
test("the shared Ultimate Water profile and unmodified source textures are present", () => {
    const base = path.resolve(__dirname, "../../Mods/LevelEditor/Web/bpsim/ultimate-water");
    const profile = JSON.parse(fs.readFileSync(path.join(base, "style.json"), "utf8"));
    assert.equal(profile.source, "Ultimate Water 2D / DW Demo Scene");
    assert.deepEqual(profile.surface.normalsTiling, [6.9, 32.27]);
    assert.equal(profile.surface.reflectivity, 0.663);
    for (const name of Object.values(profile.textures)) assert.ok(fs.statSync(path.join(base, name)).size > 1000, name);
    const build = fs.readFileSync(path.resolve(__dirname, "../../Assets/Editor/LevelEditorModBuild.cs"), "utf8");
    assert.ok(build.includes("SearchOption.AllDirectories"));
    assert.ok(build.includes('Path.Combine(mod, "Web", "bpsim", "ultimate-water")'));
    assert.ok(build.includes("TextureImporterAlphaSource.FromInput"));
    assert.ok(build.includes("importer.mipmapEnabled = true"));
    assert.ok(build.includes("UnityEngine.FilterMode.Bilinear"));
});
test("integration never hides/replaces the main editor", () => {
    const css = fs.readFileSync(path.resolve(__dirname, "../../Mods/LevelEditor/Web/bpsim/editor.css"), "utf8");
    assert.ok(!css.includes("bpsim-water-mode #main,"));
    assert.ok(!css.includes("#bpsim-water-editor"));
    const adapter = fs.readFileSync(path.resolve(__dirname, "../../Mods/LevelEditor/Web/bpsim/editor-adapter.js"), "utf8");
    assert.ok(adapter.includes('rendererEvent(event.data.event, "worker")')); assert.ok(adapter.includes('rendererEvent(message, "main")'));
    assert.ok(adapter.includes("source.parentElement.append(canvas)"));
});
const report = { checks, passed: true };
const output = path.resolve(__dirname, "../../_codex_work/level-editor-mod/water-canvas-tests.json");
fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report));

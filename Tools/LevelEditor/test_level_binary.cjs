const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const binary = require("../../Mods/LevelEditor/Web/bpsim/level-binary.js");

const project = path.resolve(__dirname, "../..");
const levels = path.join(project, "Assets/StreamingAssets/BpsimLevels");
const index = JSON.parse(fs.readFileSync(path.join(levels, "index.json"), "utf8"));
const report = { localLevels: 0, upstreamFixtures: 0, originalOverrides: 0, byteRoundtrips: 0, edgeCases: 0 };
function roundtrip(file) {
    const bytes = fs.readFileSync(file);
    const parsed = binary.parse(bytes);
    const restored = Buffer.from(binary.serialize(parsed));
    assert.equal(restored.compare(bytes), 0, `Byte roundtrip failed: ${file}`);
    for (const node of parsed.objects) if (node.Prefab?.override_data) report.originalOverrides++;
    assert.equal(Buffer.from(binary.fromBase64(binary.toBase64(bytes))).compare(bytes), 0);
    report.byteRoundtrips++;
}
for (const level of index.levels) {
    roundtrip(path.join(levels, level.data)); report.localLevels++;
}
const fixtures = path.join(project, "_codex_work/level-editor-mod/upstream/crates/core/tests/fixtures");
for (const version of ["v1.5.1", "v2.3.6"]) {
    for (const name of ["Level_05_data.bytes", "scenario_58_data.bytes"]) {
        const file = path.join(fixtures, version, "levels", name);
        if (fs.existsSync(file)) { roundtrip(file); report.upstreamFixtures++; }
    }
}
assert.throws(() => binary.parse(new Uint8Array()), /Truncated/); report.edgeCases++;
const sample = fs.readFileSync(path.join(levels, index.levels[0].data));
assert.throws(() => binary.parse(sample.subarray(0, sample.length - 1)), /Truncated/); report.edgeCases++;
assert.throws(() => binary.serialize({ roots: [0], objects: [{ Parent: { name: "cycle", position: { x: 0, y: 0, z: 0 }, children: [0] } }] }), /cyclic/); report.edgeCases++;
assert.throws(() => binary.serialize({ roots: [0], objects: [{ Parent: { name: "empty", position: { x: 0, y: 0, z: 0 }, children: [] } }] }), /empty group/); report.edgeCases++;
const unicode = {
    roots: [0], objects: [{ Prefab: {
        name: "中文名称".repeat(80), prefab_index: 0, position: { x: .5, y: -.25, z: 0 },
        rotation: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 },
        data_type: "None", terrain_data: null, override_data: null, parent: null,
    } }],
};
assert.equal(binary.parse(binary.serialize(unicode)).objects[0].Prefab.name, unicode.objects[0].Prefab.name); report.edgeCases++;
unicode.objects[0].Prefab.position.x = Infinity;
assert.throws(() => binary.serialize(unicode), /Non-finite/); report.edgeCases++;
const output = path.join(project, "_codex_work/level-editor-mod/level-binary-tests.json");
fs.writeFileSync(output, JSON.stringify(report, null, 2) + "\n", "utf8");
console.log(JSON.stringify(report));

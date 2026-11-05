"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
let checks = 0;
function test(name, fn) { fn(); checks++; console.log("PASS " + name); }
class Worker {
    constructor(url, options) { this.url = url; this.options = options; this.listeners = []; }
    addEventListener(type, callback) { if (type === "message") this.listeners.push(callback); }
    message(data) { for (const callback of this.listeners) callback({ data }); }
}
let rendered = null, forwarded = [], tokenStored = null;
const window = { Worker, bpsimLevelBinary: {}, bpsimWaterCanvas: {},
    bpEditorCanvas: { render(scene) { rendered = scene; }, destroy() {}, command() {} } };
const context = vm.createContext({
    window, document: { readyState: "loading", addEventListener() {}, getElementById() { return null; }, querySelector() { return null; } },
    location: { hash: "#bpsim-token=fixture-token" }, sessionStorage: { getItem() { return null; }, setItem(key, value) { tokenStored = value; } },
    URLSearchParams, requestAnimationFrame() {}, console,
});
vm.runInContext(fs.readFileSync(path.resolve(__dirname, "../../Mods/LevelEditor/Web/bpsim/editor-adapter.js"), "utf8"), context);
test("token survives hash stripping without entering diagnostics", () => {
    assert.equal(tokenStored, "fixture-token"); assert.ok(!JSON.stringify(window.bpsimEditorDiagnostics()).includes("fixture-token"));
});
test("scene hook observes but does not alter the upstream payload", () => {
    const scene = { file_name: "Level_01_data.bytes", document_key: "document-1", revision: 3, level: { objects: [], roots: [] } };
    window.bpEditorCanvas.render(scene); assert.equal(rendered, scene); assert.equal(window.bpsimEditorDiagnostics().upstreamKey, "document-1");
});
test("main camera events are observed and forwarded once", () => {
    window.bpRendererEvent = value => forwarded.push(value);
    const message = { type: "camera", x: 7, y: -2, zoom: 41 };
    window.bpRendererEvent(message);
    const diagnostic = window.bpsimEditorDiagnostics();
    assert.equal(diagnostic.cameraSource, "main"); assert.equal(diagnostic.camera.x, 7); assert.equal(diagnostic.camera.zoom, 41);
    assert.deepEqual(forwarded, [message]);
});
test("renderer Worker retains native constructor/instanceof semantics", () => {
    const worker = new window.Worker("/renderer/badpiggies-render-worker.js", { name: "badpiggies-renderer" });
    assert.ok(worker instanceof Worker); assert.equal(worker.listeners.length, 1);
    worker.message({ type: "renderer_event", event: { type: "camera", x: 11, y: 2, zoom: 80 } });
    const diagnostic = window.bpsimEditorDiagnostics();
    assert.equal(diagnostic.cameraSource, "worker"); assert.equal(diagnostic.camera.x, 11); assert.equal(diagnostic.camera.zoom, 80);
});
test("file parsing Workers are not intercepted", () => {
    const worker = new window.Worker("/worker/file.js", { name: "badpiggies-file-worker" });
    assert.equal(worker.listeners.length, 0);
    worker.message({ type: "renderer_event", event: { type: "camera", x: 99, y: 99, zoom: 99 } });
    assert.equal(window.bpsimEditorDiagnostics().camera.x, 11);
});
test("invalid camera payloads never poison water coordinates", () => {
    for (const message of [{ type: "camera", x: NaN, y: 0, zoom: 1 }, { type: "camera", x: 0, y: 0, zoom: 0 }, { type: "selection", x: 99, y: 99, zoom: 99 }])
        window.bpRendererEvent(message);
    assert.equal(window.bpsimEditorDiagnostics().camera.x, 11);
});
test("replacing the canvas host keeps scene observation", () => {
    let observed = null;
    window.bpEditorCanvas = { render(scene) { observed = scene; }, destroy() {} };
    const scene = { file_name: "Level_02_data.bytes", document_key: "document-2", level: { objects: [], roots: [] } };
    window.bpEditorCanvas.render(scene); assert.equal(observed, scene); assert.equal(window.bpsimEditorDiagnostics().upstreamKey, "document-2");
});
test("destroying the upstream canvas invalidates its binding", () => {
    window.bpEditorCanvas.destroy(); assert.equal(window.bpsimEditorDiagnostics().upstreamKey, null);
});
const report = { checks, passed: true };
const output = path.resolve(__dirname, "../../_codex_work/level-editor-mod/editor-adapter-tests.json");
fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report));

// RC2 画笔/水域几何层的可复放判据。
// 断言全部落在 rc2-core.validateScene / validateMesh（浏览器与 C# 两侧共用的约束）上，
// 并且真的往一份 APK 原始关卡里插入节点，而不是只测孤立函数。
//
// 运行：node Tools/LevelEditor/test_rc2_tools.cjs
// 输出：_codex_work/level-editor-mod/rc2-tools-tests.json
const fs = require("fs");
const path = require("path");

const project = path.resolve(__dirname, "..", "..");
const web = path.join(project, "Mods", "LevelEditor", "Web", "bpsim");
const rpp = path.join(project, "Mods", "LevelEditor", "Rc2");

const checks = [];
function check(name, run) {
    try { run(); checks.push({ name, passed: true }); }
    catch (error) { checks.push({ name, passed: false, error: error.message }); }
}
function assert(condition, message) { if (!condition) throw new Error(message); }
function equal(actual, expected, message) {
    if (actual !== expected) throw new Error(`${message || "值不相等"}：期望 ${expected}，实际 ${actual}`);
}
function near(actual, expected, tolerance, message) {
    if (!(Math.abs(actual - expected) <= tolerance)) throw new Error(`${message || "数值偏差过大"}：期望 ${expected}±${tolerance}，实际 ${actual}`);
}

// rc2-core.js 用 UMD（有 module.exports 时走 exports，否则挂 globalThis），
// rc2-tools.js 同时挂 window.bpsimRc2Tools 与 module.exports —— 两种都要认。
const sandbox = { window: {} };
sandbox.globalThis = sandbox;
function load(file) {
    const code = fs.readFileSync(path.join(web, file), "utf8");
    const factory = new Function("window", "module", "exports", "document", code);
    const holder = {};
    holder.exports = {};
    factory(sandbox.window, holder, holder.exports, undefined);
    const exported = holder.exports && Object.keys(holder.exports).length ? holder.exports : null;
    return exported || sandbox.window[(file === "rc2-core.js") ? "rc2Core" : "bpsimRc2Tools"];
}
const core = load("rc2-core.js");
const tools = load("rc2-tools.js");

const catalog = JSON.parse(fs.readFileSync(path.join(rpp, "catalog.json"), "utf8"));
function loadLevel(mission) {
    const entry = catalog.levels.find(level => level.mission === mission) || catalog.levels[0];
    return { entry, document: JSON.parse(fs.readFileSync(path.join(rpp, entry.path), "utf8")) };
}
let counter = 0;
function ids(prefix) {
    counter += 1;
    return { node: `${prefix}_node_${counter}`, renderer: `${prefix}_renderer_${counter}`, mesh: `${prefix}_mesh_${counter}` };
}

check("依赖脚本已加载", () => {
    assert(core && typeof core.validateScene === "function", "缺少 rc2-core");
    assert(tools && typeof tools.terrainMesh === "function", "缺少 bpsimRc2Tools");
    assert(Array.isArray(tools.TERRAIN_GROUND) && tools.TERRAIN_GROUND.length >= 2, "地面材质表为空");
    assert(tools.TERRAIN_SURFACE.length >= 3, "表面材质表为空");
});

check("地形材质键都在 catalog 里", () => {
    const materials = catalog.assets.materials;
    for (const entry of [...tools.TERRAIN_GROUND, ...tools.TERRAIN_SURFACE]) assert(materials[entry.key], "catalog 缺少 " + entry.key);
    assert(materials[tools.WATER_MATERIAL], "catalog 缺少水域材质 " + tools.WATER_MATERIAL);
});

check("多边形形状：矩形/正方形/圆/三角形/薄板", () => {
    equal(tools.polygon("rect", 10, 4, 32).length, 4, "矩形顶点数");
    const square = tools.polygon("square", 10, 4, 32);
    equal(square.length, 4, "正方形顶点数");
    near(square[0][1] - square[3][1], 4, 1e-9, "正方形边长应取长宽较小值");
    equal(tools.polygon("circle", 10, 10, 48).length, 48, "圆顶点数");
    equal(tools.polygon("triangle", 10, 6, 32).length, 3, "三角形顶点数");
    const band = tools.polygon("band", 10, 6, 32);
    equal(band.length, 4, "薄板顶点数");
    assert(band[0][1] - band[3][1] < 6, "薄板厚度应小于高度");
    // 圆必须闭合在原点周围：所有点到中心距离相等
    const circle = tools.polygon("circle", 10, 10, 24), center = tools.centroid(circle);
    near(center[0], 0, 1e-9); near(center[1], 0, 1e-9);
    const radii = circle.map(point => Math.hypot(point[0] - center[0], point[1] - center[1]));
    near(Math.max(...radii), 5, 1e-9, "圆半径");
});

check("地形网格满足 validateMesh", () => {
    const mesh = tools.terrainMesh({ polygon: tools.polygon("rect", 12, 5, 32), shape: "rect" });
    core.validateMesh(mesh);
    equal(mesh.submeshes.length, 2, "地形网格应有本体 + 表面两块子网格");
    equal(mesh.vertices.length % 3, 0, "顶点维度");
    equal(mesh.uv.length, (mesh.vertices.length / 3) * 2, "UV 维度");
    equal(mesh.colors.length, (mesh.vertices.length / 3) * 4, "颜色维度");
    for (const triangles of mesh.submeshes) {
        equal(triangles.length % 3, 0, "三角形索引维度");
        for (const index of triangles) assert(Number.isInteger(index) && index >= 0 && index < mesh.vertices.length / 3, "索引越界 " + index);
    }
    assert(mesh.submeshes[1].length > 0, "表面条带为空");
    equal(mesh.stats.shape, "rect");
});

check("地形网格的 UV 随尺寸缩放（表面条带沿周长展开）", () => {
    const small = tools.terrainMesh({ polygon: tools.polygon("rect", 4, 4, 32), textureScale: 10 });
    const large = tools.terrainMesh({ polygon: tools.polygon("rect", 40, 4, 32), textureScale: 10 });
    const umax = mesh => Math.max(...mesh.uv.filter((_, index) => index % 2 === 0));
    assert(umax(large) > umax(small) * 3, "更大的地形应铺更多次贴图");
    // 矩形周长 16、贴图尺度 10；每段起点算 u，因此最后一段起点是 (16-4)/10，末段由环绕闭合。
    near(umax(small), 12 / 10, 1e-6, "矩形周长 16、贴图尺度 10");
});

check("多边形碰撞体与 APK 地形节点同形", () => {
    const points = tools.polygon("rect", 8, 4, 32);
    const collider = tools.polygonCollider(points, {});
    equal(collider.type, "PolygonCollider2D");
    equal(collider.data.m_Points.m_Paths.length, 1);
    equal(collider.data.m_Points.m_Paths[0].length, points.length);
    near(collider.data.m_Points.m_Paths[0][0].x, points[0][0], 1e-12);
    assert("m_Offset" in collider.data && "m_SpriteTilingProperty" in collider.data, "碰撞体缺少 APK 组件必备键");
    equal(collider.data.m_IsTrigger, false);
    equal(tools.polygonCollider(points, { trigger: true }).data.m_IsTrigger, true);
});

check("水域箱型碰撞体带 m_Size / m_Offset", () => {
    const collider = tools.boxCollider([8, 2], { trigger: true });
    equal(collider.type, "BoxCollider2D");
    equal(collider.data.m_Size.x, 8);
    equal(collider.data.m_Size.y, 2);
    equal(collider.data.m_IsTrigger, true);
    assert("m_Offset" in collider.data && "m_EdgeRadius" in collider.data, "箱型碰撞体缺少 APK 必备键");
});

check("流体参数默认值覆盖 APK 实测字段", () => {
    const data = tools.fluidData({ _width: 12 });
    equal(data._width, 12);
    for (const key of ["_height", "_resolution", "_waveLength", "_waveAmplitude", "_waveSpeed", "_enableGoUnder", "m_Name"])
        assert(key in data, "充体参数缺 " + key);
    assert(data._width > 0 && data._width <= 10000, "宽度超出 Rc2Document 允许范围");
});

// ---- 真关卡集成 ----
const { entry, document } = loadLevel("Mission_Sand_1");
const baseNodes = document.scene.nodes.length;

check("在真关卡里插入地形后 validateScene 通过", () => {
    const scene = core.clone(document.scene);
    const created = tools.createTerrain(scene, ids("terrain"), { shape: "rect", width: 12, height: 5, position: [10, -20, 0] });
    core.validateScene(scene, catalog);
    equal(scene.nodes.length, baseNodes + 1, "节点数应 +1");
    assert(scene.mesh_overrides[created.meshKey], "网格覆盖没写进场景");
    assert(scene.prefabs[created.prefabKey], "地形预制体没建出来");
    equal(scene.prefabs[created.prefabKey].materials.length, 2, "地形预制体应绑定 地面 + 表面 两个材质");
    equal(scene.nodes[scene.nodes.length - 1].renderers[0].mesh_override, created.meshKey);
    equal(scene.nodes[scene.nodes.length - 1].colliders.length, 1);
});

check("在真关卡里插入水域后 validateScene 通过", () => {
    const scene = core.clone(document.scene);
    const created = tools.createWater(scene, ids("water"), { width: 16, height: 4, position: [511, -61, 0] });
    core.validateScene(scene, catalog);
    assert(scene.prefabs[created.prefabKey].fluid_generated, "水域预制体必须标记 fluid_generated");
    equal(scene.prefabs[created.prefabKey].materials[0], tools.WATER_MATERIAL);
    const node = scene.nodes[scene.nodes.length - 1];
    equal(node.name, "FluidObject");
    near(node.fluid.data._width, 16, 1e-9);
    near(node.fluid.data._height, 4, 1e-9);
    equal(node.colliders[0].data.m_IsTrigger, true, "水域碰撞体默认应是触发器");
});

check("同一份文档里连续插入多个工具节点仍然通过校验", () => {
    const scene = core.clone(document.scene);
    for (let i = 0; i < 3; i++) tools.createTerrain(scene, ids("t" + i), { shape: ["rect", "circle", "triangle"][i], width: 6 + i, height: 3 + i, position: [i * 10, i * 5, 0] });
    for (let i = 0; i < 2; i++) tools.createWater(scene, ids("w" + i), { width: 10 + i, height: 3, position: [i * 20, -30, 0] });
    core.validateScene(scene, catalog);
    equal(scene.nodes.length, baseNodes + 5);
    // 地形预制体按材质对复用，不能每画一笔就多一个
    const terrainPrefabs = Object.keys(scene.prefabs).filter(key => key.startsWith("bpsim_terrain_"));
    equal(terrainPrefabs.length, 1, "同材质对的地形应共用一个预制体");
    equal(Object.keys(scene.prefabs).filter(key => key === "bpsim_fluid").length, 1, "水域应共用一个流体预制体");
});

check("refreshTerrain 原地重算网格与碰撞体", () => {
    const scene = core.clone(document.scene);
    const created = tools.createTerrain(scene, ids("resize"), { shape: "rect", width: 8, height: 4 });
    const node = created.node;
    const renderer = node.renderers[0];
    const before = JSON.stringify(scene.mesh_overrides[created.meshKey].vertices);
    renderer.brush.width = 20;
    const mesh = tools.refreshTerrain(scene, node, renderer);
    core.validateScene(scene, catalog);
    assert(mesh && mesh === scene.mesh_overrides[created.meshKey], "应返回同一份覆盖网格");
    assert(JSON.stringify(mesh.vertices) !== before, "改宽度后顶点应变化");
    near(Math.max(...mesh.vertices.filter((_, index) => index % 3 === 0)) - Math.min(...mesh.vertices.filter((_, index) => index % 3 === 0)), 20, 1e-6, "网格宽度应跟上画笔宽度");
    equal(node.colliders.length, 1);
    equal(node.colliders[0].data.m_Points.m_Paths[0].length, 4, "碰撞体应跟随形状");
});

check("关掉碰撞体后 refreshTerrain 清空 colliders", () => {
    const scene = core.clone(document.scene);
    const created = tools.createTerrain(scene, ids("nocollider"), { shape: "rect", width: 6, height: 6 });
    created.node.renderers[0].brush.collider = false;
    tools.refreshTerrain(scene, created.node, created.node.renderers[0]);
    core.validateScene(scene, catalog);
    equal(created.node.colliders.length, 0);
});

check("负控：缺 mesh_override 的地形必须被 validateScene 拒绝", () => {
    const scene = core.clone(document.scene);
    const created = tools.createTerrain(scene, ids("broken"), { shape: "rect", width: 6, height: 6 });
    delete scene.mesh_overrides[created.meshKey];
    let failed = null;
    try { core.validateScene(scene, catalog); } catch (error) { failed = error.message; }
    assert(failed && failed.includes("网格覆盖"), "应报缺少实例网格覆盖，实际：" + failed);
});

check("负控：非法网格必须被 validateMesh 拒绝", () => {
    const bad = { vertices: [0, 0, 0], uv: [0], colors: [1, 1, 1, 1], submeshes: [[0, 1, 2]] };
    let failed = null;
    try { core.validateMesh(bad); } catch (error) { failed = error.message; }
    assert(failed && failed.includes("维度"), "应报维度不匹配，实际：" + failed);
});

check("负控：不在 catalog 里的材质必须被拒绝", () => {
    const scene = core.clone(document.scene);
    scene.prefabs["bpsim_terrain_bad"] = tools.terrainPrefab("mat_not_in_catalog", tools.TERRAIN_SURFACE[0].key);
    let failed = null;
    try { core.validateScene(scene, catalog); } catch (error) { failed = error.message; }
    assert(failed && failed.includes("原始资源") && failed.includes("materials/mat_not_in_catalog"), "应报缺少原始资源 materials/…，实际：" + failed);
});

check("保存路径：整份文档 JSON 往返后仍然有效", () => {
    const scene = core.clone(document.scene);
    tools.createTerrain(scene, ids("roundtrip_t"), { shape: "circle", width: 9, height: 9, position: [3, 4, 0] });
    tools.createWater(scene, ids("roundtrip_w"), { width: 14, height: 3, position: [1, -9, 0] });
    const copy = { ...document, scene: scene };
    const text = JSON.stringify(copy);
    const parsed = JSON.parse(text);
    core.validateScene(parsed.scene, catalog);
    equal(parsed.scene.nodes.length, baseNodes + 2);
    assert(text.length > 0);
});

const failed = checks.filter(entry => !entry.passed);
const report = {
    pass: failed.length === 0,
    checks: checks.length,
    failed: failed.length,
    level: entry.mission,
    baseNodes,
    terrainMaterials: tools.TERRAIN_GROUND.length + tools.TERRAIN_SURFACE.length,
    failures: failed,
};
const out = path.join(project, "_codex_work", "level-editor-mod");
fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(path.join(out, "rc2-tools-tests.json"), JSON.stringify({ ...report, detail: checks }, null, 2));
console.log(JSON.stringify({ pass: report.pass, checks: report.checks, failed: report.failed, level: report.level, baseNodes: report.baseNodes, failures: report.failures }, null, 2));
process.exit(report.pass ? 0 : 1);

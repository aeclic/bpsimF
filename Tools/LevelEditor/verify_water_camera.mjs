// Raster regressions for finite water bounds and cameras above/below the waterline.
// Run after port_ultimate_water.py, with Mods/LevelEditor/Web served on port 8099.
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const require = createRequire(process.env.CODEX_NODE_MODULES
    ? path.join(process.env.CODEX_NODE_MODULES, 'entry.cjs')
    : 'C:/Users/aecli/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/');
const { chromium } = require('playwright');
const output = path.resolve(process.env.WATER_CAMERA_OUTPUT_DIR || '_codex_work/water-camera-regression');
await mkdir(output, { recursive: true });
const args = ['--mute-audio'];
if (process.env.WATER_SWIFTSHADER !== '0') args.push('--use-angle=swiftshader', '--enable-unsafe-swiftshader');
const browser = await chromium.launch({
    executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true, args,
});
try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 762 }, deviceScaleFactor: 1 });
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    await page.goto('http://127.0.0.1:8099/bpsim/ultimate-water/demo-profile.json');
    await page.addScriptTag({ url: '/bpsim/ultimate-water-renderer.js' });
    await page.addScriptTag({ url: '/bpsim/water-canvas.js' });
    const results = await page.evaluate(async () => {
        const size = { width: 1280, height: 762 };
        const source = document.createElement('canvas');
        source.width = size.width; source.height = size.height;
        const context = source.getContext('2d');
        const gradient = context.createLinearGradient(0, 0, 0, size.height);
        gradient.addColorStop(0, '#84bdd9'); gradient.addColorStop(1, '#192d41');
        context.fillStyle = gradient; context.fillRect(0, 0, size.width, size.height);
        context.strokeStyle = '#738a95'; context.lineWidth = 1;
        for (let x = 0; x < size.width; x += 40) { context.beginPath(); context.moveTo(x, 0); context.lineTo(x, size.height); context.stroke(); }
        for (let y = 0; y < size.height; y += 40) { context.beginPath(); context.moveTo(0, y); context.lineTo(size.width, y); context.stroke(); }
        const renderer = new UltimateWaterRenderer(); await renderer.loading;
        if (!renderer.ready) throw Error(renderer.error || 'WebGL2 is unavailable');
        const geometry = new UltimateWaterRenderer(); await geometry.loading;
        if (!geometry.ready) throw Error(geometry.error || 'WebGL2 geometry renderer is unavailable');
        // Use the real surface vertex program, replacing only its fragment colour.
        // This distinguishes a missing triangle from an intentional material fade.
        const vertex = await (await fetch('/bpsim/ultimate-water/WaterSurface.vertex-port.glsl')).text();
        geometry.gl.deleteProgram(geometry.programs.WaterSurface.program);
        geometry.programs.WaterSurface = geometry.compile(`#version 300 es
precision highp float;
out vec4 SV_TARGET0;
void main(){SV_TARGET0=vec4(1);}`, vertex);
        const templateBody = bpsimWaterCanvas.migrate({ id: 'camera-fixture', name: 'Camera fixture', x: -2,
            surface_y: 0, width: 4, depth: 40, wave_amplitude: 0, wave_speed: 0, color: '#00a9fd', opacity: 1 });
        const cases = [
            { name: 'above-near', x: 0, y: .475, zoom: 200 },
            { name: 'below-near', x: 0, y: -.475, zoom: 200 },
            { name: 'above-panned', x: .75, y: 1.5, zoom: 120 },
            { name: 'below-panned', x: -.75, y: -1.5, zoom: 120 },
            { name: 'above-far', x: 0, y: 4, zoom: 40 },
            { name: 'below-far', x: 0, y: -4, zoom: 60 },
            { name: 'below-wide', x: .5, y: -10, zoom: 24 },
            { name: 'underwater-deep', x: 0, y: -20, zoom: 100 },
            { name: 'waterline-edge-on', x: 0, y: 0, zoom: 200 },
            { name: 'shallow-depth-limit', x: 0, y: -6, zoom: 50, depth: 2 },
        ];
        const snapshot = (target) => {
            const pixels = new Uint8Array(size.width * size.height * 4);
            target.gl.readPixels(0, 0, size.width, size.height, target.gl.RGBA, target.gl.UNSIGNED_BYTE, pixels);
            return { pixels, png: target.canvas.toDataURL() };
        };
        const alphaAt = (pixels, x, y) => pixels[((size.height - y - 1) * size.width + x) * 4 + 3];
        const records = [];
        for (const camera of cases) {
            const body = { ...templateBody, depth: camera.depth ?? templateBody.depth };
            const checks = [];
            const check = (name, passed, details) => checks.push({ name, passed, ...details });
            geometry.draw([body], camera, size, source, [], 2, [], 'WaterSurface');
            const coverage = snapshot(geometry);
            renderer.draw([body], camera, size, source, [], 2, [], 'WaterSurface');
            const surface = snapshot(renderer);
            renderer.draw([body], camera, size, source, [], 2, [], 'Underwater');
            const underwater = snapshot(renderer);
            renderer.draw([body], camera, size, source, [], 2, []);
            const canvas = document.createElement('canvas'); canvas.width = size.width; canvas.height = size.height;
            const display = canvas.getContext('2d'); display.drawImage(source, 0, 0); display.drawImage(renderer.canvas, 0, 0);
            const waterline = size.height / 2 + (camera.y - body.surface_y) * camera.zoom;
            const projectedDepth = Math.min(Math.abs(camera.y - body.surface_y) * 294.121521 / (294.121521 + 19.4), body.depth);
            const above = camera.y >= body.surface_y;
            const upperSurface = waterline - (above ? projectedDepth : 0) * camera.zoom;
            const lowerSurface = waterline + (above ? 0 : projectedDepth) * camera.zoom;
            const expectedLeft = size.width / 2 + (body.x - camera.x) * camera.zoom;
            const expectedRight = expectedLeft + body.width * camera.zoom;
            display.strokeStyle = '#ffce53'; display.setLineDash([7, 7]);
            display.beginPath(); display.moveTo(expectedLeft, 0); display.lineTo(expectedLeft, size.height);
            display.moveTo(expectedRight, 0); display.lineTo(expectedRight, size.height);
            display.moveTo(expectedLeft, waterline); display.lineTo(expectedRight, waterline); display.stroke();
            const rows = [];
            let visibleSurfacePixels = 0;
            for (let y = 0; y < size.height; y++) {
                let left = -1, right = -1;
                for (let x = 0; x < size.width; x++) {
                    if (alphaAt(coverage.pixels, x, y) > 127) { if (left < 0) left = x; right = x; }
                    if (alphaAt(surface.pixels, x, y) > 8) visibleSurfacePixels++;
                }
                if (left >= 0) rows.push({ y, left, right });
            }
            let bankWorldY = null;
            if (projectedDepth > 0) {
                check('surface remains visible', visibleSurfacePixels > 100, { visibleSurfacePixels });
                check('surface has depth on both camera sides', rows.length > 10, { coveredRows: rows.length });
                // Ignore only the outermost row to allow edge rasterisation.
                const interior = rows.slice(1, -1);
                const maxBoundaryError = interior.length ? Math.max(...interior.map(row =>
                    Math.max(Math.abs(row.left - expectedLeft), Math.abs(row.right + 1 - expectedRight)))) : Infinity;
                check('every depth row reaches both pool sides', maxBoundaryError <= 1, { maxBoundaryError });
                const upperPixel = rows.length ? rows[0].y : -1;
                const lowerPixel = rows.length ? rows.at(-1).y + 1 : -1;
                check('surface extends to the correct side of its stationary near bank', Math.abs(upperPixel - Math.max(0, upperSurface)) <= 1,
                    { upperPixel, upperSurface, clipped: upperSurface < 0, errorPixels: upperPixel - Math.max(0, upperSurface) });
                check('lower edge stays within the finite water depth', Math.abs(lowerPixel - Math.min(size.height, lowerSurface)) <= 1,
                    { lowerPixel, lowerSurface, errorPixels: lowerPixel - Math.min(size.height, lowerSurface) });
                if (waterline >= 0 && waterline < size.height)
                    bankWorldY = camera.y + (size.height / 2 - (above ? lowerPixel : upperPixel)) / camera.zoom;
                if (bankWorldY !== null) check('near bank stays at the authored water height',
                    Math.abs(bankWorldY - body.surface_y) * camera.zoom <= 1, { bankWorldY, authoredY: body.surface_y });
                let holes = 0;
                for (const row of interior) for (let x = row.left + 1; x < row.right; x++)
                    if (alphaAt(coverage.pixels, x, row.y) < 128) holes++;
                check('surface triangles leave no interior holes', holes === 0, { holes });
            } else {
                check('zero-height view is edge-on without an offset surface', rows.length === 0 && visibleSurfacePixels === 0,
                    { coveredRows: rows.length, visibleSurfacePixels });
            }
            // Sample actual material alpha inside the finite water volume, away
            // from its meniscus, bottom and deliberately faded outer pixels.
            let sampled = 0, holes = 0, minAlpha = 255;
            const minX = Math.max(0, Math.ceil(expectedLeft + 6));
            const maxX = Math.min(size.width - 1, Math.floor(expectedRight - 6));
            const topY = Math.max(0, Math.ceil(lowerSurface + 8));
            const bottomY = Math.min(size.height - 1, Math.floor(waterline + body.depth * camera.zoom - 8));
            for (let y = topY; y <= bottomY; y += 2) for (let x = minX; x <= maxX; x += 2) {
                const alpha = alphaAt(underwater.pixels, x, y); sampled++; minAlpha = Math.min(minAlpha, alpha);
                if (alpha < 8) holes++;
            }
            check('underwater material has no missing patches', (topY > bottomY || sampled > 100) && holes === 0,
                { sampled, holes, minAlpha, belowSurfaceVisible: topY <= bottomY });
            if (rows.length > 10) {
                let repainted = 0, seamHoles = 0;
                // Unity draws refraction after parts: it must leave the surface
                // band clear and meet that band's lower edge without a gap.
                for (const row of rows.slice(2, -2)) for (let x = minX; x <= maxX; x += 2)
                    if (alphaAt(underwater.pixels, x, row.y) > 8) repainted++;
                const seamY = rows.at(-1).y + 2;
                const seamVisible = seamY < size.height && projectedDepth < body.depth;
                if (seamVisible)
                    for (let x = minX; x <= maxX; x += 2)
                        if (alphaAt(underwater.pixels, x, seamY) < 8) seamHoles++;
                check('late refraction leaves the surface and foreground parts clear', repainted === 0, { repainted });
                if (seamVisible) check('underwater joins the lower surface edge', seamHoles === 0, { seamHoles });
            }
            check('WebGL reports no errors', renderer.gl.getError() === 0 && geometry.gl.getError() === 0, {});
            records.push({ name: camera.name, camera, bankWorldY, checks, passed: checks.every(item => item.passed),
                images: { composite: canvas.toDataURL(), surface: surface.png, underwater: underwater.png, coverage: coverage.png } });
        }
        const above = records.find(record => record.name === 'above-near');
        const moved = records.find(record => record.name === 'above-panned');
        const drift = Math.abs(above.bankWorldY - moved.bankWorldY);
        moved.checks.push({ name: 'camera translation leaves the near bank fixed against world terrain',
            passed: above.bankWorldY !== null && moved.bankWorldY !== null && drift <= 1 / Math.min(above.camera.zoom, moved.camera.zoom),
            initialWorldY: above.bankWorldY, movedWorldY: moved.bankWorldY, drift });
        moved.passed = moved.checks.every(item => item.passed);
        const gpu = renderer.gl.getExtension('WEBGL_debug_renderer_info');
        return { records, gpu: gpu ? renderer.gl.getParameter(gpu.UNMASKED_RENDERER_WEBGL) : 'unknown' };
    });
    for (const record of results.records) {
        for (const [kind, png] of Object.entries(record.images))
            await writeFile(path.join(output, `${record.name}-${kind}.png`), Buffer.from(png.split(',')[1], 'base64'));
        delete record.images;
    }
    results.errors = errors;
    results.passed = errors.length === 0 && results.records.every(record => record.passed);
    await writeFile(path.join(output, 'report.json'), JSON.stringify(results, null, 2));
    await writeFile(path.join(output, 'index.html'), `<!doctype html><meta charset="utf-8"><title>Water camera regression</title>
<style>body{background:#14202c;color:#e7f0f7;font:16px system-ui;margin:24px}main{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}img{width:100%}figure{margin:0}figcaption{padding:8px 0}</style>
<h1>Water camera regression: ${results.passed ? 'PASS' : 'FAIL'}</h1><p>Dashed yellow lines show the finite water bounds and the stationary near bank.</p><main>
${results.records.map(record => `<figure><img src="${record.name}-composite.png"><figcaption>${record.name}: ${record.passed ? 'PASS' : 'FAIL'} (camera y=${record.camera.y}, zoom=${record.camera.zoom})</figcaption></figure>`).join('\n')}</main>`);
    console.log(JSON.stringify({ passed: results.passed, gpu: results.gpu, errors,
        cases: results.records.map(record => ({ name: record.name, passed: record.passed,
            failures: record.checks.filter(check => !check.passed) })), output }, null, 2));
    if (!results.passed) process.exitCode = 1;
} finally { await browser.close(); }

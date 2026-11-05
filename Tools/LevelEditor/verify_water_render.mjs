// Actual WebGL2 compilation and deterministic frames, shared with the Unity QA.
import { createRequire } from 'node:module';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire('C:/Users/aecli/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/entry.cjs');
const { chromium } = require('playwright');
const output = path.resolve(process.env.WATER_OUTPUT_DIR || '_codex_work/water-comparison');
await mkdir(output, { recursive: true });
let uniformOverrides = {};
try { uniformOverrides = JSON.parse(await readFile(path.join(output, 'diagnostic-overrides.json'), 'utf8')); }
catch (error) { if (error.code !== 'ENOENT') throw error; }
const cameraY = Number(process.env.WATER_CAMERA_Y ?? 0.475);
const useSwiftShader = process.env.WATER_SWIFTSHADER !== '0';
const browserArgs = ['--mute-audio'];
if (useSwiftShader) browserArgs.push('--use-angle=swiftshader', '--enable-unsafe-swiftshader');
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
    args: browserArgs });
try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 762 } });
    const errors = []; page.on('pageerror', error => errors.push(String(error)));
    await page.goto('http://127.0.0.1:8099/bpsim/ultimate-water/demo-profile.json');
    await page.addScriptTag({ url: '/bpsim/ultimate-water-renderer.js' });
    await page.addScriptTag({ url: '/bpsim/water-canvas.js' });
    const result = await page.evaluate(async (cameraY, uniformOverrides) => {
        document.body.innerHTML = ''; document.body.style.cssText = 'margin:0;background:black';
        const scene = document.createElement('canvas'); scene.width = 1280; scene.height = 762;
        const ctx = scene.getContext('2d');
        const gradient = ctx.createLinearGradient(0, 0, 0, 762); gradient.addColorStop(0, '#50c9f5'); gradient.addColorStop(.65, '#316a78'); gradient.addColorStop(1, '#162f47');
        ctx.fillStyle = gradient; ctx.fillRect(0, 0, 1280, 762);
        ctx.fillStyle = '#14664e'; ctx.beginPath(); ctx.moveTo(0, 50); ctx.lineTo(300, 320); ctx.lineTo(420, 510); ctx.lineTo(0, 762); ctx.fill();
        ctx.fillStyle = '#a1c959'; ctx.fillRect(120, 420, 260, 65);
        ctx.fillStyle = '#f4f7dd'; ctx.fillRect(850, 100, 120, 220);
        ctx.fillStyle = '#f4f7dd'; ctx.fillRect(540, 454, 150, 112);
        ctx.fillStyle = '#9c7041'; ctx.fillRect(540, 510, 150, 56);
        ctx.strokeStyle = '#342e24'; ctx.lineWidth = 4; ctx.strokeRect(540, 454, 150, 112);
        ctx.strokeStyle = '#526b70'; ctx.lineWidth = 3;
        for (let x = 0; x < 1280; x += 80) { ctx.beginPath(); ctx.moveTo(x, 510); ctx.lineTo(x, 762); ctx.stroke(); }
        for (let y = 550; y < 762; y += 60) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(1280, y); ctx.stroke(); }
        const source = document.createElement('canvas'); source.width = 1280; source.height = 762;
        const frame = source.getContext('2d'), frameGradient = frame.createLinearGradient(0, 0, 0, 762);
        frameGradient.addColorStop(0, '#50c9f5'); frameGradient.addColorStop(.65, '#316a78'); frameGradient.addColorStop(1, '#162f47');
        frame.fillStyle = frameGradient; frame.fillRect(0, 0, 1280, 762);
        frame.drawImage(scene, 0, (cameraY - 0.475) * 272);
        const renderer = new UltimateWaterRenderer(); await renderer.loading;
        if (!renderer.ready) throw new Error(renderer.error || 'WebGL2 unavailable');
        const body = bpsimWaterCanvas.migrate({ id: 'fixture', name: 'fixture', x: -18.984375, surface_y: 0, width: 37.96875, depth: 7.1382084,
            wave_amplitude: 0, wave_speed: 0, color: '#00a9fd', opacity: 1 });
        const camera = { x: 0, y: cameraY, zoom: 272 }, size = { width: 1280, height: 762 };
        renderer.draw([body], camera, size, source, [], 2, [], 'Underwater', uniformOverrides);
        const underwater = renderer.canvas.toDataURL();
        renderer.draw([body], camera, size, source, [], 2, [], 'WaterSurface', uniformOverrides);
        const surface = renderer.canvas.toDataURL();
        renderer.draw([body], camera, size, source, [], 2, [], null, uniformOverrides);
        const display = document.createElement('canvas'); display.width = 1280; display.height = 762; document.body.appendChild(display);
        display.getContext('2d').drawImage(source, 0, 0); display.getContext('2d').drawImage(renderer.canvas, 0, 0);
        const debugInfo = renderer.gl.getExtension('WEBGL_debug_renderer_info');
        return { error: renderer.gl.getError(), ready: renderer.ready, gpu: debugInfo ? renderer.gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) : 'unknown',
            rays: renderer.lastRayData, source: source.toDataURL(), water: renderer.canvas.toDataURL(), underwater, surface,
            halfSource: renderer.underwaterCapture.toDataURL(), reflection: renderer.mirror.toDataURL(),
            fixture: { body, camera, size, time: 2, waterlineY: size.height / 2 + (body.surface_y - camera.y) * -camera.zoom } };
    }, cameraY, uniformOverrides);
    for (const key of ['source', 'water', 'underwater', 'surface', 'halfSource', 'reflection']) {
        const name = key === 'halfSource' ? 'web-half-source.png' : key === 'reflection' ? 'web-reflection.png' : 'web-' + key + '.png';
        await writeFile(path.join(output, name), Buffer.from(result[key].split(',')[1], 'base64')); delete result[key];
    }
    await page.screenshot({ path: path.join(output, 'web-composite.png') });
    await writeFile(path.join(output, 'ray-frame.json'),JSON.stringify(result.rays)); delete result.rays;
    result.errors = errors; await writeFile(path.join(output, 'web-report.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result));
    if (result.error || errors.length) process.exitCode = 1;
} finally { await browser.close(); }

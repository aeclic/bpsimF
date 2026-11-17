// Replay captured demo ray inputs against the port. Run with the local Web root
// served on 8099: node Tools/LevelEditor/probe_water_rays.mjs. Reports RGBA8 error;
// only covers the captured ray draw, not Unity capture or whole-scene compositing.
import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const require = createRequire('C:/Users/aecli/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/entry.cjs');
const { chromium } = require('playwright');
const records = JSON.parse(await readFile('_codex_work/water-reference/draw-inputs.json', 'utf8')).records;
const out = '_codex_work/water-rays-probe'; await mkdir(out, { recursive: true });
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--mute-audio'] });
try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 762 } });
    await page.goto('http://127.0.0.1:8099/bpsim/ultimate-water/demo-profile.json');
    await page.addScriptTag({ url: '/bpsim/ultimate-water-renderer.js' });
    const result = await page.evaluate(async records => {
        const r = new UltimateWaterRenderer(); await r.loading; if (!r.ready) throw Error(r.error);
        const gl = r.gl, record = records.find(x => x.kind === 'LightRays'), u = record.uniforms;
        const vp = u['hlslcc_mtx4x4unity_MatrixVP[0]'], model = u['hlslcc_mtx4x4unity_ObjectToWorld[0]'];
        const distance = vp[15] + model[14];
        const camera = { x: (-vp[12] / vp[0] - model[12]) / 8, y: -vp[13] / vp[5] / 8, zoom: 762 * .5 * vp[5] / distance * 8 };
        const body = { x: -151.875 / 8, surface_y: 0, width: 303.75 / 8, depth: 57.105667 / 8, wave_amplitude: 0, wave_speed: 0, wave_length: 1 };
        const rect = [camera.x - 640 / camera.zoom, camera.y - 381 / camera.zoom, 1280 / camera.zoom, 762 / camera.zoom];
        const source = document.createElement('canvas'); source.width = 1280; source.height = 762;
        r.canvas.width = 1280; r.canvas.height = 762;
        gl.enable(gl.BLEND); gl.disable(gl.DEPTH_TEST); gl.disable(gl.CULL_FACE); gl.bindBuffer(gl.ARRAY_BUFFER, r.buffer);
        r.drawRays([body], camera, { width: 1280, height: 762 }, rect, u._Time[1], { LightRays: u });
        gl.bindFramebuffer(gl.FRAMEBUFFER, r.rayFramebuffer);
        const port = new Uint8Array(640 * 381 * 4); gl.readPixels(0, 0, 640, 381, gl.RGBA, gl.UNSIGNED_BYTE, port);
        // Anchoring the near wave edge retains the original demo's projection.
        // Compare against the captured source without any compensating translation.
        const originalVertex = record.sources.find(x => x.includes('gl_Position'));
        const program = r.compile(record.sources.find(x => x.includes('SV_Target0')), originalVertex).program;
        gl.useProgram(program);
        for (let i = 0; i < gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS); i++) {
            const info = gl.getActiveUniform(program, i), loc = gl.getUniformLocation(program, info.name), value = u[info.name];
            if (info.type === gl.SAMPLER_2D) { gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, r.black); gl.uniform1i(loc, 0); }
            else if (info.type === gl.FLOAT) gl.uniform1f(loc, value);
            else gl['uniform' + ({ [gl.FLOAT_VEC2]: 2, [gl.FLOAT_VEC3]: 3, [gl.FLOAT_VEC4]: 4 })[info.type] + 'fv'](loc, value);
        }
        for (const a of record.attributes) {
            const loc = gl.getAttribLocation(program, a.name); if (loc < 0) continue;
            const buffer = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buffer); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(a.values), gl.STATIC_DRAW);
            gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, a.size, gl.FLOAT, false, 0, 0);
        }
        const indices = gl.createBuffer(); gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indices); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(record.indices), gl.STATIC_DRAW);
        gl.viewport(0, 0, 640, 381); gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
        gl.drawElements(gl.TRIANGLES, record.indices.length, gl.UNSIGNED_SHORT, 0);
        const reference = new Uint8Array(port.length); gl.readPixels(0, 0, 640, 381, gl.RGBA, gl.UNSIGNED_BYTE, reference);
        let sum = 0, max = 0, changed = 0, nonzero = 0;
        for (let i = 0; i < port.length; i++) { const d = Math.abs(port[i] - reference[i]); sum += d; max = Math.max(max, d); if (d > 2) changed++; if (reference[i]) nonzero++; }
        const image = bytes => { const canvas = document.createElement('canvas'); canvas.width = 640; canvas.height = 381; const flipped = new Uint8ClampedArray(bytes.length); for (let y = 0; y < 381; y++) flipped.set(bytes.subarray(y * 2560, (y + 1) * 2560), (380 - y) * 2560); canvas.getContext('2d').putImageData(new ImageData(flipped, 640, 381), 0, 0); return canvas.toDataURL(); };
        const surfaceFragment = await (await fetch('/bpsim/ultimate-water/WaterSurface.fragment.glsl')).text();
        const surfaceVertex = await (await fetch('/bpsim/ultimate-water/WaterSurface.vertex-port.glsl')).text();
        // Isolate the underside branch: the regular above-water fixture uses the
        // front face, where the local underwater surface map has no influence.
        const underside = surfaceFragment.replace('true ? 0xffffffffu', 'false ? 0xffffffffu').replace('gl_FrontFacing ? 0xffffffffu', 'false ? 0xffffffffu')
            .replace('(uw_CaptureRect.y+uw_CaptureRect.w*0.5>=uw_Bounds.y) ? 0xffffffffu', 'false ? 0xffffffffu');
        const plain = underside.replace('uwUnderwater(texture(_LocalUnderwaterSurfaceMap, u_xlat26.xy, _GlobalMipBias.x), u_xlat26.xy)', 'uwDecodeScene(texture(_LocalUnderwaterSurfaceMap, u_xlat26.xy, _GlobalMipBias.x))');
        const withRays = plain.replace('uwDecodeScene(texture(_LocalUnderwaterSurfaceMap, u_xlat26.xy, _GlobalMipBias.x))', 'uwUnderwater(texture(_LocalUnderwaterSurfaceMap, u_xlat26.xy, _GlobalMipBias.x), u_xlat26.xy)');
        if (plain === withRays) throw Error('Local underwater texture sample was not found');
        source.getContext('2d').fillStyle = '#203040'; source.getContext('2d').fillRect(0, 0, 1280, 762);
        const surfacePixels = (fragment, opacity) => {
            r.programs.WaterSurface = r.compile(fragment, surfaceVertex);
            r.draw([body], camera, { width: 1280, height: 762 }, source, [], u._Time[1], [], 'WaterSurface', { LightRays: { _Opacity: opacity } });
            const pixels = new Uint8Array(1280 * 762 * 4); gl.readPixels(0, 0, 1280, 762, gl.RGBA, gl.UNSIGNED_BYTE, pixels); return pixels;
        };
        const delta = (a, b) => { let channels = 0, total = 0; for (let i = 0; i < a.length; i++) { const d = Math.abs(a[i] - b[i]); total += d; if (d) channels++; } return { channels, total }; };
        const surfaceContribution = delta(surfacePixels(plain, 1), surfacePixels(withRays, 1));
        const surfaceDisabledControl = delta(surfacePixels(plain, 0), surfacePixels(withRays, 0));
        return { max, mean: sum / port.length, channelsOver2: changed, nonzero, error: gl.getError(), camera, nearEdgeAnchored: true, surfaceContribution, surfaceDisabledControl, port: image(port), reference: image(reference) };
    }, records);
    for (const name of ['port', 'reference']) { await writeFile(out + '/' + name + '.png', Buffer.from(result[name].split(',')[1], 'base64')); delete result[name]; }
    await writeFile(out + '/report.json', JSON.stringify(result, null, 2)); console.log(result);
    if (result.error || result.mean > .5 || result.channelsOver2 > 1000 || !result.nonzero || !result.surfaceContribution.channels || result.surfaceDisabledControl.channels) process.exitCode = 1;
} finally { await browser.close(); }

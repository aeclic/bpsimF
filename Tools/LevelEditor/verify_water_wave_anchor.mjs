// Frozen-time regression for the actual wave-bearing near edge. The production
// vertex shader is captured with GPU transform feedback; no projection formula
// is reproduced to decide whether camera movement changes its world position.
import { createRequire } from 'node:module';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

const require = createRequire(process.env.CODEX_NODE_MODULES
    ? path.join(process.env.CODEX_NODE_MODULES, 'entry.cjs')
    : 'C:/Users/aecli/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/entry.cjs');
const { chromium } = require('playwright');
const output = path.resolve(process.env.WATER_WAVE_OUTPUT_DIR || '_codex_work/water-wave-anchor');
const vertexPath = path.resolve(process.env.WATER_WAVE_VERTEX || 'Mods/LevelEditor/Web/bpsim/ultimate-water/WaterSurface.vertex-port.glsl');
const vertexSource = await readFile(vertexPath, 'utf8');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--mute-audio'] });
try {
    const page = await browser.newPage();
    await page.goto('http://127.0.0.1:8099/bpsim/ultimate-water/demo-profile.json');
    await page.addScriptTag({ url: '/bpsim/ultimate-water-renderer.js' });
    const result = await page.evaluate(async vertex => {
        const renderer = new UltimateWaterRenderer(); await renderer.loading;
        if (!renderer.ready) throw Error(renderer.error || 'WebGL2 unavailable');
        const gl = renderer.gl, program = gl.createProgram();
        for (const [type, source] of [[gl.VERTEX_SHADER, vertex], [gl.FRAGMENT_SHADER,
            '#version 300 es\nprecision highp float;out vec4 color;void main(){color=vec4(1);}']]) {
            const shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
            if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw Error(gl.getShaderInfoLog(shader));
            gl.attachShader(program, shader); gl.deleteShader(shader);
        }
        gl.transformFeedbackVaryings(program, ['uw_world', 'uw_original', 'gl_Position'], gl.INTERLEAVED_ATTRIBS);
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw Error(gl.getProgramInfoLog(program));
        renderer.programs.WaterSurface = { program, locations: {} };
        const body = { id: 'wave-anchor', x: -2, surface_y: 7, width: 4, depth: 40,
            wave_amplitude: .15, wave_speed: 1, wave_length: 2.5 };
        const size = { width: 1280, height: 762 }, time = 2, stride = 9;
        renderer.canvas.width = size.width; renderer.canvas.height = size.height;
        const buffer = gl.createBuffer(), feedback = gl.createTransformFeedback();
        gl.bindTransformFeedback(gl.TRANSFORM_FEEDBACK, feedback);
        gl.bindBuffer(gl.TRANSFORM_FEEDBACK_BUFFER, buffer);
        gl.bufferData(gl.TRANSFORM_FEEDBACK_BUFFER, renderer.surfaceInfo.vertices * stride * 4, gl.STREAM_READ);
        gl.bindBufferBase(gl.TRANSFORM_FEEDBACK_BUFFER, 0, buffer);
        const cases = [
            { name: 'baseline', x: 0, y: 7.475, zoom: 200, time },
            { name: 'repeat-AA', x: 0, y: 7.475, zoom: 200, time },
            { name: 'pan-horizontal', x: 2.5, y: 7.475, zoom: 200, time },
            { name: 'zoom', x: 0, y: 7.475, zoom: 80, time },
            { name: 'camera-higher', x: 0, y: 8.5, zoom: 200, time },
            { name: 'camera-below', x: 0, y: 6.525, zoom: 200, time },
            { name: 'camera-deeper', x: -.75, y: 5.5, zoom: 120, time },
            { name: 'time-advanced', x: 0, y: 7.475, zoom: 200, time: 3.5 },
        ];
        const frames = [];
        for (const camera of cases) {
            const rect = [camera.x - size.width / camera.zoom / 2, camera.y - size.height / camera.zoom / 2,
                size.width / camera.zoom, size.height / camera.zoom];
            renderer.updateRipples(body, camera.time, []);
            renderer.bind('WaterSurface', body, rect, camera.time, null);
            gl.bindBuffer(gl.ARRAY_BUFFER, renderer.surfaceBuffer);
            gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 16, 0);
            gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 2, gl.FLOAT, false, 16, 8);
            gl.enable(gl.RASTERIZER_DISCARD); gl.beginTransformFeedback(gl.POINTS);
            gl.drawArrays(gl.POINTS, 0, renderer.surfaceInfo.vertices);
            gl.endTransformFeedback(); gl.disable(gl.RASTERIZER_DISCARD);
            const data = new Float32Array(renderer.surfaceInfo.vertices * stride);
            gl.getBufferSubData(gl.TRANSFORM_FEEDBACK_BUFFER, 0, data);
            const near = [];
            for (let i = 0; i < data.length; i += stride) {
                if (Math.abs(data[i + 4]) > .001) continue;
                near.push({ index: i / stride, x: data[i], y: data[i + 1], wave: data[i + 3],
                    screenWorldX: rect[0] + (data[i + 5] / data[i + 8] + 1) * rect[2] / 2,
                    screenWorldY: rect[1] + (data[i + 6] / data[i + 8] + 1) * rect[3] / 2 });
            }
            frames.push({ camera, near });
        }
        const checks = [], baseline = frames[0];
        const check = (name, passed, details) => checks.push({ name, passed, ...details });
        check('wave-bearing near edge was captured', baseline.near.length > 100, { vertices: baseline.near.length });
        const waveRange = Math.max(...baseline.near.map(v => v.wave)) - Math.min(...baseline.near.map(v => v.wave));
        check('the fixture contains a non-flat wave', waveRange > .1, { waveRange });
        for (const frame of frames.slice(1)) {
            let maxWorldDrift = 0, maxScreenWorldDrift = 0, maxWaveChange = 0;
            for (let i = 0; i < baseline.near.length; i++) {
                const a = baseline.near[i], b = frame.near[i];
                if (!b || a.index !== b.index) throw Error('Near-edge vertex correspondence changed');
                maxWorldDrift = Math.max(maxWorldDrift, Math.abs(b.x - a.x), Math.abs(b.y - a.y));
                maxScreenWorldDrift = Math.max(maxScreenWorldDrift, Math.abs(b.screenWorldX - a.screenWorldX), Math.abs(b.screenWorldY - a.screenWorldY));
                maxWaveChange = Math.max(maxWaveChange, Math.abs(b.wave - a.wave));
            }
            const details = { camera: frame.camera, maxWorldDrift, maxScreenWorldDrift, maxWaveChange };
            if (frame.camera.time === time) {
                check(frame.camera.name + ': frozen wave stays anchored to world terrain',
                    maxWorldDrift < 1e-5 && maxScreenWorldDrift < 1e-5 && maxWaveChange === 0, details);
            } else {
                check('time advance changes the wave while the camera is fixed', maxWaveChange > .05 && maxWorldDrift > .005, details);
            }
        }
        // The visible cyan line is shaded by Underwater, independently of the
        // Surface vertex edge. Exercise its real isolated pass and the late pass.
        const source = document.createElement('canvas'); source.width=size.width; source.height=size.height;
        source.getContext('2d').fillRect(0,0,size.width,size.height);
        const lineFrames=[];
        const raster = (camera, pass, overrides=null) => {
            renderer.draw([body],camera,size,source,[],time,[],pass,overrides);
            const pixels=new Uint8Array(size.width*size.height*4);
            gl.readPixels(0,0,size.width,size.height,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
            return pixels;
        };
        for(const camera of [{x:0,y:6.525,zoom:200},{x:0,y:5.5,zoom:200},{x:0,y:6.525,zoom:200}]) {
            const pixels=raster(camera,'Meniscus'), columns=[]; let count=0;
            for(let x=0;x<size.width;x++) {
                let sum=0,weight=0;
                for(let y=0;y<size.height;y++) {
                    const alpha=pixels[(y*size.width+x)*4+3];
                    if(alpha>8){count++;sum+=(camera.y+(y+.5-size.height/2)/camera.zoom)*alpha;weight+=alpha;}
                }
                columns.push(weight?sum/weight:null);
            }
            check('cyan meniscus is visible below the bank',count>100,{camera,pixels:count});
            lineFrames.push({camera,columns});
        }
        for(const frame of lineFrames.slice(1)) {
            let drift=0,samples=0;
            for(let x=0;x<size.width;x++) if(frame.columns[x]!==null&&lineFrames[0].columns[x]!==null){
                drift=Math.max(drift,Math.abs(frame.columns[x]-lineFrames[0].columns[x]));samples++;
            }
            check('rendered cyan line stays fixed as the underwater camera moves',samples>100&&drift<=.0051,{samples,maxWorldDrift:drift});
        }
        const below={x:0,y:6.525,zoom:200};
        const normal=raster(below,'Underwater'), noCyan=raster(below,'Underwater',{Underwater:{_MeniscusColor:[0,0,0,0]}});
        let displacedCyan=0;
        for(let i=0;i<normal.length;i++)if(normal[i]!==noCyan[i])displacedCyan++;
        check('late underwater pass has no displaced cyan duplicate',displacedCyan===0,{changedChannels:displacedCyan});
        const error = gl.getError(); check('WebGL reports no errors', error === 0, { error });
        const gpu = gl.getExtension('WEBGL_debug_renderer_info');
        return { passed: checks.every(item => item.passed), body, checks,
            gpu: gpu ? gl.getParameter(gpu.UNMASKED_RENDERER_WEBGL) : 'unknown',
            frames: frames.map(frame => ({ camera: frame.camera, nearCount: frame.near.length,
                yRange: [Math.min(...frame.near.map(v => v.y)), Math.max(...frame.near.map(v => v.y))] })) };
    }, vertexSource);
    result.vertexPath = vertexPath;
    result.vertexSha256 = createHash('sha256').update(vertexSource).digest('hex');
    await writeFile(path.join(output, 'report.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
    if (!result.passed) process.exitCode = 1;
} finally { await browser.close(); }

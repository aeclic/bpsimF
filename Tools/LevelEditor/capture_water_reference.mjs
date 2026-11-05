// Capture the reference player's real material uniforms and mesh inputs.
import { createRequire } from 'node:module';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(process.env.CODEX_NODE_MODULES ? path.join(process.env.CODEX_NODE_MODULES, 'entry.cjs') : 'C:/Users/aecli/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/entry.cjs');
const { chromium } = require('playwright');
const interacting=process.argv.includes('--interact');
const out = path.resolve(interacting?'_codex_work/water-interaction':'_codex_work/water-reference');
await mkdir(out, { recursive: true });
const profile = path.join(out, 'browser-profile');
const context = await chromium.launchPersistentContext(profile, {
    executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true,
    viewport: { width: 1280, height: 800 }, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--mute-audio'],
});
try {
    const page = await context.newPage();
    await page.addInitScript(interact => {
        window.waterNeedRipple=interact;
        window.waterReference = { armed: false, records: [], errors: [] };
        window.waterNativeTextures={};
        const proto = WebGL2RenderingContext.prototype;
        const textureSizes = new WeakMap();
        for (const method of ['texImage2D', 'compressedTexImage2D', 'texStorage2D']) {
            const original = proto[method];
            proto[method] = function (...args) {
                const texture = this.getParameter(this.TEXTURE_BINDING_2D);
                if (texture) {
                    const dimensions = method === 'texStorage2D' ? [args[3], args[4]] : args.length >= 9 || method === 'compressedTexImage2D' ? [args[3], args[4]] : [args[5]?.width, args[5]?.height];
                    if (dimensions[0] && (method === 'texStorage2D' || args[1] === 0)) textureSizes.set(texture, dimensions);
                }
                return original.apply(this, args);
            };
        }
        const lengths = { 5126: 1, 35664: 2, 35665: 3, 35666: 4, 35676: 16, 5124: 1, 35667: 2, 35668: 3, 35669: 4 };
        function readColor(gl,width,height) {
            let type=gl.getParameter(gl.IMPLEMENTATION_COLOR_READ_TYPE),format=gl.getParameter(gl.IMPLEMENTATION_COLOR_READ_FORMAT);
            let pixels=type===35899 ? new Uint32Array(width*height) : type===gl.FLOAT ? new Float32Array(width*height*4) : new Uint8Array(width*height*4);
            gl.readPixels(0,0,width,height,format,type,pixels);
            if(type===35899) {
                const unpack=(v,m)=>{const e=v>>m,f=v&((1<<m)-1);return e ? (1+f/(1<<m))*Math.pow(2,e-15) : f/(1<<m)*Math.pow(2,-14);};
                const decoded=new Float32Array(width*height*4);
                pixels.forEach((v,index)=>{decoded[index*4]=unpack(v&2047,6);decoded[index*4+1]=unpack((v>>>11)&2047,6);decoded[index*4+2]=unpack(v>>>22,5);decoded[index*4+3]=1;});
                pixels=decoded;type=gl.FLOAT;format=gl.RGBA;
            }
            const bytes=new Uint8Array(pixels.buffer);
            let binary='';for(let n=0;n<bytes.length;n+=8192)binary+=String.fromCharCode(...bytes.subarray(n,n+8192));
            return {width,height,type,format,rgba:btoa(binary)};
        }
        for (const method of ['drawElements', 'drawArrays']) {
            const original = proto[method];
            proto[method] = function (...args) {
                const gl = this, capture = window.waterReference;
                let capturedRecord;
                if (capture.armed && capture.records.length < 16) {
                    const program = gl.getParameter(gl.CURRENT_PROGRAM);
                    const sources = gl.getAttachedShaders(program).map(shader => gl.getShaderSource(shader));
                    const fragment = sources.find(source => source.includes('SV_Target0') || source.includes('SV_TARGET0')) || sources.join('\n');
                    const kind = fragment.includes('_SurfaceFoamVoronoiOpacity') ? 'WaterSurface' : fragment.includes('_UnderwaterTopColor') ? 'Underwater' : fragment.includes('_DepthFadePower') ? 'LightRays' : null;
                    if (kind && !capture.records.some(record => record.kind === kind)) {
                        if(window.waterNeedRipple && kind!=='WaterSurface' && !capture.rippleReady)return original.apply(gl,args);
                        try {
                            const record = { kind, method, args, sources, uniforms: {}, attributes: [], textures: {}, viewport: Array.from(gl.getParameter(gl.VIEWPORT)) };
                            const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
                            const oldUniformBuffer = gl.getParameter(gl.UNIFORM_BUFFER_BINDING);
                            const blocks = new Map();
                            for (let i = 0; i < count; i++) {
                                const info = gl.getActiveUniform(program, i);
                                const block = gl.getActiveUniforms(program, [i], gl.UNIFORM_BLOCK_INDEX)[0];
                                let value;
                                if (block < 0 || block === 0xffffffff) {
                                    value = gl.getUniform(program, gl.getUniformLocation(program, info.name));
                                    if (info.size > 1) value = Array.from({ length: info.size }, (_, index) => {
                                        const item = gl.getUniform(program, gl.getUniformLocation(program, info.name.replace('[0]', '[' + index + ']')));
                                        return typeof item === 'object' ? Array.from(item) : item;
                                    }).flat();
                                }
                                else {
                                    if (!blocks.has(block)) {
                                        const binding = gl.getActiveUniformBlockParameter(program, block, gl.UNIFORM_BLOCK_BINDING);
                                        const buffer = gl.getIndexedParameter(gl.UNIFORM_BUFFER_BINDING, binding);
                                        const start = gl.getIndexedParameter(gl.UNIFORM_BUFFER_START, binding);
                                        gl.bindBuffer(gl.UNIFORM_BUFFER, buffer);
                                        const bytes = new Uint8Array(gl.getActiveUniformBlockParameter(program, block, gl.UNIFORM_BLOCK_DATA_SIZE));
                                        gl.getBufferSubData(gl.UNIFORM_BUFFER, start, bytes);
                                        blocks.set(block, bytes.buffer);
                                    }
                                    const offset = gl.getActiveUniforms(program, [i], gl.UNIFORM_OFFSET)[0];
                                    value = new Float32Array(blocks.get(block), offset, (lengths[info.type] || 1) * info.size);
                                }
                                record.uniforms[info.name] = value !== null && typeof value === 'object' ? Array.from(value) : value;
                            }
                            gl.bindBuffer(gl.UNIFORM_BUFFER, oldUniformBuffer);
                            if(window.waterNeedRipple && kind==='WaterSurface' && !(Number(record.uniforms._RippleCount)>0)) return original.apply(gl,args);
                            if(window.waterNeedRipple && kind==='WaterSurface') {
                                capture.firstRippleTime??=Number(record.uniforms._Time[1]);
                                if(Number(record.uniforms._Time[1])-capture.firstRippleTime<.3)return original.apply(gl,args);
                                capture.rippleReady=true;
                            }
                            const active = gl.getParameter(gl.ACTIVE_TEXTURE), readFramebuffer = gl.getParameter(gl.READ_FRAMEBUFFER_BINDING);
                            for(const key of ['_NormalTexture','_UnderwaterDistortionMap','_SurfaceFoamTexture','_SurfaceFoamTextureDistortionMap']) {
                                if(record.uniforms[key]===undefined)continue;
                                gl.activeTexture(gl.TEXTURE0+record.uniforms[key]);
                                window.waterNativeTextures[key]={gl,texture:gl.getParameter(gl.TEXTURE_BINDING_2D)};
                                record.textureFilters??={};record.textureFilters[key]={min:gl.getTexParameter(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER),mag:gl.getTexParameter(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER)};
                            }
                            const framebuffer = gl.createFramebuffer(); gl.bindFramebuffer(gl.READ_FRAMEBUFFER, framebuffer);
                            for (const key of ['_LocalReflectionMap', '_LocalUnderwaterSurfaceMap', '_CameraSortingLayerTexture', '_ShapeLightTexture0','_RippleCentersTex','_RippleParamsTex','_RippleExtraParamsTex']) {
                                const unit = record.uniforms[key]; if (unit === undefined) continue;
                                gl.activeTexture(gl.TEXTURE0 + unit);
                                const texture = gl.getParameter(gl.TEXTURE_BINDING_2D), size = textureSizes.get(texture);
                                if (!size) continue;
                                gl.framebufferTexture2D(gl.READ_FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
                                if (gl.checkFramebufferStatus(gl.READ_FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) continue;
                                record.textures[key] = readColor(gl,...size);
                            }
                            gl.bindFramebuffer(gl.READ_FRAMEBUFFER, readFramebuffer); gl.deleteFramebuffer(framebuffer); gl.activeTexture(active);
                            const oldArray = gl.getParameter(gl.ARRAY_BUFFER_BINDING);
                            for (let i = 0; i < gl.getProgramParameter(program, gl.ACTIVE_ATTRIBUTES); i++) {
                                const info = gl.getActiveAttrib(program, i), loc = gl.getAttribLocation(program, info.name);
                                if (!gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_ENABLED)) {
                                    record.attributes.push({ name: info.name, constant: Array.from(gl.getVertexAttrib(loc, gl.CURRENT_VERTEX_ATTRIB)) });
                                    continue;
                                }
                                const buffer = gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_BUFFER_BINDING);
                                if (!buffer) continue;
                                gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
                                const size = gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_SIZE), stride = gl.getVertexAttrib(loc, gl.VERTEX_ATTRIB_ARRAY_STRIDE) || size * 4;
                                const offset = gl.getVertexAttribOffset(loc, gl.VERTEX_ATTRIB_ARRAY_POINTER);
                                const bytes = new Uint8Array(gl.getBufferParameter(gl.ARRAY_BUFFER, gl.BUFFER_SIZE));
                                gl.getBufferSubData(gl.ARRAY_BUFFER, 0, bytes);
                                const view = new DataView(bytes.buffer), samples = [], values = [], min = Array(size).fill(Infinity), max = Array(size).fill(-Infinity);
                                for (let p = offset, n = 0; p + size * 4 <= bytes.length; p += stride, n++) {
                                    const value = Array.from({ length: size }, (_, j) => view.getFloat32(p + j * 4, true));
                                    values.push(...value);
                                    value.forEach((v, j) => { min[j] = Math.min(min[j], v); max[j] = Math.max(max[j], v); });
                                    if (n < 4 || p + stride * 4 >= bytes.length) samples.push(value);
                                }
                                record.attributes.push({ name: info.name, size, stride, offset, min, max, samples, ...(values.length ? { values } : {}) });
                            }
                            gl.bindBuffer(gl.ARRAY_BUFFER, oldArray);
                            if(method==='drawElements') {
                                const indexBytes=new Uint8Array(gl.getBufferParameter(gl.ELEMENT_ARRAY_BUFFER,gl.BUFFER_SIZE));
                                gl.getBufferSubData(gl.ELEMENT_ARRAY_BUFFER,0,indexBytes);
                                record.indices=Array.from(args[2]===gl.UNSIGNED_SHORT?new Uint16Array(indexBytes.buffer,args[3],args[1]):new Uint32Array(indexBytes.buffer,args[3],args[1]));
                            }
                            capture.records.push(record);
                            capturedRecord = record;
                        } catch (error) { capture.errors.push(String(error)); }
                    }
                }
                const result = original.apply(gl, args);
                if (capturedRecord && capturedRecord.viewport[2] === 1280) {
                    const read=gl.getParameter(gl.READ_FRAMEBUFFER_BINDING);
                    gl.bindFramebuffer(gl.READ_FRAMEBUFFER,gl.getParameter(gl.DRAW_FRAMEBUFFER_BINDING));
                    capturedRecord.output=readColor(gl,1280,762);
                    gl.bindFramebuffer(gl.READ_FRAMEBUFFER,read);
                }
                return result;
            };
        }
    },interacting);
    await page.goto(process.argv.find(arg=>arg.startsWith('http')) || 'http://127.0.0.1:8123/');
    await page.waitForFunction(() => document.querySelector('#unity-loading-bar')?.style.display === 'none', { timeout: 240000 });
    await page.waitForTimeout(4000);
    if(interacting) {
        await page.evaluate(()=>{window.waterReference.armed=true;});
        await page.locator('#unity-canvas').click({position:{x:290,y:440}});
        await page.keyboard.down('d'); await page.keyboard.press('j');
        await page.waitForTimeout(2500); await page.keyboard.up('d');
    }
    await page.evaluate(() => { window.waterReference.armed = true; });
    await page.waitForFunction(() => window.waterReference.records.some(r => r.kind === 'WaterSurface'), { timeout: 60000 });
    await page.waitForTimeout(1000);
    const record = await page.evaluate(() => { window.waterReference.armed = false; return window.waterReference; });
    await writeFile(path.join(out, 'draw-inputs.json'), JSON.stringify(record, null, 2));
    if(!interacting) {
    const runtime = {};
    for (const program of record.records) runtime[program.kind] = Object.fromEntries(Object.entries(program.uniforms)
        .filter(([key]) => !key.startsWith('Xhlslcc_Unused') && !key.startsWith('hlslcc_') && !/^_(Time|ProjectionParams|ScaledScreenParams|GlobalMipBias)/.test(key) && !/Tex$|Texture\d*$|Map$/.test(key))
        .map(([key, value]) => [key, Array.isArray(value) && value.length === 1 ? value[0] : value]));
    for(const program of Object.values(runtime))if('_RippleCount' in program)program._RippleCount=0;
    await writeFile(path.resolve('Mods/LevelEditor/Web/bpsim/ultimate-water/runtime-profile.json'), JSON.stringify(runtime, null, 2));
    const ray = record.records.find(program => program.kind === 'LightRays');
    if (ray) await writeFile(path.resolve('Mods/LevelEditor/Web/bpsim/ultimate-water/light-rays-mesh.json'), JSON.stringify(Object.fromEntries(ray.attributes.filter(attribute => attribute.values).map(attribute => [attribute.name, attribute.values]))));
    for(const program of record.records) {
        const fragment=program.sources.find(source=>source.includes('void main()')&&(source.includes('SV_Target0')||source.includes('SV_TARGET0')));
        if(fragment)await writeFile(path.resolve('Mods/LevelEditor/Web/bpsim/ultimate-water/'+program.kind+'.active.glsl'),fragment);
        const vertex=program.sources.find(source=>source.includes('gl_Position'));
        if(vertex)await writeFile(path.resolve('Mods/LevelEditor/Web/bpsim/ultimate-water/'+program.kind+'.vertex.glsl'),vertex);
    }
    const lighting={};
    const surface=record.records.find(p=>p.kind==='WaterSurface'),vp=surface.uniforms['hlslcc_mtx4x4unity_MatrixVP[0]'],model=surface.uniforms['hlslcc_mtx4x4unity_ObjectToWorld[0]'];
    const distance=vp[15]+model[14],cx=-vp[12]/vp[0]-model[12],cy=-vp[13]/vp[5],w=2*distance/vp[0],h=2*distance/vp[5];
    for(const program of record.records) {
        const light=program.textures._ShapeLightTexture0;if(!light)continue;
        const filename=program.kind+'.lighting.bin';
        await writeFile(path.resolve('Mods/LevelEditor/Web/bpsim/ultimate-water/'+filename),Buffer.from(light.rgba,'base64'));
        lighting[program.kind]={file:filename,width:light.width,height:light.height,type:light.type,rect:[cx-w/2,cy-h/2,w,h]};
    }
    await writeFile(path.resolve('Mods/LevelEditor/Web/bpsim/ultimate-water/lighting-profile.json'),JSON.stringify(lighting,null,2));
    }
    await page.locator('#unity-canvas').screenshot({ path: path.join(out, 'reference.png') });
    const native=await page.evaluate(()=>{
        const results={};
        for(const [key,{gl,texture}]of Object.entries(window.waterNativeTextures)) {
            const program=gl.createProgram();
            for(const [type,source]of [[gl.VERTEX_SHADER,'#version 300 es\nout vec2 uv;void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);uv=p;gl_Position=vec4(p*2.-1.,0,1);}'],[gl.FRAGMENT_SHADER,'#version 300 es\nprecision highp float;in vec2 uv;uniform sampler2D tex;out vec4 color;void main(){color=textureLod(tex,uv,0.0);}']]) {
                const shader=gl.createShader(type);gl.shaderSource(shader,source);gl.compileShader(shader);gl.attachShader(program,shader);
            }
            gl.linkProgram(program);gl.useProgram(program);const target=gl.createTexture();gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,target);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA8,512,512,0,gl.RGBA,gl.UNSIGNED_BYTE,null);
            const fbo=gl.createFramebuffer();gl.bindFramebuffer(gl.FRAMEBUFFER,fbo);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,target,0);
            gl.bindVertexArray(gl.createVertexArray());gl.viewport(0,0,512,512);gl.disable(gl.BLEND);gl.disable(gl.DEPTH_TEST);gl.disable(gl.SCISSOR_TEST);gl.disable(gl.CULL_FACE);gl.colorMask(true,true,true,true);
            gl.bindTexture(gl.TEXTURE_2D,texture);gl.uniform1i(gl.getUniformLocation(program,'tex'),0);gl.drawArrays(gl.TRIANGLES,0,3);
            const pixels=new Uint8Array(512*512*4);gl.readPixels(0,0,512,512,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
            let binary='';for(let n=0;n<pixels.length;n+=8192)binary+=String.fromCharCode(...pixels.subarray(n,n+8192));results[key]=btoa(binary);
        }
        return results;
    });
    await writeFile(path.join(out,'native-textures.json'),JSON.stringify(native));
    console.log(JSON.stringify({ output: out, programs: record.records.map(r => r.kind), errors: record.errors }));
} finally { await context.close(); await rm(profile, { recursive: true, force: true }); }

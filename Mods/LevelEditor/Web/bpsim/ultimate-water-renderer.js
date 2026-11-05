(function (root) {
    "use strict";
    const base = "/bpsim/ultimate-water/";
    const vertex = `#version 300 es
    layout(location=0) in vec2 position;
    layout(location=1) in vec2 world;
    layout(location=2) in vec2 uv;
    out vec2 uw_world; out vec2 uw_uv;
    void main(){gl_Position=vec4(position,0,1);uw_world=world;uw_uv=uv;}`;
    class UltimateWaterRenderer {
        constructor() {
            this.canvas = document.createElement("canvas");
            this.gl = this.canvas.getContext("webgl2", { alpha: true, antialias: true, premultipliedAlpha: false });
            this.mask = document.createElement("canvas"); this.maskContext = this.mask.getContext("2d");
            this.mirror = document.createElement("canvas"); this.mirrorContext = this.mirror.getContext("2d");
            this.underwaterCapture=document.createElement("canvas");this.underwaterCaptureContext=this.underwaterCapture.getContext("2d");
            this.ready = false; this.error = null;
            if (this.gl) this.loading = this.load().catch(error => { this.error = String(error); console.error("Ultimate Water:", error); });
        }
        texture(pixel = [0, 0, 0, 255]) {
            const gl = this.gl, texture = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(pixel));
            return texture;
        }
        upload(texture, image, clamp = false) {
            const gl = this.gl; gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true); gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, clamp ? gl.CLAMP_TO_EDGE : gl.REPEAT);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, clamp ? gl.CLAMP_TO_EDGE : gl.REPEAT);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
        }
        compile(fragment, vertexSource = vertex) {
            const gl = this.gl, program = gl.createProgram();
            for (const [type, source] of [[gl.VERTEX_SHADER, vertexSource], [gl.FRAGMENT_SHADER, fragment]]) {
                const shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
                if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
                gl.attachShader(program, shader); gl.deleteShader(shader);
            }
            gl.linkProgram(program);
            if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
            return { program, locations: {} };
        }
        async load() {
            const json = async name => { const response = await fetch(base + name); if (!response.ok) throw new Error(name + ": " + response.status); return response.json(); };
            [this.profile, this.runtime, this.metadata] = await Promise.all([json("demo-profile.json"), json("runtime-profile.json"), json("programs.json")]);
            this.rayMesh = await json("light-rays-mesh.json");
            this.programs = {};
            for (const name of ["WaterSurface", "Underwater", "LightRays"]) {
                const response = await fetch(base + name + ".fragment.glsl");
                if (!response.ok) throw new Error(name + " program missing");
                const rayVertex = `#version 300 es
                layout(location=0) in vec2 position; layout(location=1) in vec2 world; layout(location=2) in vec2 uv;
                layout(location=3) in vec2 seed; layout(location=4) in vec3 local; layout(location=5) in vec4 color;
                out vec2 uw_world; out vec2 uw_uv; out vec2 uw_seed; out vec3 uw_local; out vec4 uw_color;
                void main(){gl_Position=vec4(position,0,1);uw_world=world;uw_uv=uv;uw_seed=seed;uw_local=local;uw_color=color;}`;
                const surfaceVertex=`#version 300 es
                layout(location=0) in vec2 local;layout(location=2) in vec2 uv;
                uniform vec4 uw_CaptureRect,uw_Bounds;
                out vec2 uw_world,uw_uv,uw_local;
                void main(){vec2 point=vec2(local.x*uw_Bounds.z*8./303.75,local.y);float cx=(uw_CaptureRect.x+uw_CaptureRect.z*.5-uw_Bounds.x-uw_Bounds.z*.5)*8.;
                float d=19.4+local.y,p=19.4/d;
                uw_world=vec2(uw_Bounds.x+uw_Bounds.z*.5+(cx+(point.x-cx)*p)*.125,uw_Bounds.y+3.8*(1.-p)*.125);
                gl_Position=vec4(((uw_world-uw_CaptureRect.xy)/uw_CaptureRect.zw*2.-1.)*d,0,d);uw_uv=uv;uw_local=point;}`;
                const originalVertex=await (await fetch(base+name+'.vertex-port.glsl')).text();
                this.programs[name] = this.compile(await response.text(),originalVertex);
            }
            this.black = this.texture([0, 0, 0, 0]); this.white = this.texture([255, 255, 255, 255]);
            if(!this.gl.getExtension('OES_texture_float_linear')) throw Error('Floating point texture filtering unavailable');
            this.lightingProfile=await json('lighting-profile.json');this.lighting={};
            for(const [name,info]of Object.entries(this.lightingProfile)) {
                const response=await fetch(base+info.file);if(!response.ok)throw Error(info.file+' missing');
                const texture=this.texture(),gl=this.gl;
                gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
                gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA32F,info.width,info.height,0,gl.RGBA,gl.FLOAT,new Float32Array(await response.arrayBuffer()));
                this.lighting[name]=texture;
            }
            this.scene = this.texture(); this.sceneHalf=this.texture();this.terrain = this.texture(); this.reflection = this.texture(); this.rays = this.texture([0,0,0,0]); this.rayFramebuffer = this.gl.createFramebuffer(); this.textures = {};
            await Promise.all(Object.entries(this.profile.textures).map(async ([name,info]) => {
                const response=await fetch(base+name+'.mips.bin'); if(!response.ok)throw Error(name+' mip data missing');
                const bytes=new Uint8Array(await response.arrayBuffer()),texture=this.texture(),gl=this.gl;
                gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);
                let offset=0;
                for(let level=0;level<info.mip_count;level++) {
                    const w=Math.max(1,info.width>>level),h=Math.max(1,info.height>>level),count=w*h*4;
                    gl.texImage2D(gl.TEXTURE_2D,level,info.color_space===1?gl.SRGB8_ALPHA8:gl.RGBA8,w,h,0,gl.RGBA,gl.UNSIGNED_BYTE,bytes.subarray(offset,offset+count));offset+=count;
                }
                gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR_MIPMAP_NEAREST);
                this.textures[name] = texture;
            }));
            this.surfaceInfo=await json('surface-mesh.json');
            const meshResponse=await fetch(base+'surface-mesh.bin');if(!meshResponse.ok)throw Error('Surface mesh missing');
            const mesh=await meshResponse.arrayBuffer(),gl=this.gl;
            this.surfaceBuffer=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,this.surfaceBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(mesh,0,this.surfaceInfo.vertices*4),gl.STATIC_DRAW);
            this.surfaceIndices=gl.createBuffer();gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,this.surfaceIndices);gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,new Uint16Array(mesh,this.surfaceInfo.vertices*16,this.surfaceInfo.indices),gl.STATIC_DRAW);
            this.buffer = this.gl.createBuffer(); this.ready = true;
            this.rippleTextures={};for(const key of ['_RippleCentersTex','_RippleParamsTex','_RippleExtraParamsTex'])this.rippleTextures[key]=this.texture([0,0,0,0]);
        }
        uniform(pass, key, type, value) {
            const gl = this.gl;
            if (!(key in pass.locations)) pass.locations[key] = gl.getUniformLocation(pass.program, key);
            const loc = pass.locations[key]; if (loc === null || value === undefined) return;
            if (type === "sampler2D") gl.uniform1i(loc, value);
            else if (type === "float") gl.uniform1f(loc, Array.isArray(value) ? value[0] : value);
            else gl["uniform" + type.slice(3) + "fv"](loc, value);
        }
        bind(name, body, rect, time, uniformOverrides) {
            const gl = this.gl, pass = this.programs[name], metadata = this.metadata[name];
            const material = this.profile.materials[name === "WaterSurface" ? "SurfaceWater" : name] || { textures: {}, floats: {}, vectors: {} };
            gl.useProgram(pass.program);
            let unit = 0;
            for (const [key, info] of Object.entries(metadata.uniforms)) {
                if (info.type === "sampler2D") {
                    let texture = this.textures[material.textures[key]?.name] || this.white;
                    if (key.includes("Ripple") && key.endsWith("Tex")) texture = this.black;
                    if(this.rippleTextures[key]) texture=this.rippleTextures[key];
                    if (key === "_CameraSortingLayerTexture") texture = this.scene;
                    if (key === "_LocalUnderwaterSurfaceMap") texture = this.sceneHalf;
                    if (key === "_LocalReflectionMap") texture = this.reflection;
                    if (key === "_ShapeLightTexture0") texture=this.lighting[name]||this.white;
                    texture = this.referenceTextures?.[name]?.[key] || texture;
                    gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, texture); this.uniform(pass, key, info.type, unit++);
                } else {
                    let value = uniformOverrides?.[name]?.[key] ?? this.runtime[name]?.[key] ?? material.floats[key] ?? material.vectors[key] ?? this.profile.shaders[name].properties[key];
                    if (key.startsWith("hlslcc_")) value = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
                    if (key === "_Time") value = [time / 20, time, time * 2, time * 3];
                    if (key === "_TimeParameters") value = [time, Math.sin(time), Math.cos(time), 0];
                    if (key === "_ProjectionParams") value = [1, 0, 0, 0];
                    if (key === "_ScaledScreenParams") value = [this.canvas.width, this.canvas.height, 0, 0];
                    if (key === "_GlobalMipBias") value = [0, 0];
                    if (key === "_RippleCount") value=this.rippleCount||0;
                    if (key === "_WaveAmplitude") value=body.wave_amplitude*8;
                    if (key === "_WaveFrequency") value=Math.PI*2/(body.wave_length*8);
                    if (key === "_WaveSpeed") value=body.wave_speed;
                    if (key === "_LocalXYBounds") value = [-body.width*4,body.width*4,-body.depth*8,0];
                    this.uniform(pass, key, info.type, value);
                }
            }
            gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, this.terrain); this.uniform(pass, "uw_Terrain", "sampler2D", unit);
            gl.activeTexture(gl.TEXTURE0 + unit + 1); gl.bindTexture(gl.TEXTURE_2D, name === "LightRays" || this.referenceTextures ? this.black : this.rays); this.uniform(pass, "uw_Rays", "sampler2D", unit + 1);
            this.uniform(pass, "uw_CaptureRect", "vec4", rect);
            const light=this.lightingProfile[name]?.rect;
            this.uniform(pass,"uw_LightRect","vec4",this.referenceTextures?rect:light?[body.x+body.width*.5+light[0]/8,body.surface_y+light[1]/8,light[2]/8,light[3]/8]:rect);
            this.uniform(pass, "uw_Bounds", "vec4", [body.x, body.surface_y, body.width, body.depth]);
            this.uniform(pass, "uw_Scale", "float", 0.125);
            this.uniform(pass, "uw_CapturesLinear", "float", this.referenceTextures?1:0);
            return pass;
        }
        drawRays(bodies,camera,size,rect,time,uniformOverrides) {
            const gl=this.gl, mesh=this.rayMesh, width=Math.max(1,this.canvas.width>>1), height=Math.max(1,this.canvas.height>>1);
            gl.bindTexture(gl.TEXTURE_2D,this.rays); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
            gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,width,height,0,gl.RGBA,gl.UNSIGNED_BYTE,null);
            gl.bindFramebuffer(gl.FRAMEBUFFER,this.rayFramebuffer); gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,this.rays,0);
            gl.viewport(0,0,width,height); gl.clearColor(0,0,0,0); gl.clear(gl.COLOR_BUFFER_BIT); gl.blendFunc(gl.ONE,gl.ONE_MINUS_SRC_ALPHA);
            for(const body of bodies) {
                this.bind("LightRays",body,rect,time,uniformOverrides); const data=[], vertices=[];
                for(let i=0;i<mesh.in_POSITION0.length/3;i++) {
                    const uv=mesh.in_TEXCOORD0.slice(i*2,i*2+2),seed=mesh.in_TEXCOORD1.slice(i*2,i*2+2),extra=mesh.in_TEXCOORD2.slice(i*2,i*2+2);
                    let [x,y,z]=mesh.in_POSITION0.slice(i*3,i*3+3);
                    x*=body.width*8/303.75;
                    vertices.push([x,y,z,...uv,...seed,...extra,...mesh.in_COLOR0.slice(i*4,i*4+4)]);
                }
                for(let i=0;i<vertices.length;i+=4) for(const index of [0,1,2,2,1,3]) data.push(...vertices[i+index]);
                this.lastRayData=data;
                gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(data),gl.STREAM_DRAW);
                let offset=0; [3,2,2,2,4].forEach((count,index)=>{gl.enableVertexAttribArray(index);gl.vertexAttribPointer(index,count,gl.FLOAT,false,52,offset*4);offset+=count;});
                gl.drawArrays(gl.TRIANGLES,0,data.length/13);
            }
            gl.bindFramebuffer(gl.FRAMEBUFFER,null); gl.viewport(0,0,this.canvas.width,this.canvas.height);
            gl.blendFuncSeparate(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA,gl.ONE,gl.ONE_MINUS_SRC_ALPHA);
            for(let i=3;i<6;i++)gl.disableVertexAttribArray(i);
        }
        updateRipples(body,time,ripples) {
            const gl=this.gl,active=ripples.filter(r=>r.id===body.id&&time-r.time<40/6).slice(-64);
            const data={_RippleCentersTex:new Float32Array(256),_RippleParamsTex:new Float32Array(256),_RippleExtraParamsTex:new Float32Array(256)};
            active.forEach((r,i)=>{data._RippleCentersTex.set([(r.x-body.x-body.width*.5)*8,1.6,r.time,40],i*4);data._RippleParamsTex.set([Math.max(.1,Math.min(4,Math.abs(r.amplitude)/.06)),3,6,2.5],i*4);data._RippleExtraParamsTex.set([1,1,1,0],i*4);});
            for(const [key,pixels]of Object.entries(data)) {gl.bindTexture(gl.TEXTURE_2D,this.rippleTextures[key]);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA32F,64,1,0,gl.RGBA,gl.FLOAT,pixels);}
            this.rippleCount=active.length;
        }
        draw(bodies, camera, size, source, meshes, time, ripples, onlyPass = null, uniformOverrides = null) {
            if (!this.ready || !source.width || !source.height) return false;
            const gl = this.gl, ratio = Math.min(2, root.devicePixelRatio || 1);
            const width = Math.max(1, Math.round(size.width * ratio)), height = Math.max(1, Math.round(size.height * ratio));
            if (this.canvas.width !== width || this.canvas.height !== height) { this.canvas.width = width; this.canvas.height = height; }
            gl.viewport(0, 0, width, height); gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
            gl.disable(gl.CULL_FACE); gl.disable(gl.DEPTH_TEST); gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
            this.upload(this.scene, source, true);
            const halfWidth=Math.max(1,source.width>>1),halfHeight=Math.max(1,source.height>>1);
            if(this.underwaterCapture.width!==halfWidth||this.underwaterCapture.height!==halfHeight){this.underwaterCapture.width=halfWidth;this.underwaterCapture.height=halfHeight;this.mirror.width=halfWidth;this.mirror.height=halfHeight;}
            this.underwaterCaptureContext.drawImage(source,0,0,halfWidth,halfHeight);this.upload(this.sceneHalf,this.underwaterCapture,true);
            this.mask.width = source.width; this.mask.height = source.height;
            const mask = this.maskContext;
            const screen = (x, y) => [(x - camera.x) * camera.zoom + size.width / 2, size.height / 2 - (y - camera.y) * camera.zoom];
            mask.fillStyle = "white";
            for (const mesh of meshes) for (let i = 0; i < mesh.indices.length; i += 3) {
                mask.beginPath();
                mesh.indices.slice(i, i + 3).forEach((index, j) => { const point = mesh.vertices[index]; if (point) mask[j ? "lineTo" : "moveTo"](...screen(point.x, point.y)); });
                mask.closePath(); mask.fill();
            }
            this.upload(this.terrain, this.mask, true);
            const rect = [camera.x - size.width / camera.zoom / 2, camera.y - size.height / camera.zoom / 2, size.width / camera.zoom, size.height / camera.zoom];
            gl.bindBuffer(gl.ARRAY_BUFFER, this.buffer);
            this.drawRays(bodies,camera,size,rect,time,uniformOverrides);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
            for (let i = 0; i < 3; i++) { gl.enableVertexAttribArray(i); gl.vertexAttribPointer(i, 2, gl.FLOAT, false, 24, i * 8); }
            for (const body of bodies) {
                this.updateRipples(body,time,ripples);
                const horizon = screen(0, body.surface_y)[1] * halfHeight / size.height;
                this.mirrorContext.setTransform(1, 0, 0, -1, 0, 2 * horizon); this.mirrorContext.drawImage(this.underwaterCapture, 0, 0);
                this.upload(this.reflection, this.mirror, true);
                for (const name of ["Underwater", "WaterSurface"]) {
                    if (onlyPass && name !== onlyPass) continue;
                    this.bind(name, body, rect, time, uniformOverrides);
                    if(name==='WaterSurface') {
                        gl.bindBuffer(gl.ARRAY_BUFFER,this.surfaceBuffer);gl.vertexAttribPointer(0,2,gl.FLOAT,false,16,0);gl.disableVertexAttribArray(1);gl.vertexAttribPointer(2,2,gl.FLOAT,false,16,8);
                        gl.frontFace(gl.CW);gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,this.surfaceIndices);gl.drawElements(gl.TRIANGLES,this.surfaceInfo.indices,gl.UNSIGNED_SHORT,0);gl.frontFace(gl.CCW);
                        gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer);gl.enableVertexAttribArray(1);
                        for(let i=0;i<3;i++)gl.vertexAttribPointer(i,2,gl.FLOAT,false,24,i*8);
                        continue;
                    }
                    const data = [], count = Math.max(48, Math.min(16384, Math.ceil(body.width * 32)));
                    const vertex = (x, y, u, v) => data.push((x - camera.x) * camera.zoom / size.width * 2, (y - camera.y) * camera.zoom / size.height * 2, x, y, u, v);
                    const surfaceAt = root.bpsimWaterCanvas?.surfaceAt || (() => body.surface_y);
                    for (let i = 0; i < count; i++) {
                        const u0 = i / count, u1 = (i + 1) / count, x0 = body.x + u0 * body.width, x1 = body.x + u1 * body.width;
                        const y0 = body.surface_y, y1 = body.surface_y;
                        const top0 = name === "WaterSurface" ? body.surface_y + 0.445625 : y0, top1 = name === "WaterSurface" ? body.surface_y + 0.445625 : y1;
                        const bottom0 = name === "WaterSurface" ? y0 : body.surface_y - body.depth, bottom1 = name === "WaterSurface" ? y1 : body.surface_y - body.depth;
                        vertex(x0, bottom0, u0, 0); vertex(x1, bottom1, u1, 0); vertex(x0, top0, u0, 1);
                        vertex(x0, top0, u0, 1); vertex(x1, bottom1, u1, 0); vertex(x1, top1, u1, 1);
                    }
                    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(data), gl.STREAM_DRAW); gl.drawArrays(gl.TRIANGLES, 0, data.length / 6);
                }
            }
            return true;
        }
    }
    root.UltimateWaterRenderer = UltimateWaterRenderer;
})(typeof globalThis !== "undefined" ? globalThis : this);

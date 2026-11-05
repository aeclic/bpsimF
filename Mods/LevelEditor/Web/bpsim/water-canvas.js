(function (root) {
    "use strict";
    const defaults = Object.freeze({
        physics_enabled: false, wave_length: 2.5, density: 1, buoyancy_multiplier: 1,
        linear_drag: 0.5, angular_drag: 0, flow_x: 0, flow_y: 0, flow_acceleration: 0,
        ripples_enabled: true, ripple_strength: 0.06, ripple_damping: 2, ripple_speed: 3,
        refraction_strength: 0.05, caustics_strength: 0.35, reflection_strength: 0.663, foam_strength: 1,
    });
    const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
    const copy = value => JSON.parse(JSON.stringify(value));
    function migrate(body) {
        const output = copy(body);
        for (const [key, value] of Object.entries(defaults)) if (output[key] === undefined) output[key] = value;
        return output;
    }
    function screen(point, camera, size) {
        return { x: size.width / 2 + (point.x - camera.x) * camera.zoom,
            y: size.height / 2 - (point.y - camera.y) * camera.zoom };
    }
    function world(point, camera, size) {
        return { x: camera.x + (point.x - size.width / 2) / camera.zoom,
            y: camera.y - (point.y - size.height / 2) / camera.zoom };
    }
    function surfaceAt(body, x, time, ripples = []) {
        const amplitude = Math.min(body.wave_amplitude, body.depth * 0.45);
        const phase = (x - body.x) * Math.PI * 2 / Math.max(0.01, body.wave_length) - time * body.wave_speed;
        let height = amplitude * (Math.sin(phase) + 0.35 * Math.sin(phase * 1.73 + time * body.wave_speed * 0.27)) / 1.35;
        for (const ripple of ripples) {
            if (ripple.id !== body.id) continue;
            const age = Math.max(0, time - ripple.time), distance = Math.abs(x - ripple.x);
            const spread = Math.max(0.15, ripple.radius + age * 0.2);
            const front = distance - age * body.ripple_speed;
            height += ripple.amplitude * Math.exp(-body.ripple_damping * age) *
                Math.cos(front * 14) * Math.exp(-(front * front) / (spread * spread));
        }
        return body.surface_y + clamp(height, -body.depth * 0.45, body.depth * 0.45);
    }
    function terrainMeshes(level) {
        const meshes = [];
        for (const item of level?.objects || []) {
            const p = item.Prefab;
            if (!p?.terrain_data?.fill_mesh) continue;
            const angle = p.rotation.z * Math.PI / 180, c = Math.cos(angle), s = Math.sin(angle);
            meshes.push({
                vertices: p.terrain_data.fill_mesh.vertices.map(v => ({
                    x: p.position.x + v.x * p.scale.x * c - v.y * p.scale.y * s,
                    y: p.position.y + v.x * p.scale.x * s + v.y * p.scale.y * c,
                })),
                indices: p.terrain_data.fill_mesh.indices,
            });
        }
        return meshes;
    }
    function contains(body, point) {
        return point.x >= body.x && point.x <= body.x + body.width &&
            point.y <= body.surface_y && point.y >= body.surface_y - body.depth;
    }
    function hitHandle(body, point, zoom, pixels = 12) {
        const corners = { nw: [body.x, body.surface_y], ne: [body.x + body.width, body.surface_y],
            sw: [body.x, body.surface_y - body.depth], se: [body.x + body.width, body.surface_y - body.depth] };
        for (const [name, [x, y]] of Object.entries(corners))
            if (Math.hypot(point.x - x, point.y - y) * zoom <= pixels) return name;
        return null;
    }
    function dragBounds(original, start, end, handle = null) {
        const result = { x: original.x, surface_y: original.surface_y, width: original.width, depth: original.depth };
        const dx = end.x - start.x, dy = end.y - start.y;
        if (!handle) { result.x += dx; result.surface_y += dy; return result; }
        if (handle.includes("w")) {
            result.x = clamp(original.x + dx, original.x + original.width - 10000, original.x + original.width - 0.01);
            result.width = original.x + original.width - result.x;
        } else result.width = clamp(original.width + dx, 0.01, 10000);
        if (handle.includes("n")) {
            result.surface_y = clamp(original.surface_y + dy, original.surface_y - original.depth + 0.01, original.surface_y - original.depth + 10000);
            result.depth = result.surface_y - (original.surface_y - original.depth);
        } else result.depth = clamp(original.depth - dy, 0.01, 10000);
        return result;
    }
    function inTriangle(point, a, b, c) {
        const cross = (u, v, w) => (v.x - u.x) * (w.y - u.y) - (v.y - u.y) * (w.x - u.x);
        if (Math.abs(cross(a, b, c)) < 1e-12) return false;
        const x = cross(a, b, point), y = cross(b, c, point), z = cross(c, a, point);
        return !((x < 0 || y < 0 || z < 0) && (x > 0 || y > 0 || z > 0));
    }
    function inTerrain(point, meshes) {
        for (const mesh of meshes) for (let i = 0; i < mesh.indices.length; i += 3) {
            const [a, b, c] = mesh.indices.slice(i, i + 3).map(index => mesh.vertices[index]);
            if (a && b && c && inTriangle(point, a, b, c)) return true;
        }
        return false;
    }
    // These disposable probes explain density/flow in the editor, not a replacement for Unity's solver.
    function stepProbe(probe, bodies, dt) {
        dt = clamp(dt, 0, 1 / 30);
        probe.vy -= 9.81 / 8 * dt;
        let water = null, fraction = 0;
        for (const body of bodies) {
            if (!body.physics_enabled) continue;
            const horizontal = Math.max(0, Math.min(probe.x + probe.size / 2, body.x + body.width) - Math.max(probe.x - probe.size / 2, body.x));
            const vertical = Math.max(0, Math.min(probe.y + probe.size / 2, body.surface_y) - Math.max(probe.y - probe.size / 2, body.surface_y - body.depth));
            fraction = clamp(horizontal * vertical / (probe.size * probe.size), 0, 1);
            if (fraction > 0) { water = body; break; }
        }
        if (water) {
            probe.vy += 9.81 / 8 * water.density * water.buoyancy_multiplier / probe.density * fraction * dt;
            const dragFraction = probe.y < water.surface_y ? 1 : 0;
            const probeMass = Math.max(probe.density * probe.size * probe.size, 1e-4);
            const decay = Math.exp(-water.linear_drag * dragFraction * dt / probeMass);
            if (dragFraction > 0) {
                probe.vx = water.flow_x + (probe.vx - water.flow_x) * decay;
                probe.vy = water.flow_y + (probe.vy - water.flow_y) * decay;
            }
            const length = Math.hypot(water.flow_x, water.flow_y);
            if (length > 0) {
                probe.vx += water.flow_x / length * water.flow_acceleration * fraction * dt;
                probe.vy += water.flow_y / length * water.flow_acceleration * fraction * dt;
            }
            probe.spin *= Math.exp(-water.angular_drag * fraction * dt);
        }
        probe.x += probe.vx * dt; probe.y += probe.vy * dt; probe.angle += probe.spin * dt;
        return { water, fraction };
    }

    const styleDefaults = {
        surface: {
            waterColor: [0, 0.6634375453, 0.9921568036, 1], baseFarColor: [0, 0.806797266, 1, 1],
            normalStrength: 0.251, normalsTiling: [6.9, 32.27], normalTextureScale: [1, -0.98], waterSpeed: [0.04, 0], reflectivity: 0.663,
            baseGradientDistance: 1.18, baseGradientFade: 0.16, foamSharpness: 5.35, foamTextureOpacity: 1.83,
            foamTextureSize: [1.23, 2.96], foamTextureScrollSpeed: [0.04, 0],
            rippleFoamTextureTiling: [3, 2], foamVoronoiOpacity: 0.598,
        },
        textures: { normal: "WaterNormals_01.png", foam: "Foam_01.png", caustics: "Foam_02.png", surfaceFoam: "Foam_03.png" },
    };
    const waterVertexShader = `#version 300 es
        layout(location=0) in vec2 a_position;
        layout(location=1) in vec2 a_world;
        layout(location=2) in vec2 a_uv;
        layout(location=3) in float a_surface;
        layout(location=4) in vec4 a_color;
        out vec2 v_world;
        out vec2 v_uv;
        out float v_surface;
        out vec4 v_color;
        void main() {
            gl_Position = vec4(a_position, 0.0, 1.0);
            v_world = a_world;
            v_uv = a_uv;
            v_surface = a_surface;
            v_color = a_color;
        }
    `;
    const waterFragmentShader = `#version 300 es
        precision highp float;
        in vec2 v_world;
        in vec2 v_uv;
        in float v_surface;
        in vec4 v_color;
        uniform sampler2D u_scene;
        uniform sampler2D u_terrain;
        uniform sampler2D u_normal;
        uniform sampler2D u_foam;
        uniform sampler2D u_caustics;
        uniform sampler2D u_surfaceFoam;
        uniform vec4 u_captureRect;
        uniform vec4 u_waterColor;
        uniform vec4 u_baseFarColor;
        uniform vec4 u_effects;
        uniform vec4 u_foamStyle;
        uniform vec2 u_captureTexel;
        uniform vec2 u_normalsTiling;
        uniform vec2 u_normalTextureScale;
        uniform vec2 u_waterSpeed;
        uniform vec2 u_foamTiling;
        uniform vec2 u_foamScrollSpeed;
        uniform vec2 u_causticTiling;
        uniform float u_time;
        uniform float u_normalStrength;
        uniform float u_reflectivity;
        uniform float u_gradientHeight;
        uniform float u_gradientFade;
        uniform float u_opacity;
        uniform vec2 u_flow;
        uniform vec4 u_underwaterBottomColor;
        out vec4 outColor;
        void main() {
            vec2 screenUv = (v_world - u_captureRect.xy) / max(u_captureRect.zw, vec2(0.001));
            float terrain = texture(u_terrain, clamp(screenUv, 0.0, 1.0)).r;
            if (terrain > 0.5) discard;
            float depth = max(0.0, v_surface - v_world.y);
            float gradient = max(u_gradientHeight, 0.05);
            float depthFade = clamp(1.0 - exp(-depth / gradient), 0.0, 1.0);
            float deep = smoothstep(0.0, gradient * max(u_gradientFade, 0.05), depth);
            vec2 normalUv = v_uv * u_normalsTiling * u_normalTextureScale + u_time * u_waterSpeed;
            normalUv += u_flow * u_time * 0.001;
            vec2 normalA = texture(u_normal, normalUv).ag * 2.0 - 1.0;
            vec2 normalB = texture(u_normal, normalUv + vec2(0.5, 0.0)).ag * 2.0 - 1.0;
            float slope = v_color.r * 2.0 - 1.0;
            vec2 normal = (normalA - normalB) * 0.5 * u_normalStrength;
            normal += vec2(slope * 0.6, -abs(slope) * 0.18) * u_normalStrength;
            normal = clamp(normal, -0.8, 0.8);
            vec3 scene = texture(u_scene, clamp(screenUv, 0.0, 1.0)).rgb;
            vec2 refractUv = screenUv + normal * u_effects.x / max(u_captureRect.zw, vec2(0.001));
            vec3 refracted = texture(u_scene, clamp(refractUv, 0.0, 1.0)).rgb;
            vec3 transmitted = mix(scene, refracted, clamp(u_effects.x * 1.5, 0.0, 1.0));
            vec3 waterColor = mix(u_waterColor.rgb, u_baseFarColor.rgb, deep);
            waterColor = mix(waterColor, u_underwaterBottomColor.rgb, clamp(depthFade * 0.45, 0.0, 1.0));
            vec3 color = mix(transmitted, waterColor, depthFade);
            float mirrorY = (2.0 * v_surface - v_world.y - u_captureRect.y) / max(u_captureRect.w, 0.001);
            vec2 mirrorUv = vec2(screenUv.x, mirrorY) + normal * u_effects.x * 0.5 / max(u_captureRect.zw, vec2(0.001));
            float mirrorOnscreen = step(0.0, mirrorUv.y) * step(mirrorUv.y, 1.0);
            vec3 reflection = texture(u_scene, clamp(mirrorUv, 0.0, 1.0)).rgb;
            float normalZ = sqrt(max(1.0 - dot(normal, normal), 0.05));
            float fresnel = u_reflectivity * u_effects.z * (0.3 + 0.7 * pow(1.0 - normalZ, 2.0));
            fresnel *= mirrorOnscreen * exp(-depth / max(gradient * 0.24, 0.05));
            color = mix(color, reflection * vec3(0.82, 0.96, 1.0), clamp(fresnel, 0.0, 1.0));
            vec2 causticUv = v_uv * u_causticTiling + vec2(u_time * 0.035, -u_time * 0.027) + normal * 0.04;
            float causticA = texture(u_caustics, causticUv).r;
            float causticB = texture(u_caustics, causticUv * 1.37 + vec2(0.43, 0.19) - u_time * 0.018).r;
            float caustic = pow(clamp(causticA + causticB * 0.55, 0.0, 1.0), 2.2);
            caustic *= (1.0 - deep) * clamp(depth * 4.0, 0.0, 1.0);
            color += caustic * vec3(0.55, 0.92, 1.0) * u_effects.y * 0.52;
            vec2 foamUv = v_uv * u_foamTiling + u_time * u_foamScrollSpeed + normal * 0.04;
            float foamA = texture(u_foam, foamUv).r;
            float foamB = texture(u_surfaceFoam, foamUv * vec2(0.73, 1.31) + vec2(0.37, 0.61) - u_time * 0.012).r;
            float foamTexture = clamp(max(foamA, foamB * u_foamStyle.w), 0.0, 1.0);
            float shore = 0.0;
            shore = max(shore, texture(u_terrain, clamp(screenUv + vec2(u_captureTexel.x * 2.0, 0.0), 0.0, 1.0)).r);
            shore = max(shore, texture(u_terrain, clamp(screenUv - vec2(u_captureTexel.x * 2.0, 0.0), 0.0, 1.0)).r);
            shore = max(shore, texture(u_terrain, clamp(screenUv + vec2(0.0, u_captureTexel.y * 2.0), 0.0, 1.0)).r);
            shore = max(shore, texture(u_terrain, clamp(screenUv - vec2(0.0, u_captureTexel.y * 2.0), 0.0, 1.0)).r);
            float waterline = 1.0 - smoothstep(0.0, 0.012, depth);
            float crest = clamp(abs(slope) * 1.8 + v_color.g, 0.0, 1.0);
            float foam = clamp((shore * 0.72 + waterline * foamTexture * 0.2 + crest * foamTexture * 0.28) * u_effects.w, 0.0, 1.0);
            foam = pow(foam, 1.0 / max(u_foamStyle.x, 1.0));
            color = mix(color, vec3(0.88, 0.98, 1.0), clamp(foam * u_foamStyle.y * 0.38, 0.0, 1.0));
            outColor = vec4(mix(scene, color, clamp(u_opacity, 0.0, 1.0)), 1.0);
        }
    `;

    class WebWaterRenderer {
        constructor(canvas) {
            this.canvas = document.createElement("canvas");
            this.gl = this.canvas.getContext("webgl2", { alpha: true, antialias: true, premultipliedAlpha: true });
            this.program = null; this.style = styleDefaults; this.textures = {}; this.locations = {};
            this.maskCanvas = document.createElement("canvas"); this.maskContext = this.maskCanvas.getContext("2d");
            if (!this.gl) return;
            try { this.program = this.createProgram(waterVertexShader, waterFragmentShader); }
            catch (error) { console.warn("Ultimate Water WebGL renderer unavailable:", error); return; }
            this.sceneTexture = this.createTexture(); this.terrainTexture = this.createTexture();
            for (const [key, file] of Object.entries(styleDefaults.textures)) this.loadTexture(key, file);
            if (typeof fetch === "function") fetch("/bpsim/ultimate-water/style.json")
                .then(response => response.ok ? response.json() : null)
                .then(profile => { if (profile?.surface && profile?.textures) this.style = profile; })
                .catch(() => {});
        }
        createProgram(vertexSource, fragmentSource) {
            const gl = this.gl;
            const compile = (type, source) => {
                const shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
                if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
                return shader;
            };
            const program = gl.createProgram();
            gl.attachShader(program, compile(gl.VERTEX_SHADER, vertexSource));
            gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragmentSource)); gl.linkProgram(program);
            if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
            return program;
        }
        createTexture() {
            const gl = this.gl, texture = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([0, 0, 0, 255]));
            return texture;
        }
        loadTexture(key, file) {
            const gl = this.gl, texture = this.createTexture(), image = new Image();
            this.textures[key] = texture;
            image.onload = () => {
                gl.bindTexture(gl.TEXTURE_2D, texture); gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
                gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
                gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
            };
            image.src = "/bpsim/ultimate-water/" + file;
        }
        uniform(name) {
            if (!(name in this.locations)) this.locations[name] = this.gl.getUniformLocation(this.program, name);
            return this.locations[name];
        }
        draw(bodies, camera, size, source, meshes, time, ripples) {
            const gl = this.gl;
            if (!gl || !this.program || !source.width || !source.height) return false;
            const ratio = Math.min(2, root.devicePixelRatio || 1);
            this.canvas.width = Math.max(1, Math.round(size.width * ratio));
            this.canvas.height = Math.max(1, Math.round(size.height * ratio));
            gl.viewport(0, 0, this.canvas.width, this.canvas.height); gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT); gl.disable(gl.DEPTH_TEST); gl.disable(gl.CULL_FACE);
            gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true); gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
            this.uploadCanvas(this.sceneTexture, source);
            this.maskCanvas.width = source.width; this.maskCanvas.height = source.height;
            const mask = this.maskContext; mask.clearRect(0, 0, source.width, source.height);
            mask.fillStyle = "white";
            for (const mesh of meshes) for (let i = 0; i < mesh.indices.length; i += 3) {
                const points = mesh.indices.slice(i, i + 3).map(index => mesh.vertices[index]).filter(Boolean).map(point => screen(point, camera, size));
                if (points.length !== 3) continue;
                mask.beginPath(); mask.moveTo(points[0].x, points[0].y); mask.lineTo(points[1].x, points[1].y);
                mask.lineTo(points[2].x, points[2].y); mask.closePath(); mask.fill();
            }
            this.uploadCanvas(this.terrainTexture, this.maskCanvas);
            gl.useProgram(this.program);
            const textureNames = ["scene", "terrain", "normal", "foam", "caustics", "surfaceFoam"];
            const textureValues = [this.sceneTexture, this.terrainTexture, this.textures.normal, this.textures.foam, this.textures.caustics, this.textures.surfaceFoam];
            textureNames.forEach((name, index) => {
                gl.activeTexture(gl.TEXTURE0 + index); gl.bindTexture(gl.TEXTURE_2D, textureValues[index]);
                gl.uniform1i(this.uniform("u_" + name), index);
            });
            const height = size.height / camera.zoom, width = size.width / camera.zoom;
            const rect = [camera.x - width * 0.5, camera.y - height * 0.5, width, height];
            gl.uniform4fv(this.uniform("u_captureRect"), rect);
            gl.uniform2f(this.uniform("u_captureTexel"), 1 / source.width, 1 / source.height);
            const profile = this.style.surface, textures = this.style.textures;
            for (const body of bodies) {
                const color = parseColor(body.color, profile.waterColor);
                gl.uniform4fv(this.uniform("u_waterColor"), color);
                gl.uniform4fv(this.uniform("u_baseFarColor"), profile.baseFarColor);
                gl.uniform4fv(this.uniform("u_underwaterBottomColor"), this.style.underwater?.bottomColor || [0, 0.1927058697, 1, 1]);
                gl.uniform4f(this.uniform("u_effects"), body.refraction_strength, body.caustics_strength,
                    body.reflection_strength, body.foam_strength);
                gl.uniform4f(this.uniform("u_foamStyle"), profile.foamSharpness, profile.foamTextureOpacity,
                    profile.rippleFoamWidth || 0.86, profile.foamVoronoiOpacity);
                gl.uniform2fv(this.uniform("u_normalsTiling"), profile.normalsTiling);
                gl.uniform2fv(this.uniform("u_normalTextureScale"), profile.normalTextureScale || [1, -0.98]);
                gl.uniform2fv(this.uniform("u_waterSpeed"), profile.waterSpeed);
                gl.uniform2fv(this.uniform("u_foamTiling"), profile.foamTextureSize);
                gl.uniform2fv(this.uniform("u_foamScrollSpeed"), profile.foamTextureScrollSpeed);
                gl.uniform2fv(this.uniform("u_causticTiling"), profile.rippleFoamTextureTiling);
                gl.uniform1f(this.uniform("u_time"), time);
                gl.uniform1f(this.uniform("u_normalStrength"), profile.normalStrength);
                gl.uniform1f(this.uniform("u_reflectivity"), profile.reflectivity);
                gl.uniform1f(this.uniform("u_gradientHeight"), profile.baseGradientDistance);
                gl.uniform1f(this.uniform("u_gradientFade"), profile.baseGradientFade);
                gl.uniform1f(this.uniform("u_opacity"), body.opacity);
                gl.uniform2f(this.uniform("u_flow"), body.flow_x / Math.max(body.width, 0.01), body.flow_y / Math.max(body.depth, 0.01));
                if (textures.normal !== styleDefaults.textures.normal || textures.foam !== styleDefaults.textures.foam) {
                    for (const [key, file] of Object.entries(textures)) if (!this.loadedFiles || this.loadedFiles[key] !== file) this.loadTexture(key, file);
                    this.loadedFiles = { ...textures };
                }
                const xMin = Math.max(body.x, camera.x - width * 0.5), xMax = Math.min(body.x + body.width, camera.x + width * 0.5);
                if (xMax <= xMin || body.surface_y < camera.y - height * 0.5 - body.wave_amplitude) continue;
                const count = clamp(Math.ceil((xMax - xMin) * camera.zoom / 4), 48, 384);
                const data = [];
                const vertex = (x, y, u, v, surface, slope, amplitude) => {
                    const point = screen({ x, y }, camera, size);
                    data.push(point.x / size.width * 2 - 1, 1 - point.y / size.height * 2, x, y,
                        u, v, surface, clamp(slope * 0.35 + 0.5, 0, 1), clamp(amplitude, 0, 1), 0, 0);
                };
                for (let i = 0; i < count; i++) {
                    const u0 = (i / count) * (xMax - xMin) / body.width + (xMin - body.x) / body.width;
                    const u1 = ((i + 1) / count) * (xMax - xMin) / body.width + (xMin - body.x) / body.width;
                    const x0 = xMin + (xMax - xMin) * i / count, x1 = xMin + (xMax - xMin) * (i + 1) / count;
                    const dx = Math.max(0.001, body.width / count * 0.5);
                    const y0 = surfaceAt(body, x0, time, ripples), y1 = surfaceAt(body, x1, time, ripples);
                    const slope0 = (surfaceAt(body, x0 + dx, time, ripples) - surfaceAt(body, x0 - dx, time, ripples)) / (2 * dx);
                    const slope1 = (surfaceAt(body, x1 + dx, time, ripples) - surfaceAt(body, x1 - dx, time, ripples)) / (2 * dx);
                    const amp0 = Math.abs(y0 - body.surface_y) / Math.max(body.wave_amplitude, 0.001);
                    const amp1 = Math.abs(y1 - body.surface_y) / Math.max(body.wave_amplitude, 0.001);
                    const btm = body.surface_y - body.depth;
                    vertex(x0, y0, u0, 1, y0, slope0, amp0); vertex(x0, btm, u0, 0, y0, slope0, amp0);
                    vertex(x1, y1, u1, 1, y1, slope1, amp1); vertex(x1, y1, u1, 1, y1, slope1, amp1);
                    vertex(x0, btm, u0, 0, y0, slope0, amp0); vertex(x1, btm, u1, 0, y1, slope1, amp1);
                }
                const buffer = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
                gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(data), gl.STREAM_DRAW);
                const stride = 11 * 4;
                gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);
                gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 2 * 4);
                gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 2, gl.FLOAT, false, stride, 4 * 4);
                gl.enableVertexAttribArray(3); gl.vertexAttribPointer(3, 1, gl.FLOAT, false, stride, 6 * 4);
                gl.enableVertexAttribArray(4); gl.vertexAttribPointer(4, 4, gl.FLOAT, false, stride, 7 * 4);
                gl.drawArrays(gl.TRIANGLES, 0, data.length / 11); gl.deleteBuffer(buffer);
            }
            return true;
        }
        uploadCanvas(texture, canvas) {
            const gl = this.gl; gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, canvas);
        }
    }

    function parseColor(value, fallback) {
        const match = /^#?([0-9a-f]{6})$/i.exec(value || "");
        if (!match) return fallback;
        const n = parseInt(match[1], 16);
        return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255, 1];
    }

    class WaterCanvas {
        constructor(canvas) {
            this.canvas = canvas; this.context = canvas.getContext("2d");
            this.renderer = root.UltimateWaterRenderer ? new root.UltimateWaterRenderer() : new WebWaterRenderer(canvas);
            this.source = document.createElement("canvas"); this.sourceContext = this.source.getContext("2d");
            this.reflection = document.createElement("canvas"); this.reflectionContext = this.reflection.getContext("2d");
            this.ripples = []; this.probes = []; this.time = 0; this.lastTime = null;
            this.captureAvailable = false; this.meshes = []; this.level = null;
            this.solidKey = null; this.solidPath = null;
        }
        reset() { this.ripples = []; this.probes = []; this.lastTime = null; this.time = 0; this.level = null; }
        splash(body, x, strength = 1) {
            if (!body.ripples_enabled) return;
            this.ripples.push({ id: body.id, x: clamp(x, body.x, body.x + body.width), time: this.time,
                amplitude: -body.ripple_strength * clamp(strength, 0.1, 4), radius: 0.2 });
            if (this.ripples.length > 64) this.ripples.shift();
        }
        addProbe(body, kind) {
            if (this.probes.length >= 16) this.probes.shift();
            this.probes.push({ x: body.x + body.width / 2, y: body.surface_y + 0.6, vx: 0, vy: 0,
                size: 0.25, density: kind === "metal" ? 2.8 : 0.6, angle: 0.1, spin: 0.3, age: 0, wet: false, kind });
        }
        render(view, timestamp) {
            const ctx = this.context, width = this.canvas.clientWidth, height = this.canvas.clientHeight;
            const ratio = Math.min(2, root.devicePixelRatio || 1);
            if (this.canvas.width !== Math.round(width * ratio) || this.canvas.height !== Math.round(height * ratio)) {
                this.canvas.width = Math.max(1, Math.round(width * ratio)); this.canvas.height = Math.max(1, Math.round(height * ratio));
            }
            ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0, 0, width, height);
            const dt = this.lastTime === null ? 0 : Math.min(1 / 30, Math.max(0, (timestamp - this.lastTime) / 1000));
            this.lastTime = timestamp;
            if (!view.bound || !view.document || !width || !height) return;
            this.time += dt;
            if (this.level !== view.level) { this.level = view.level; this.meshes = terrainMeshes(view.level); this.solidKey = null; }
            const bodies = view.document.water_bodies || [], size = { width, height }, camera = view.camera;
            const project = point => screen(point, camera, size);
            this.ripples = this.ripples.filter(r => this.time - r.time < 8 && bodies.some(b => b.id === r.id));
            for (const probe of this.probes) {
                const oldY = probe.y;
                const contact = stepProbe(probe, bodies, dt);
                if (contact.water && !probe.wet) this.splash(contact.water, probe.x, Math.abs(probe.vy) + 0.5);
                probe.wet = contact.fraction > 0;
                if (probe.vy < 0 && inTerrain({ x: probe.x, y: probe.y - probe.size / 2 }, this.meshes)) {
                    probe.y = oldY; probe.vy = 0; probe.vx *= 0.95; probe.spin *= 0.9;
                }
                probe.age += dt;
            }
            this.probes = this.probes.filter(p => p.age < 30);
            if (view.sourceCanvas && bodies.length) {
                try {
                    if (this.source.width !== Math.ceil(width) || this.source.height !== Math.ceil(height)) {
                        this.source.width = Math.ceil(width); this.source.height = Math.ceil(height);
                        this.reflection.width = this.source.width; this.reflection.height = this.source.height;
                    }
                    this.sourceContext.clearRect(0, 0, width, height);
                    this.sourceContext.drawImage(view.sourceCanvas, 0, 0, width, height);
                    this.captureAvailable = true;
                } catch (_) { this.captureAvailable = false; }
            } else this.captureAvailable = false;
            const solidKey = [camera.x, camera.y, camera.zoom, width, height].join(",");
            if (this.solidKey !== solidKey) {
                const solid = new Path2D();
                solid.rect(0, 0, width, height);
                for (const mesh of this.meshes) for (let i = 0; i < mesh.indices.length; i += 3) {
                    const points = mesh.indices.slice(i, i + 3).map(index => mesh.vertices[index]).filter(Boolean).map(project);
                    if (points.length !== 3) continue;
                    solid.moveTo(points[0].x, points[0].y); solid.lineTo(points[1].x, points[1].y);
                    solid.lineTo(points[2].x, points[2].y); solid.closePath();
                }
                this.solidPath = solid; this.solidKey = solidKey;
            }
            if (this.renderer.draw(bodies, camera, size, this.source, this.meshes, this.time, this.ripples)) {
                ctx.drawImage(this.renderer.canvas, 0, 0, width, height);
                this.drawEditorOverlays(view, size, project);
                return;
            }
            const solid = this.solidPath;
            for (const body of bodies) {
                const tl = project({ x: body.x, y: body.surface_y }), br = project({ x: body.x + body.width, y: body.surface_y - body.depth });
                if (br.x < 0 || tl.x > width || br.y < 0 || tl.y > height + body.wave_amplitude * camera.zoom) continue;
                const left = Math.max(-20, tl.x), right = Math.min(width + 20, br.x), bottom = br.y;
                const wave = new Path2D();
                const count = Math.max(2, Math.ceil((right - left) / 5));
                const surface = [];
                for (let i = 0; i <= count; i++) {
                    const sx = left + (right - left) * i / count;
                    const wx = world({ x: sx, y: 0 }, camera, size).x;
                    const point = project({ x: wx, y: surfaceAt(body, wx, this.time, this.ripples) });
                    surface.push(point);
                    if (i === 0) wave.moveTo(point.x, point.y); else wave.lineTo(point.x, point.y);
                }
                wave.lineTo(right, bottom); wave.lineTo(left, bottom); wave.closePath();
                ctx.save(); ctx.clip(wave); ctx.clip(solid, "evenodd");
                if (this.captureAvailable && body.refraction_strength > 0) {
                    const top = Math.max(0, tl.y - body.wave_amplitude * camera.zoom), end = Math.min(height, bottom);
                    for (let y = top; y < end; y += 8) {
                        const shift = Math.sin(y * 0.04 + this.time * 1.1) * body.refraction_strength * camera.zoom;
                        ctx.drawImage(this.source, 0, y, width, Math.min(8, end - y), shift, y, width, Math.min(8, end - y));
                    }
                }
                const gradient = ctx.createLinearGradient(0, tl.y, 0, Math.max(tl.y + 1, bottom));
                gradient.addColorStop(0, body.color); gradient.addColorStop(1, "#123b58");
                ctx.globalAlpha = body.opacity * 0.8; ctx.fillStyle = gradient; ctx.fillRect(left, tl.y - 30, right - left, bottom - tl.y + 30);
                if (this.captureAvailable && body.reflection_strength > 0) {
                    const reflected = this.reflectionContext;
                    reflected.setTransform(1, 0, 0, 1, 0, 0); reflected.clearRect(0, 0, width, height);
                    reflected.save(); reflected.translate(0, 2 * tl.y); reflected.scale(1, -1);
                    reflected.drawImage(this.source, Math.sin(this.time) * body.refraction_strength * camera.zoom, 0); reflected.restore();
                    reflected.globalCompositeOperation = "destination-in";
                    const fade = reflected.createLinearGradient(0, tl.y, 0, tl.y + Math.max(20, 1.8 * camera.zoom));
                    fade.addColorStop(0, "white"); fade.addColorStop(1, "transparent");
                    reflected.fillStyle = fade; reflected.fillRect(0, 0, width, height);
                    reflected.globalCompositeOperation = "source-over";
                    ctx.globalAlpha = body.reflection_strength; ctx.drawImage(this.reflection, 0, 0);
                }
                ctx.globalAlpha = 1;
                if (body.caustics_strength > 0) {
                    ctx.globalAlpha = Math.min(0.5, body.caustics_strength * 0.18); ctx.strokeStyle = "#d4fff3"; ctx.lineWidth = 1.2;
                    const cell = Math.max(24, camera.zoom * 0.65), top = Math.max(0, tl.y);
                    for (let y = top; y < Math.min(height, bottom); y += cell) {
                        ctx.beginPath();
                        for (let x = left; x <= right; x += 5) {
                            const yy = y + Math.sin(x / cell * 4 + this.time * 0.7 + y / cell) * cell * 0.24;
                            if (x === left) ctx.moveTo(x, yy); else ctx.lineTo(x, yy);
                        }
                        ctx.stroke();
                    }
                }
                ctx.restore();
                ctx.save(); ctx.clip(solid, "evenodd");
                ctx.strokeStyle = "#d6f6fc"; ctx.globalAlpha = clamp(body.foam_strength, 0, 1);
                ctx.lineWidth = Math.max(1, body.foam_strength * 2.5); ctx.beginPath();
                surface.forEach((p, i) => i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y)); ctx.stroke(); ctx.restore();
                if (view.mode === "water" && body.id === view.selected) {
                    ctx.save(); ctx.strokeStyle = "#f5da7a"; ctx.lineWidth = 1.5; ctx.setLineDash([5, 4]);
                    ctx.strokeRect(tl.x, tl.y, br.x - tl.x, br.y - tl.y); ctx.setLineDash([]);
                    ctx.fillStyle = "#f5da7a";
                    for (const [x, y] of [[tl.x, tl.y], [br.x, tl.y], [tl.x, br.y], [br.x, br.y]]) ctx.fillRect(x - 4, y - 4, 8, 8);
                    ctx.font = '12px "Microsoft YaHei", system-ui'; ctx.fillText(body.name, tl.x + 8, tl.y - 8); ctx.restore();
                }
            }
            for (const probe of this.probes) {
                const p = project(probe); ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(-probe.angle);
                const side = Math.max(6, probe.size * camera.zoom);
                ctx.fillStyle = probe.kind === "metal" ? "#8598a3" : "#bd8d4f"; ctx.strokeStyle = "#2e2520";
                ctx.lineWidth = 1.5; ctx.fillRect(-side / 2, -side / 2, side, side); ctx.strokeRect(-side / 2, -side / 2, side, side);
                ctx.restore();
            }
        }
        drawEditorOverlays(view, size, project) {
            const ctx = this.context, camera = view.camera;
            for (const body of view.document.water_bodies || []) {
                if (view.mode !== "water" || body.id !== view.selected) continue;
                const tl = project({ x: body.x, y: body.surface_y }), br = project({ x: body.x + body.width, y: body.surface_y - body.depth });
                ctx.save(); ctx.strokeStyle = "#f5da7a"; ctx.lineWidth = 1.5; ctx.setLineDash([5, 4]);
                ctx.strokeRect(tl.x, tl.y, br.x - tl.x, br.y - tl.y); ctx.setLineDash([]); ctx.fillStyle = "#f5da7a";
                for (const [x, y] of [[tl.x, tl.y], [br.x, tl.y], [tl.x, br.y], [br.x, br.y]]) ctx.fillRect(x - 4, y - 4, 8, 8);
                ctx.font = '12px "Microsoft YaHei", system-ui'; ctx.fillText(body.name, tl.x + 8, tl.y - 8); ctx.restore();
            }
            for (const probe of this.probes) {
                const p = project(probe); ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(-probe.angle);
                const side = Math.max(6, probe.size * camera.zoom);
                ctx.fillStyle = probe.kind === "metal" ? "#8598a3" : "#bd8d4f"; ctx.strokeStyle = "#2e2520";
                ctx.lineWidth = 1.5; ctx.fillRect(-side / 2, -side / 2, side, side); ctx.strokeRect(-side / 2, -side / 2, side, side);
                ctx.restore();
            }
        }
    }
    const api = { defaults, migrate, screen, world, surfaceAt, terrainMeshes, contains, hitHandle, dragBounds, stepProbe, inTriangle, WaterCanvas };
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.bpsimWaterCanvas = api;
})(typeof window === "undefined" ? globalThis : window);

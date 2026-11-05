(function (root) {
    "use strict";
    class Writer {
        constructor() {
            this.buffer = new ArrayBuffer(4096);
            this.view = new DataView(this.buffer);
            this.offset = 0;
        }
        reserve(size) {
            if (this.offset + size <= this.buffer.byteLength) return;
            const next = new ArrayBuffer(Math.max(this.buffer.byteLength * 2, this.offset + size));
            new Uint8Array(next).set(new Uint8Array(this.buffer));
            this.buffer = next;
            this.view = new DataView(next);
        }
        byte(value) { this.reserve(1); this.view.setUint8(this.offset++, value); }
        i16(value) {
            if (!Number.isInteger(value) || value < -32768 || value > 32767) throw new Error("16-bit level value outside its range");
            this.reserve(2); this.view.setInt16(this.offset, value, true); this.offset += 2;
        }
        i32(value) {
            if (!Number.isInteger(value) || value < -2147483648 || value > 2147483647) throw new Error("32-bit level value outside its range");
            this.reserve(4); this.view.setInt32(this.offset, value, true); this.offset += 4;
        }
        u32(value) { this.reserve(4); this.view.setUint32(this.offset, value, true); this.offset += 4; }
        f32(value) {
            if (!Number.isFinite(value)) throw new Error("Non-finite level coordinate");
            this.reserve(4); this.view.setFloat32(this.offset, value, true); this.offset += 4;
        }
        bytes(values) {
            this.reserve(values.length);
            new Uint8Array(this.buffer, this.offset, values.length).set(values);
            this.offset += values.length;
        }
        string(value) {
            if (typeof value !== "string") throw new Error("Missing level object name");
            const bytes = new TextEncoder().encode(value);
            let length = bytes.length;
            while (length >= 128) { this.byte((length & 127) | 128); length >>>= 7; }
            this.byte(length); this.bytes(bytes);
        }
        vec2(v) { this.f32(v.x); this.f32(v.y); }
        vec3(v) { this.f32(v.x); this.f32(v.y); this.f32(v.z); }
        mesh(mesh) {
            this.i32(mesh.vertices.length);
            for (const point of mesh.vertices) this.vec2(point);
            this.i32(mesh.indices.length);
            for (const index of mesh.indices) this.i16(index);
        }
        finish() { return new Uint8Array(this.buffer.slice(0, this.offset)); }
    }
    function serialize(level) {
        if (!level || !Array.isArray(level.objects) || !Array.isArray(level.roots)) throw new Error("Unsupported editor level payload");
        const writer = new Writer();
        const stack = new Set();
        let objectCount = 0;
        function object(index, depth) {
            if (!Number.isInteger(index) || index < 0 || index >= level.objects.length || depth > 128 || stack.has(index) || ++objectCount > 100000)
                throw new Error("Invalid or cyclic level hierarchy");
            stack.add(index);
            const node = level.objects[index];
            if (node.Parent) {
                const parent = node.Parent;
                if (!parent.children.length) throw new Error("The legacy level format cannot encode an empty group");
                writer.i16(parent.children.length); writer.string(parent.name); writer.vec3(parent.position);
                for (const child of parent.children) object(child, depth + 1);
            } else if (node.Prefab) {
                const prefab = node.Prefab;
                writer.i16(0); writer.string(prefab.name); writer.i16(prefab.prefab_index);
                writer.vec3(prefab.position); writer.vec3(prefab.rotation); writer.vec3(prefab.scale);
                const types = { None: 0, Terrain: 1, PrefabOverrides: 2 };
                const type = types[prefab.data_type];
                if (type === undefined) throw new Error("Unknown level payload type");
                writer.byte(type);
                if (type === 1) {
                    const terrain = prefab.terrain_data;
                    if (!terrain) throw new Error("Terrain payload is missing");
                    writer.f32(terrain.fill_texture_tile_offset_x); writer.f32(terrain.fill_texture_tile_offset_y);
                    writer.mesh(terrain.fill_mesh);
                    const c = terrain.fill_color;
                    const channel = value => Math.round(value * 255) & 255;
                    writer.u32(((channel(c.r) << 24) | (channel(c.g) << 16) | (channel(c.b) << 8) | channel(c.a)) >>> 0);
                    writer.i32(terrain.fill_texture_index); writer.mesh(terrain.curve_mesh);
                    writer.i32(terrain.curve_textures.length);
                    for (const texture of terrain.curve_textures) {
                        writer.i32(texture.texture_index); writer.vec2(texture.size);
                        writer.byte(texture.fixed_angle ? 1 : 0); writer.f32(texture.fade_threshold);
                    }
                    writer.i32(terrain.control_texture_count);
                    if (terrain.control_texture_count > 0) {
                        if (!terrain.control_texture_data) throw new Error("Terrain control texture is missing");
                        writer.i32(terrain.control_texture_data.length); writer.bytes(terrain.control_texture_data);
                    }
                    writer.byte(terrain.has_collider ? 1 : 0);
                } else if (type === 2) {
                    if (!prefab.override_data || !prefab.override_data.raw_bytes) throw new Error("Original prefab overrides are missing");
                    writer.i32(prefab.override_data.raw_bytes.length); writer.bytes(prefab.override_data.raw_bytes);
                }
            } else throw new Error("Unknown object variant");
            stack.delete(index);
        }
        writer.i32(level.roots.length);
        for (const index of level.roots) object(index, 0);
        return writer.finish();
    }
    function toBase64(bytes) {
        let text = "";
        for (let offset = 0; offset < bytes.length; offset += 8192)
            text += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
        return btoa(text);
    }
    function fromBase64(text) {
        const binary = atob(text);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < bytes.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }
    class Reader {
        constructor(bytes) { this.bytes = bytes; this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength); this.offset = 0; }
        check(size) { if (size < 0 || this.offset + size > this.bytes.length) throw new Error("Truncated level data"); }
        byte() { this.check(1); return this.view.getUint8(this.offset++); }
        i16() { this.check(2); const value = this.view.getInt16(this.offset, true); this.offset += 2; return value; }
        i32() { this.check(4); const value = this.view.getInt32(this.offset, true); this.offset += 4; return value; }
        u32() { this.check(4); const value = this.view.getUint32(this.offset, true); this.offset += 4; return value; }
        f32() {
            this.check(4); const value = this.view.getFloat32(this.offset, true); this.offset += 4;
            if (!Number.isFinite(value)) throw new Error("Non-finite level coordinate");
            return value;
        }
        count(maximum) { const value = this.i32(); if (value < 0 || value > maximum) throw new Error("Invalid level array length"); return value; }
        raw(size) { this.check(size); const value = this.bytes.slice(this.offset, this.offset + size); this.offset += size; return value; }
        string() {
            let length = 0;
            for (let shift = 0; shift < 35; shift += 7) {
                const value = this.byte();
                if (shift === 28 && value > 7) throw new Error("Invalid string length");
                length |= (value & 127) << shift;
                if (value < 128) {
                    if (length < 0 || length > 65535) throw new Error("Level object name too long");
                    return new TextDecoder("utf-8", { fatal: true }).decode(this.raw(length));
                }
            }
            throw new Error("Invalid string length");
        }
        vec2() { return { x: this.f32(), y: this.f32() }; }
        vec3() { return { x: this.f32(), y: this.f32(), z: this.f32() }; }
        mesh() {
            const count = this.count(65535), vertices = [];
            for (let i = 0; i < count; i++) vertices.push(this.vec2());
            const indexCount = this.count(1000000), indices = [];
            for (let i = 0; i < indexCount; i++) indices.push(this.i16());
            return { vertices, indices };
        }
    }
    function parse(bytes) {
        if (bytes.length > 16 * 1024 * 1024) throw new Error("Level exceeds 16 MiB");
        const reader = new Reader(bytes), level = { objects: [], roots: [] };
        function object(parent, depth) {
            if (depth > 128 || level.objects.length >= 100000) throw new Error("Level hierarchy limit exceeded");
            const children = reader.i16(), name = reader.string(), index = level.objects.length;
            if (children < 0) throw new Error("Negative child count");
            if (children > 0) {
                const group = { name, position: reader.vec3(), children: [], parent };
                level.objects.push({ Parent: group });
                for (let i = 0; i < children; i++) group.children.push(object(index, depth + 1));
            } else {
                const prefab = {
                    name, prefab_index: reader.i16(), position: reader.vec3(), rotation: reader.vec3(), scale: reader.vec3(),
                    data_type: "None", terrain_data: null, override_data: null, parent,
                };
                const type = reader.byte();
                if (type === 1) {
                    prefab.data_type = "Terrain";
                    const x = reader.f32(), y = reader.f32(), fillMesh = reader.mesh(), packed = reader.u32();
                    const terrain = {
                        fill_texture_tile_offset_x: x, fill_texture_tile_offset_y: y, fill_mesh: fillMesh,
                        fill_color: { r: (packed >>> 24) / 255, g: ((packed >>> 16) & 255) / 255, b: ((packed >>> 8) & 255) / 255, a: (packed & 255) / 255 },
                        fill_texture_index: reader.i32(), curve_mesh: reader.mesh(), curve_textures: [],
                        control_texture_count: 0, control_texture_data: null, has_collider: false, fill_boundary: null,
                    };
                    const textures = reader.count(1024);
                    for (let i = 0; i < textures; i++)
                        terrain.curve_textures.push({ texture_index: reader.i32(), size: reader.vec2(), fixed_angle: !!reader.byte(), fade_threshold: reader.f32() });
                    terrain.control_texture_count = reader.count(65535);
                    if (terrain.control_texture_count > 0) terrain.control_texture_data = Array.from(reader.raw(reader.count(4 * 1024 * 1024)));
                    terrain.has_collider = !!reader.byte();
                    prefab.terrain_data = terrain;
                } else if (type === 2) {
                    prefab.data_type = "PrefabOverrides";
                    const raw = reader.raw(reader.count(4 * 1024 * 1024));
                    prefab.override_data = { raw_text: new TextDecoder("utf-8", { fatal: true }).decode(raw), raw_bytes: Array.from(raw) };
                } else if (type !== 0) throw new Error("Unknown level object payload");
                level.objects.push({ Prefab: prefab });
            }
            return index;
        }
        const roots = reader.count(100000);
        for (let i = 0; i < roots; i++) level.roots.push(object(null, 0));
        if (reader.offset !== bytes.length) throw new Error("Unexpected bytes after the level tree");
        return level;
    }
    const api = { serialize, parse, toBase64, fromBase64 };
    root.bpsimLevelBinary = api;
    if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);

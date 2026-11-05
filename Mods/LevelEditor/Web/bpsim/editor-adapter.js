(function () {
    "use strict";
    const tokenFromUrl = new URLSearchParams(location.hash.slice(1)).get("bpsim-token");
    const token = tokenFromUrl || sessionStorage.getItem("bpsim-level-editor-token");
    if (tokenFromUrl) sessionStorage.setItem("bpsim-level-editor-token", tokenFromUrl);
    const binary = window.bpsimLevelBinary, water = window.bpsimWaterCanvas;
    const state = {
        document: null, level: null, scene: null, binding: null, pendingImport: null, lastRevision: undefined,
        mode: "terrain", selected: null, dirty: false, busy: false, drawing: false,
        waterUndo: [], waterRedo: [], camera: { x: 0, y: 0, zoom: 40 }, cameraSource: "waiting",
        session: null, canvas: null, sourceCanvas: null, preview: null, drag: null, importTimer: null,
    };
    const clone = value => JSON.parse(JSON.stringify(value));
    const uuid = () => crypto.randomUUID();
    const bound = () => !!state.binding && state.scene?.document_key === state.binding;
    let host = window.bpEditorCanvas;

    function observe(scene) {
        if (!scene || !scene.level || typeof scene.file_name !== "string" || typeof scene.document_key !== "string" ||
            !Array.isArray(scene.level.objects) || !Array.isArray(scene.level.roots)) return;
        state.scene = scene;
        if (state.pendingImport && scene.file_name.toLowerCase() === state.pendingImport.file.toLowerCase() &&
            scene.document_key !== state.pendingImport.oldKey) {
            state.binding = scene.document_key; state.pendingImport = null;
            state.level = scene.level; state.lastRevision = scene.revision;
            status("已绑定主关卡画布。水域、地形和资源覆盖一起保存到关卡包。");
            requestAnimationFrame(() => host?.command("fit"));
        } else if (state.binding === scene.document_key) {
            state.level = scene.level;
            if (state.lastRevision !== undefined && state.lastRevision !== scene.revision) state.dirty = true;
            state.lastRevision = scene.revision;
        } else if (state.binding) {
            status("当前标签不是绑定关卡。请切回绑定标签，或在顶部载入另一关卡；已禁用水域操作与保存。", true);
        }
        mountCanvas(); ensureMounted(); updateButtons();
    }
    function wrapHost(value) {
        if (!value || typeof value.render !== "function" || value.__bpsimObserved) return value;
        const render = value.render;
        value.render = function (scene) { observe(scene); return render.call(this, scene); };
        const destroy = value.destroy;
        if (typeof destroy === "function") value.destroy = function () {
            state.scene = null; state.drag = null; updateButtons(); return destroy.call(this);
        };
        value.__bpsimObserved = true;
        return value;
    }
    Object.defineProperty(window, "bpEditorCanvas", {
        configurable: true, get() { return host; }, set(value) { host = wrapHost(value); },
    });
    wrapHost(host);
    function rendererEvent(message, source) {
        if (message?.type !== "camera" || ![message.x, message.y, message.zoom].every(Number.isFinite) || message.zoom <= 0) return;
        state.camera = { x: message.x, y: message.y, zoom: message.zoom };
        state.cameraSource = source;
    }
    // Main-thread rendering and Worker rendering have different event paths in the pinned editor.
    let originalRendererEvent = window.bpRendererEvent;
    const mainRendererEvent = function (message) {
        rendererEvent(message, "main");
        return typeof originalRendererEvent === "function" ? originalRendererEvent.call(this, message) : undefined;
    };
    Object.defineProperty(window, "bpRendererEvent", {
        configurable: true, get() { return mainRendererEvent; }, set(value) { originalRendererEvent = value; },
    });
    if (typeof window.Worker === "function") {
        window.Worker = new Proxy(window.Worker, {
            construct(target, args, newTarget) {
                const worker = Reflect.construct(target, args, newTarget);
                if (args[1]?.name === "badpiggies-renderer" || String(args[0]).includes("badpiggies-render-worker.js")) {
                    worker.addEventListener("message", event => {
                        if (event.data?.type === "renderer_event") rendererEvent(event.data.event, "worker");
                    });
                }
                return worker;
            },
        });
    }

    function status(message, error = false) {
        // The Mod owns no strip of its own: the pinned editor's own footer stays the only bar,
        // so diagnostics go to the console instead of adding another row of chrome.
        if (error) console.warn("[bpsim]", message); else console.debug("[bpsim]", message);
    }
    async function api(path, value) {
        const headers = { Authorization: "Bearer " + token }, options = { headers, cache: "no-store" };
        if (value !== undefined) {
            options.method = "POST"; headers["Content-Type"] = "application/json"; headers["X-Request-Id"] = uuid();
            options.body = JSON.stringify(value);
        }
        // Never retry a failed write: Unity may already have completed it.
        const response = await fetch("/api/" + path, options), result = await response.json();
        if (!response.ok || result.ok === false) throw new Error(result.error || "请求失败");
        return result;
    }
    async function action(callback) {
        if (state.busy) return;
        state.busy = true; updateButtons();
        try { await callback(); }
        catch (error) { status(String(error.message || error), true); }
        finally { state.busy = false; updateButtons(); }
    }
    function updateButtons() {
        // The page can receive scene events before its own controls exist.
        if (!document.getElementById("bpsim-doc-actions")) return;
        // Busy is global; everything else is decided per control so that loading a level
        // stays possible while no document is open yet.
        const set = (id, disabled) => {
            const element = document.getElementById(id);
            if (element) element.disabled = state.busy || !!disabled;
        };
        for (const id of ["bpsim-save", "bpsim-play", "bpsim-export-package"]) set(id, !bound() || !state.document);
        set("bpsim-load", !document.getElementById("bpsim-level")?.value);
        set("bpsim-import-package", false);
        set("bpsim-return", false);
        set("bpsim-parallax", !state.document);
        for (const id of ["bpsim-water-add", "bpsim-water-draw", "bpsim-water-fit"]) set(id, !bound());
        set("bpsim-water-undo", !bound() || !state.waterUndo.length);
        set("bpsim-water-redo", !bound() || !state.waterRedo.length);
        set("bpsim-preview-clear", !bound() || !state.preview);
        for (const id of ["bpsim-water-delete", "bpsim-water-splash", "bpsim-probe-wood", "bpsim-probe-metal"])
            set(id, !bound() || !selectedWater());
        document.querySelectorAll("[data-water-field]").forEach(input => input.disabled = state.busy || !bound() || !selectedWater());
        const parallax = document.getElementById("bpsim-parallax");
        if (parallax) {
            const enabled = !!state.document?.parallax_enabled;
            parallax.classList.toggle("is-selected", enabled);
            parallax.setAttribute("aria-pressed", String(enabled));
            parallax.title = enabled ? "游戏视差：开" : "游戏视差：关";
        }
        const tool = document.getElementById("bpsim-water-tool");
        if (tool) {
            tool.disabled = state.busy;
            tool.classList.toggle("active", state.mode === "water");
            tool.title = state.mode === "water" ? "水域工具（点击返回地形）" : "水域工具";
        }
        if (state.canvas) state.canvas.style.pointerEvents = state.mode === "water" && bound() && !state.busy ? "auto" : "none";
    }
    function expectedFile() { return state.document.base_scene.replace(/_data$/i, "") + "_data.bytes"; }
    function importIntoEditor() {
        const shell = document.querySelector("#main .app-shell");
        if (!shell) return false;
        const transfer = new DataTransfer();
        const file = new File([binary.fromBase64(state.document.level_bytes)], expectedFile(), { type: "application/octet-stream" });
        transfer.items.add(file);
        state.pendingImport = { file: file.name, oldKey: state.scene?.document_key, time: Date.now() }; state.binding = null;
        shell.dispatchEvent(new DragEvent("dragover", { bubbles: true, cancelable: true, dataTransfer: transfer }));
        shell.dispatchEvent(new DragEvent("drop", { bubbles: true, cancelable: true, dataTransfer: transfer }));
        return true;
    }
    async function setDocument(value) {
        state.document = clone(value);
        state.document.water_schema_version = 2;
        state.document.parallax_enabled ??= true;
        state.document.water_bodies = (value.water_bodies || []).map(water.migrate);
        state.level = binary.parse(binary.fromBase64(value.level_bytes));
        state.selected = state.document.water_bodies[0]?.id || null;
        state.waterUndo = []; state.waterRedo = []; state.dirty = false; state.lastRevision = undefined;
        state.preview?.reset(); state.drag = null; state.drawing = false;
        const title = document.getElementById("bpsim-title");
        if (title) title.value = value.title;
        renderProperties();
        if (state.importTimer) clearInterval(state.importTimer);
        state.binding = null;
        let attempts = 0;
        state.importTimer = setInterval(() => {
            if (importIntoEditor() || ++attempts > 200) { clearInterval(state.importTimer); state.importTimer = null; }
        }, 250);
    }
    function documentForSave() {
        if (state.pendingImport || !bound()) throw new Error("主画布尚未绑定当前关卡，或已切换其他标签；不能保存错误的地形。");
        const invalid = document.querySelector("[data-water-field][data-invalid='true']");
        if (invalid) throw new Error("请先修正水域参数：" + invalid.getAttribute("aria-label"));
        const output = clone(state.document);
        output.title = document.getElementById("bpsim-title").value.trim();
        if (!output.title) throw new Error("请填写关卡名称。");
        output.level_bytes = binary.toBase64(binary.serialize(state.level));
        return output;
    }
    async function save(play) {
        const output = documentForSave(), revision = state.lastRevision;
        const result = await api(play ? "play" : "save", output);
        state.document = clone(result.document || output);
        state.dirty = state.lastRevision !== revision || document.getElementById("bpsim-title").value.trim() !== output.title;
        status(play ? "已保存并导入游戏。水力使用关卡参数；切回游戏开始模拟即可测试。" : "完整关卡包已保存：" + result.file);
        if (play && !window.vuplex) status("已保存并导入游戏，请切回 bpsim 开始模拟。");
        state.session = await api("session"); populateCatalog(); renderProperties();
    }
    function selectedWater() { return state.document?.water_bodies.find(body => body.id === state.selected); }
    function requireBound() { if (!bound()) throw new Error("请先载入并绑定主关卡画布。"); }
    function waterSnapshot() {
        state.waterUndo.push(clone(state.document.water_bodies));
        if (state.waterUndo.length > 100) state.waterUndo.shift();
        state.waterRedo = []; state.dirty = true;
    }
    function changeWater(key, value) {
        const body = selectedWater();
        if (!body || !bound() || body[key] === value) return;
        waterSnapshot(); body[key] = value;
        if (key === "name") renderList();
        updateButtons();
    }
    function newWater(x, y, width = 4, depth = 2) {
        return {
            ...water.defaults, id: uuid(), name: "水域 " + (state.document.water_bodies.length + 1),
            x, surface_y: y, width, depth, color: "#00a9fd", opacity: 1,
            wave_amplitude: 0.03, wave_speed: 1, physics_enabled: true,
        };
    }
    function addWater() {
        requireBound();
        if (state.document.water_bodies.length >= 64) throw new Error("最多支持 64 片水域。");
        waterSnapshot();
        const body = newWater(state.camera.x - 2, state.camera.y);
        state.document.water_bodies.push(body); state.selected = body.id;
        switchMode("water"); renderProperties();
    }
    function deleteWater() {
        requireBound(); if (!selectedWater()) return;
        waterSnapshot(); state.document.water_bodies = state.document.water_bodies.filter(body => body.id !== state.selected);
        state.selected = state.document.water_bodies[0]?.id || null; renderProperties();
    }
    function undoWater(redo = false) {
        if (!bound()) return;
        const source = redo ? state.waterRedo : state.waterUndo, destination = redo ? state.waterUndo : state.waterRedo;
        if (!source.length) return;
        destination.push(clone(state.document.water_bodies)); state.document.water_bodies = source.pop();
        state.selected = selectedWater()?.id || state.document.water_bodies[0]?.id || null;
        state.dirty = true; renderProperties();
    }
    function renderList() {
        const list = document.getElementById("bpsim-water-list"); if (!list) return;
        list.replaceChildren();
        for (const body of state.document?.water_bodies || []) list.add(new Option(body.name || body.id, body.id));
        list.value = state.selected || "";
    }
    function renderProperties() {
        if (!state.document) return;
        renderList(); const selected = selectedWater();
        document.querySelectorAll("[data-water-field]").forEach(input => {
            const value = selected ? selected[input.dataset.waterField] : "";
            if (input.type === "checkbox") input.checked = !!value;
            else input.value = input.type === "color" && typeof value === "string" ? value.slice(0, 7) : value ?? "";
            input.dataset.invalid = "false"; input.setCustomValidity("");
        });
        document.getElementById("bpsim-water-draw").classList.toggle("is-selected", state.drawing);
        updateButtons();
    }
    function populateCatalog() {
        const select = document.getElementById("bpsim-level");
        if (!select) return;
        const previous = select.value;
        select.replaceChildren(new Option("选择关卡…", ""));
        const group = (label, entries, value, prefix) => {
            if (!entries || !entries.length) return;
            const optgroup = document.createElement("optgroup");
            optgroup.label = label;
            for (const entry of entries) optgroup.append(new Option(entry.title, prefix + entry[value]));
            select.append(optgroup);
        };
        group("内置关卡", state.session?.built_in, "scene", "scene:");
        group("我的关卡", state.session?.saved, "id", "id:");
        select.value = [...select.options].some(option => option.value === previous) ? previous : "";
        if (!select.value) select.selectedIndex = 0;
    }
    async function loadSelected() {
        const select = document.getElementById("bpsim-level");
        const value = select?.value || "";
        if (!value) throw new Error("请先在工具栏选择要载入的关卡。");
        if (state.dirty && !confirm("当前修改尚未保存，确定载入另一关卡？")) return;
        const request = value.startsWith("scene:") ? { scene: value.slice(6) } : { id: value.slice(3) };
        await setDocument((await api("open", request)).document);
    }
    function switchMode(mode) {
        state.mode = mode; state.drag = null;
        document.body.classList.toggle("bpsim-water-mode", mode === "water");
        positionPanel(); updateButtons();
        status(mode === "water" ? "同一关卡画布：拖动水域移动，四角缩放；中键/Alt 平移，滚轮缩放，双击水面试涟漪。" :
            "地形工具已恢复。水面效果仍在主画布实时显示，水域数据随关卡包保存。");
    }
    function eventWorld(event) {
        const rect = state.canvas.getBoundingClientRect();
        return water.world({ x: event.clientX - rect.left, y: event.clientY - rect.top }, state.camera, { width: rect.width, height: rect.height });
    }
    function forwardPointer(event) {
        if (!state.sourceCanvas) return;
        state.sourceCanvas.dispatchEvent(new PointerEvent(event.type, {
            bubbles: true, cancelable: true, pointerId: event.pointerId, pointerType: event.pointerType,
            clientX: event.clientX, clientY: event.clientY, button: event.button, buttons: event.buttons,
            altKey: event.altKey, ctrlKey: event.ctrlKey, shiftKey: event.shiftKey, metaKey: event.metaKey,
            detail: event.detail, isPrimary: event.isPrimary,
        }));
    }
    function installCanvasInput(canvas) {
        // The renderer only navigates while its pointer is inside the canvas.
        // Our overlay must mirror hover enter/leave as well as wheel/pan events.
        canvas.addEventListener("pointerenter", event => forwardPointer(event));
        canvas.addEventListener("pointerleave", event => { if (!state.drag) forwardPointer(event); });
        canvas.addEventListener("pointerdown", event => {
            if (!bound() || state.busy) return;
            event.preventDefault(); event.stopImmediatePropagation();
            const point = eventWorld(event);
            if (event.button === 1 || event.button === 2 || event.altKey) {
                state.drag = { pan: true }; forwardPointer(event);
            } else {
                const selected = selectedWater();
                const handle = selected ? water.hitHandle(selected, point, state.camera.zoom) : null;
                const hit = handle ? selected : [...state.document.water_bodies].reverse().find(body => water.contains(body, point));
                if (state.drawing) state.drag = { create: true, start: point, clientX: event.clientX, clientY: event.clientY };
                else {
                    state.selected = hit?.id || null;
                    state.drag = hit ? { body: hit.id, start: point, original: clone(hit), handle, snapshot: false, clientX: event.clientX, clientY: event.clientY } : null;
                }
                renderProperties();
            }
            canvas.setPointerCapture(event.pointerId);
        });
        canvas.addEventListener("pointermove", event => {
            if (!bound()) return;
            const drag = state.drag;
            event.stopImmediatePropagation();
            if (!drag) {
                forwardPointer(event);
                const body = selectedWater(), handle = body ? water.hitHandle(body, eventWorld(event), state.camera.zoom) : null;
                canvas.style.cursor = state.drawing ? "crosshair" : handle ? (["nw", "se"].includes(handle) ? "nwse-resize" : "nesw-resize") : "default";
                return;
            }
            if (drag.pan) { forwardPointer(event); return; }
            if (!drag.snapshot && Math.hypot(event.clientX - drag.clientX, event.clientY - drag.clientY) < 3) return;
            const point = eventWorld(event);
            if (drag.create && !drag.body) {
                if (state.document.water_bodies.length >= 64) { state.drag = null; status("最多支持 64 片水域。", true); return; }
                waterSnapshot(); drag.snapshot = true;
                const body = newWater(drag.start.x, drag.start.y, 0.01, 0.01);
                state.document.water_bodies.push(body); drag.body = body.id; state.selected = body.id;
            }
            const body = selectedWater(); if (!body || body.id !== drag.body) return;
            if (!drag.snapshot) { waterSnapshot(); drag.snapshot = true; }
            if (drag.create) {
                body.x = Math.min(point.x, drag.start.x); body.surface_y = Math.max(point.y, drag.start.y);
                body.width = Math.min(10000, Math.max(0.01, Math.abs(point.x - drag.start.x)));
                body.depth = Math.min(10000, Math.max(0.01, Math.abs(point.y - drag.start.y)));
            } else Object.assign(body, water.dragBounds(drag.original, drag.start, point, drag.handle));
            renderProperties();
        });
        function release(event) {
            if (state.drag?.pan) forwardPointer(event);
            state.drag = null;
            if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
            event.stopImmediatePropagation();
        }
        canvas.addEventListener("pointerup", release); canvas.addEventListener("pointercancel", release);
        canvas.addEventListener("lostpointercapture", () => { state.drag = null; });
        canvas.addEventListener("contextmenu", event => event.preventDefault());
        canvas.addEventListener("wheel", event => {
            event.preventDefault(); event.stopImmediatePropagation();
            state.sourceCanvas?.dispatchEvent(new WheelEvent("wheel", {
                bubbles: true, cancelable: true, clientX: event.clientX, clientY: event.clientY,
                deltaX: event.deltaX, deltaY: event.deltaY, deltaMode: event.deltaMode,
                altKey: event.altKey, ctrlKey: event.ctrlKey, shiftKey: event.shiftKey, metaKey: event.metaKey,
            }));
        }, { passive: false });
        canvas.addEventListener("dblclick", event => {
            const point = eventWorld(event), body = [...state.document.water_bodies].reverse().find(b => water.contains(b, point) || Math.abs(point.y - b.surface_y) * state.camera.zoom < 16 && point.x >= b.x && point.x <= b.x + b.width);
            if (body) state.preview.splash(body, point.x, 2);
            event.stopImmediatePropagation(); event.preventDefault();
        });
    }
    function positionPanel() {
        if (!state.sourceCanvas || !state.canvas) return;
        const canvas = state.sourceCanvas, rect = canvas.getBoundingClientRect();
        state.canvas.style.left = canvas.offsetLeft + "px"; state.canvas.style.top = canvas.offsetTop + "px";
        state.canvas.style.width = canvas.clientWidth + "px"; state.canvas.style.height = canvas.clientHeight + "px";
        const panel = document.getElementById("bpsim-water-properties"), inspector = document.querySelector("#main .rton-side-panel-right");
        if (!panel) return;
        const side = inspector?.getBoundingClientRect();
        const docked = side && side.width > 180 && side.left >= 0 && side.right <= innerWidth + 1 && innerWidth > 900;
        panel.style.left = (docked ? side.left : Math.max(8, rect.right - Math.min(300, rect.width - 16))) + "px";
        panel.style.top = (docked ? side.top : rect.top + 8) + "px";
        panel.style.width = (docked ? side.width : Math.min(300, rect.width - 16)) + "px";
        panel.style.height = Math.max(80, (docked ? side.height : rect.height - 16)) + "px";
    }
    function mountCanvas() {
        const source = document.getElementById("editor-canvas");
        if (!source || source === state.sourceCanvas || !source.parentElement) return;
        state.canvas?.remove(); state.sourceCanvas = source;
        const canvas = document.createElement("canvas"); canvas.id = "bpsim-water-canvas";
        canvas.setAttribute("aria-label", "主关卡画布水域工具：拖动移动，四角缩放，双击涟漪");
        if (getComputedStyle(source.parentElement).position === "static") source.parentElement.style.position = "relative";
        source.parentElement.append(canvas); state.canvas = canvas; state.preview = new water.WaterCanvas(canvas);
        installCanvasInput(canvas); new ResizeObserver(positionPanel).observe(source); positionPanel(); updateButtons();
    }

    const fields = [
        ["name", "名称", "text", null, null, null],
        ["x", "X 坐标", "number", -100000, 100000, 0.1], ["surface_y", "水位 Y", "number", -100000, 100000, 0.1],
        ["width", "宽度", "number", 0.01, 10000, 0.1], ["depth", "向下深度", "number", 0.01, 10000, 0.1],
        ["color", "颜色", "color"], ["opacity", "透明度", "number", 0, 1, 0.05],
        ["wave_amplitude", "波浪振幅", "number", 0, 10, 0.01], ["wave_length", "波长", "number", 0.05, 1000, 0.1],
        ["wave_speed", "波浪速度", "number", 0, 100, 0.1],
        ["physics_enabled", "启用水力", "checkbox"], ["density", "水密度", "number", 0, 1000, 0.1],
        ["buoyancy_multiplier", "浮力倍率", "number", 0, 100, 0.1],
        ["linear_drag", "线性阻力", "number", 0, 100, 0.1], ["angular_drag", "旋转阻力", "number", 0, 100, 0.1],
        ["flow_x", "水流 X /秒", "number", -100, 100, 0.1], ["flow_y", "水流 Y /秒", "number", -100, 100, 0.1],
        ["flow_acceleration", "水流加速度", "number", 0, 100, 0.1],
        ["ripples_enabled", "接触涟漪", "checkbox"], ["ripple_strength", "涟漪强度", "number", 0, 2, 0.01],
        ["ripple_speed", "涟漪传播", "number", 0.05, 40, 0.1], ["ripple_damping", "涟漪衰减", "number", 0.01, 20, 0.1],
        ["refraction_strength", "折射扰动", "number", 0, 1, 0.01], ["caustics_strength", "焦散亮度", "number", 0, 2, 0.05],
        ["reflection_strength", "平面反射", "number", 0, 1, 0.05], ["foam_strength", "水线泡沫", "number", 0, 2, 0.05],
    ];
    function fieldMarkup([name, label, type, min, max, step]) {
        return `<label>${label}<input data-water-field="${name}" aria-label="${label}" type="${type}"` +
            (min !== null && min !== undefined ? ` min="${min}" max="${max}" step="${step}"` : "") + (name === "name" ? ' maxlength="80"' : "") + "></label>";
    }
    // Stroke icons sized by editor.css so they line up with the pinned editor's own toolbar icons.
    const icons = {
        save: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 3.5h11l3 3V20.5H5z"/><path d="M8 3.5v5.5h7V3.5"/><path d="M8 13.5h8v7"/></svg>',
        play: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7.5 5 19 12 7.5 19z"/></svg>',
        import: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 15.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-3.5"/><path d="M16.5 8 12 3.5 7.5 8"/><path d="M12 3.5V15"/></svg>',
        export: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 15.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-3.5"/><path d="M7.5 10.5 12 15l4.5-4.5"/><path d="M12 15V3.5"/></svg>',
        parallax: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.5 2.5 7 12 11.5 21.5 7z"/><path d="m2.5 12 9.5 4.5L21.5 12"/><path d="m2.5 17 9.5 4.5L21.5 17"/></svg>',
        exit: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9.5 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4.5"/><path d="M16 16.5 21 12l-5-4.5"/><path d="M21 12H9.5"/></svg>',
        water: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.8 6.9 8a7.2 7.2 0 1 0 10.2 0z"/><path d="M8.9 13.6a3.2 3.2 0 0 0 3.1 3.2"/></svg>',
    };
    function iconButton(id, icon, title) {
        return `<button id="${id}" class="bpsim-icon" type="button" title="${title}" aria-label="${title}" data-icon="${icon}">${icons[icon]}</button>`;
    }
    // The Mod owns no bar of its own: every level action is injected into the pinned editor's command bar.
    function mountActions() {
        const actions = document.getElementById("bpsim-doc-actions");
        const scroll = document.querySelector("#main .rton-commandbar-group-scroll");
        if (!actions || !scroll) return;
        if (actions.parentElement !== scroll) scroll.append(actions);
        if (actions.hidden) actions.hidden = false;
    }
    // Water editing is a canvas tool, so its entry point lives in the drawing tool row.
    function mountWaterTool() {
        const tool = document.getElementById("bpsim-water-tool");
        const row = document.querySelector("#main .canvas-tool-panel .tool-button-row");
        if (!tool || !row) return;
        if (tool.parentElement !== row) row.append(tool);
        if (tool.hidden) tool.hidden = false;
    }
    function ensureMounted() { mountActions(); mountWaterTool(); mountCanvas(); }
    function buildUi() {
        const actions = document.createElement("div");
        actions.id = "bpsim-doc-actions"; actions.className = "rton-toolbar-group-shell bpsim-doc-actions"; actions.hidden = true;
        actions.innerHTML = `<div class="rton-toolbar-group">
            <input id="bpsim-title" class="bpsim-title" type="text" aria-label="关卡名称" placeholder="关卡名称" maxlength="160">
            <select id="bpsim-level" class="bpsim-select" aria-label="选择关卡"></select>
            <button id="bpsim-load" class="bpsim-text" type="button" title="载入所选关卡">载入</button>
            ${iconButton("bpsim-save", "save", "保存关卡包 (Ctrl+S)")}
            ${iconButton("bpsim-play", "play", "保存并试玩")}
            ${iconButton("bpsim-import-package", "import", "导入关卡包")}
            ${iconButton("bpsim-export-package", "export", "导出关卡包")}
            ${iconButton("bpsim-parallax", "parallax", "游戏视差：开")}
            ${iconButton("bpsim-return", "exit", "返回游戏")}
            <input id="bpsim-package-file" type="file" accept=".bpslevel" hidden>
        </div>`;
        document.body.append(actions);
        const tool = document.createElement("button");
        tool.id = "bpsim-water-tool"; tool.type = "button"; tool.hidden = true;
        tool.className = "canvas-tool-button bpsim-water-tool";
        tool.title = "水域工具"; tool.setAttribute("aria-label", "水域工具");
        tool.innerHTML = icons.water;
        document.body.append(tool);
        const panel = document.createElement("aside"); panel.id = "bpsim-water-properties"; panel.setAttribute("aria-label", "水域属性");
        panel.innerHTML = `
            <h2>水域 · 主画布</h2><select id="bpsim-water-list" aria-label="选择水域"></select>
            <div id="bpsim-water-actions"><button id="bpsim-water-add" type="button">新增水域</button>
                <button id="bpsim-water-draw" type="button">画矩形</button><button id="bpsim-water-delete" type="button">删除</button>
                <button id="bpsim-water-undo" type="button">撤销水域</button><button id="bpsim-water-redo" type="button">重做</button>
                <button id="bpsim-water-fit" type="button">适应关卡</button></div>
            <details open><summary>范围与波浪</summary>${fields.slice(0, 10).map(fieldMarkup).join("")}</details>
            <details open><summary>浮力 · 水阻力 · 水流</summary>${fields.slice(10, 18).map(fieldMarkup).join("")}
                <p>坐标 / 水流使用 BPLE 单位（游戏 ×8）。水密度按游戏单位的二维排水量计算；阻力单位为 /秒。</p></details>
            <details><summary>涟漪 · 折射 · 焦散 · 反射</summary>${fields.slice(18).map(fieldMarkup).join("")}</details>
            <div id="bpsim-preview-actions"><button id="bpsim-water-splash" type="button">试涟漪</button>
                <button id="bpsim-probe-wood" type="button">预览木浮块</button><button id="bpsim-probe-metal" type="button">预览金属块</button>
                <button id="bpsim-preview-clear" type="button">清除预览</button></div>
            <p>这里的浮块为临时物理示意，不写入存档。真实部件 / 整车用“保存并试玩”在游戏验证。</p>
            <p>水面直接叠加原关卡画布，共用镜头与地形。水域撤销记录独立于上游地形撤销；裸 .bytes 不包含水域。</p>`;
        document.body.append(panel);
        document.body.classList.add("bpsim-connected");
        const click = (id, fn) => document.getElementById(id).addEventListener("click", () => action(fn));
        click("bpsim-water-tool", () => switchMode(state.mode === "water" ? "terrain" : "water"));
        click("bpsim-save", () => save(false)); click("bpsim-play", () => save(true));
        click("bpsim-load", loadSelected);
        click("bpsim-import-package", () => document.getElementById("bpsim-package-file").click());
        click("bpsim-export-package", () => {
            const output = documentForSave(), url = URL.createObjectURL(new Blob([JSON.stringify(output, null, 2)], { type: "application/json" }));
            const link = document.createElement("a"); link.href = url; link.download = output.id + ".bpslevel"; link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        });
        click("bpsim-return", () => api("close", {}));
        document.getElementById("bpsim-package-file").addEventListener("change", event => action(async () => {
            const file = event.target.files[0]; if (!file) return;
            if (state.dirty && !confirm("当前修改尚未保存，确定导入关卡包？")) return;
            if (file.size > 32 * 1024 * 1024) throw new Error("关卡包超过 32 MiB。");
            await setDocument((await api("import", JSON.parse(await file.text()))).document); event.target.value = "";
        }));
        click("bpsim-water-add", addWater); click("bpsim-water-delete", deleteWater);
        click("bpsim-water-draw", () => { requireBound(); state.drawing = !state.drawing; renderProperties(); });
        click("bpsim-water-undo", () => undoWater(false)); click("bpsim-water-redo", () => undoWater(true));
        click("bpsim-water-fit", () => host?.command("fit"));
        click("bpsim-water-splash", () => { const body = selectedWater(); if (body) state.preview?.splash(body, body.x + body.width / 2, 3); });
        click("bpsim-probe-wood", () => { if (selectedWater()) state.preview?.addProbe(selectedWater(), "wood"); });
        click("bpsim-probe-metal", () => { if (selectedWater()) state.preview?.addProbe(selectedWater(), "metal"); });
        click("bpsim-preview-clear", () => state.preview?.reset());
        document.getElementById("bpsim-water-list").addEventListener("change", event => { state.selected = event.target.value; renderProperties(); });
        document.querySelectorAll("[data-water-field]").forEach(input => {
            const update = () => {
                const value = input.type === "number" ? input.valueAsNumber : input.type === "checkbox" ? input.checked : input.value;
                const invalid = input.type === "number" && (!Number.isFinite(value) || value < Number(input.min) || value > Number(input.max));
                input.dataset.invalid = String(invalid);
                input.setCustomValidity(invalid ? "参数超出范围" : "");
                if (invalid) { status("参数无效：" + input.getAttribute("aria-label"), true); return; }
                changeWater(input.dataset.waterField, value);
            };
            input.addEventListener("input", update); input.addEventListener("change", () => { update(); if (input.dataset.invalid !== "true") renderProperties(); });
        });
        document.getElementById("bpsim-title").addEventListener("input", () => state.dirty = true);
        document.getElementById("bpsim-level").addEventListener("change", () => updateButtons());
        click("bpsim-parallax", () => {
            if (!state.document) throw new Error("尚未载入关卡。");
            state.document.parallax_enabled = !state.document.parallax_enabled;
            state.dirty = true;
        });
        new MutationObserver(ensureMounted).observe(document.getElementById("main") || document.body, { childList: true, subtree: true });
        ensureMounted();
        window.addEventListener("resize", positionPanel);
        window.addEventListener("keydown", event => {
            const key = event.key.toLowerCase(), command = event.ctrlKey || event.metaKey;
            if (command && key === "s") { event.preventDefault(); event.stopImmediatePropagation(); action(() => save(false)); return; }
            if (state.mode !== "water" || !bound() || ["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName) || event.target.isContentEditable) return;
            if (state.busy) { event.preventDefault(); event.stopImmediatePropagation(); return; }
            if (command && (key === "z" || key === "y")) {
                event.preventDefault(); event.stopImmediatePropagation(); undoWater(key === "y" || event.shiftKey);
            } else if (key === "delete" || key === "backspace") {
                event.preventDefault(); event.stopImmediatePropagation(); action(deleteWater);
            } else if (key === "escape") {
                event.preventDefault(); event.stopImmediatePropagation(); state.drag = null; state.drawing = false; state.selected = null; renderProperties();
            } else if (key === "f") {
                event.preventDefault(); event.stopImmediatePropagation(); host?.command("fit");
            }
        }, true);
        window.addEventListener("beforeunload", event => { if (state.dirty) { event.preventDefault(); event.returnValue = ""; } });
        let lastFrame = 0;
        function frame(timestamp) {
            if (timestamp - lastFrame >= 30) {
                lastFrame = timestamp;
                state.preview?.render({ ...state, bound: bound() }, timestamp);
            }
            requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    }
    async function start() {
        if (!binary || !water) return;
        buildUi();
        if (!token) { status("此页面只能通过已启用的关卡编辑器 Mod 打开。", true); updateButtons(); return; }
        try {
            state.session = await api("session"); populateCatalog();
            if (state.session?.document) {
                await setDocument(state.session.document);
                status("正在载入主关卡画布；水域工具不会另开页面。");
            } else {
                renderProperties();
                status("尚未载入关卡：在工具栏选择关卡后点“载入”。");
            }
            window.vuplex?.postMessage(JSON.stringify({ type: "bpsim.capabilities", webgpu: !!navigator.gpu, wasm: typeof WebAssembly !== "undefined" }));
            updateButtons();
        } catch (error) { status("Mod 服务不可用：" + error.message, true); updateButtons(); }
        setInterval(() => {
            if (state.pendingImport && Date.now() - state.pendingImport.time > 60000) {
                state.pendingImport = null;
                status("主画布尚未载入：内置 WebView 不支持此渲染后端时，请关闭关卡编辑器后重新打开。", true);
                updateButtons();
            }
            positionPanel();
        }, 1000);
    }
    // Diagnostics never include the session token or privileged host methods.
    window.bpsimEditorDiagnostics = () => ({
        version: 2, bound: bound(), waterCount: state.document?.water_bodies.length || 0,
        mode: state.mode, dirty: state.dirty, upstreamKey: state.scene?.document_key || null,
        camera: { ...state.camera }, cameraSource: state.cameraSource, sameCanvasParent: !!state.canvas && state.canvas.parentElement === state.sourceCanvas?.parentElement,
        sceneCapture: state.preview?.captureAvailable || false, previewProbes: state.preview?.probes.length || 0,
        previewRipples: state.preview?.ripples.length || 0,
        waters: state.document?.water_bodies.map(b => ({ id: b.id, width: b.width, physics_enabled: b.physics_enabled, flow_x: b.flow_x })) || [],
    });
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true }); else start();
})();

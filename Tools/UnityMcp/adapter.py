"""Translation layer: legacy DshBridge methods -> MCP for Unity (Coplay) tools.

The former Unity-side plugin (`Assets/Editor/DshBridge/DshBridge.cs`) exposed 30
commands over a loopback HTTP socket. This module keeps those 30 method names,
parameter names and result shapes, but performs the work through the MCP server
that the Coplay `com.coplaydev.unity-mcp` package runs for this editor.

Dispatch policy per method (deliberate, see README "方法映射"):

* typed MCP tool  - the Coplay tool already has the exact semantics.
* C# via execute_code - Coplay's typed tool diverges in a way that would break
  the legacy contract (world-space transforms, dotted/private member access,
  static invocation, profiler history, untitled scenes, nested hierarchy).
* host filesystem - the original command never needed Unity (read_file,
  list_dir, write_script + refresh).

Every execute_code snippet runs with `safety_checks: false`: the legacy surface
allows private-member reads/writes and `Bpsim.*` build entry points, and the
snippet text is generated here, never taken from a model.
"""

import base64
import json
import math
import os
from pathlib import Path
import time

from unity_mcp import UnityMcp, UnityMcpError


SAFE = {"safety_checks": False}

# Shared C# prelude. The wrapper compiled by execute_code is C# 5 (codedom), so
# helpers are lambdas (local functions are not available) and payloads arrive as
# base64 JSON so no quoting can break the snippet.
PRELUDE = r'''
string _payload = "__PAYLOAD__";
object _raw = Newtonsoft.Json.JsonConvert.DeserializeObject<object>(
    System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(_payload)));
Func<object,object> Norm = null;
Norm = v => {
    var jo = v as Newtonsoft.Json.Linq.JObject;
    if (jo != null) { var d = new Dictionary<string,object>(); foreach (var kv in jo) d[kv.Key] = Norm(kv.Value); return d; }
    var ja = v as Newtonsoft.Json.Linq.JArray;
    if (ja != null) { var l = new List<object>(); foreach (var it in ja) l.Add(Norm(it)); return l; }
    var jv = v as Newtonsoft.Json.Linq.JValue;
    if (jv != null) return jv.Value;
    return v;
};
Dictionary<string,object> P = Norm(_raw) as Dictionary<string,object>;
if (P == null) P = new Dictionary<string,object>();
string _projectRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
Func<object,string,string> ToStr = (v,def) => v == null ? def : System.Convert.ToString(v);
Func<object,float,float> ToFloat = (v,def) => v == null ? def : (float)System.Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
Func<object,bool,bool> ToBool = (v,def) => v == null ? def : (v is bool ? (bool)v : System.Convert.ToString(v).ToLowerInvariant() == "true");
Func<object,int,int> ToInt = (v,def) => v == null ? def : (int)System.Math.Round(System.Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture));
Func<object,Vector3,Vector3> ToVec3 = (v,def) => { var l = v as List<object>; if (l == null || l.Count < 3) return def; return new Vector3(ToFloat(l[0],0), ToFloat(l[1],0), ToFloat(l[2],0)); };
Func<object,Vector2,Vector2> ToVec2 = (v,def) => { var l = v as List<object>; if (l == null || l.Count < 2) return def; return new Vector2(ToFloat(l[0],0), ToFloat(l[1],0)); };
Func<object,Color,Color> ToColor = (v,def) => { var l = v as List<object>; if (l == null || l.Count < 3) return def; return new Color(ToFloat(l[0],0), ToFloat(l[1],0), ToFloat(l[2],0), l.Count >= 4 ? ToFloat(l[3],1) : 1f); };
Func<object,GameObject> FindGo = null;
FindGo = spec => {
    if (spec is long || spec is int || spec is double) {
        UnityEngine.Object o = UnityEditor.EditorUtility.InstanceIDToObject(System.Convert.ToInt32(spec));
        return o as GameObject;
    }
    if (spec is string) {
        string s = (string)spec;
        foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            if (go != null && go.name == s) return go;
        return null;
    }
    var d = spec as Dictionary<string,object>;
    if (d != null && d.ContainsKey("name")) return FindGo(ToStr(d["name"], ""));
    return null;
};
Func<GameObject,string> GetPath = go => {
    string path = go.name;
    Transform parent = go.transform.parent;
    while (parent != null) { path = parent.name + "/" + path; parent = parent.parent; }
    return path;
};
Func<GameObject,Dictionary<string,object>> GoRef = go => new Dictionary<string,object> {
    { "name", go.name }, { "id", go.GetInstanceID() }, { "path", GetPath(go) },
};
Func<string,Type> FindType = name => {
    if (string.IsNullOrEmpty(name)) return null;
    Type exact = null;
    var candidates = new List<Type>();
    foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
        Type[] types;
        try { types = asm.GetTypes(); } catch { continue; }
        foreach (Type t in types) {
            if (t.FullName == name) exact = t;
            else if (t.Name == name) candidates.Add(t);
        }
    }
    if (exact != null) return exact;
    foreach (Type t in candidates) if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) return t;
    foreach (Type t in candidates) if (t.Namespace != null && t.Namespace.StartsWith("UnityEditor")) return t;
    return candidates.Count > 0 ? candidates[0] : null;
};
const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy;
Func<object,string,object> GetMember = (target, path) => {
    object cur = target;
    foreach (string seg in path.Split('.')) {
        Type t = cur.GetType();
        System.Reflection.PropertyInfo pi = t.GetProperty(seg, BF);
        if (pi != null) { cur = pi.GetValue(cur, null); continue; }
        System.Reflection.FieldInfo fi = t.GetField(seg, BF);
        if (fi != null) { cur = fi.GetValue(cur); continue; }
        throw new System.Exception("member not found: " + seg + " on " + t.Name);
    }
    return cur;
};
'''

# Coerce/ToJsonValue are only needed by set_property, so they travel with it.
COERCE = r'''
Func<Type,object,GameObject,object> Coerce = (t,v,context) => {
    var dd = v as Dictionary<string,object>;
    if (dd != null) {
        if (dd.ContainsKey("x") || dd.ContainsKey("r")) {
            if (t == typeof(Vector3)) v = new Vector3(ToFloat(dd.ContainsKey("x")?dd["x"]:0,0), ToFloat(dd.ContainsKey("y")?dd["y"]:0,0), ToFloat(dd.ContainsKey("z")?dd["z"]:0,0));
            else if (t == typeof(Vector2)) v = new Vector2(ToFloat(dd.ContainsKey("x")?dd["x"]:0,0), ToFloat(dd.ContainsKey("y")?dd["y"]:0,0));
            else if (t == typeof(Color)) v = new Color(ToFloat(dd.ContainsKey("r")?dd["r"]:0,0), ToFloat(dd.ContainsKey("g")?dd["g"]:0,0), ToFloat(dd.ContainsKey("b")?dd["b"]:0,0), ToFloat(dd.ContainsKey("a")?dd["a"]:1,1));
            else v = null;
        }
        else if (dd.ContainsKey("name")) v = dd["name"];
        else if (dd.ContainsKey("id")) v = dd["id"];
    }
    if (t == typeof(GameObject)) return FindGo(v);
    if (typeof(Component).IsAssignableFrom(t)) {
        GameObject g = FindGo(v); if (g == null) g = context;
        return g == null ? null : g.GetComponent(t);
    }
    if (typeof(UnityEngine.Object).IsAssignableFrom(t)) {
        string n = ToStr(v, null);
        if (n == null) return null;
        string[] guids = UnityEditor.AssetDatabase.FindAssets(n + " t:" + t.Name);
        foreach (string guid in guids) {
            UnityEngine.Object o = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid), t);
            if (o != null) return o;
        }
        return null;
    }
    if (t == typeof(Vector3)) return ToVec3(v, Vector3.zero);
    if (t == typeof(Vector2)) return ToVec2(v, Vector2.zero);
    if (t == typeof(Quaternion)) {
        var l = v as List<object>;
        if (l != null && l.Count == 4) return new Quaternion(ToFloat(l[0],0), ToFloat(l[1],0), ToFloat(l[2],0), ToFloat(l[3],1));
        if (l != null && l.Count == 3) return Quaternion.Euler(ToFloat(l[0],0), ToFloat(l[1],0), ToFloat(l[2],0));
        return Quaternion.identity;
    }
    if (t == typeof(Color)) return ToColor(v, Color.white);
    if (t == typeof(string)) return ToStr(v, "");
    if (t == typeof(bool)) return ToBool(v, false);
    if (t == typeof(int)) return (int)System.Math.Round(ToFloat(v, 0));
    if (t == typeof(long)) return (long)System.Math.Round(ToFloat(v, 0));
    if (t == typeof(float)) return ToFloat(v, 0f);
    if (t == typeof(double)) return ToFloat(v, 0);
    if (t.IsEnum) { if (v is double || v is long || v is int) return System.Convert.ToInt32(v); return System.Enum.Parse(t, ToStr(v, ""), true); }
    try { return System.Convert.ChangeType(v, t, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
};
Func<object,GameObject,object> ToJsonValue = null;
ToJsonValue = (v, context) => {
    if (v == null) return null;
    Type t = v.GetType();
    if (t == typeof(Vector3)) { Vector3 a = (Vector3)v; return new List<object> { a.x, a.y, a.z }; }
    if (t == typeof(Vector2)) { Vector2 a = (Vector2)v; return new List<object> { a.x, a.y }; }
    if (t == typeof(Quaternion)) { Quaternion q = (Quaternion)v; return new List<object> { q.x, q.y, q.z, q.w }; }
    if (t == typeof(Color)) { Color c = (Color)v; return new List<object> { c.r, c.g, c.b, c.a }; }
    if (v is GameObject) return GoRef((GameObject)v);
    if (v is Component) return GoRef(((Component)v).gameObject);
    if (t.IsPrimitive || v is string || v is decimal) return v;
    if (t.IsEnum) return v.ToString();
    var seq = v as System.Collections.IEnumerable;
    if (seq != null && !(v is string)) {
        var items = new List<object>();
        int n = 0;
        foreach (object item in seq) { if (n++ >= 50) { items.Add("..."); break; } items.Add(ToJsonValue(item, context)); }
        return items;
    }
    return v.ToString();
};
Func<object,string,object,string> SetMember = (target, path, value) => {
    string[] segs = path.Split('.');
    object cur = target;
    for (int i = 0; i < segs.Length - 1; i++) {
        Type t = cur.GetType();
        System.Reflection.PropertyInfo pi = t.GetProperty(segs[i], BF);
        if (pi != null) { cur = pi.GetValue(cur, null); continue; }
        System.Reflection.FieldInfo fi = t.GetField(segs[i], BF);
        if (fi != null) { cur = fi.GetValue(cur); continue; }
        throw new System.Exception("member not found: " + segs[i]);
    }
    string last = segs[segs.Length - 1];
    Type tt = cur.GetType();
    System.Reflection.PropertyInfo pLast = tt.GetProperty(last, BF);
    if (pLast != null && pLast.CanWrite) { pLast.SetValue(cur, Coerce(pLast.PropertyType, value, cur as GameObject), null); return null; }
    System.Reflection.FieldInfo fLast = tt.GetField(last, BF);
    if (fLast != null) { fLast.SetValue(cur, Coerce(fLast.FieldType, value, cur as GameObject)); return null; }
    throw new System.Exception("member not settable: " + last);
};
'''

#: The 30 legacy DshBridge method names, mapped to the adapter method that serves each one.
METHODS = {
    "hierarchy": "hierarchy",
    "find": "find",
    "create_primitive": "create_primitive",
    "create_empty": "create_empty",
    "destroy": "destroy",
    "set_transform": "set_transform",
    "set_active": "set_active",
    "add_component": "add_component",
    "remove_component": "remove_component",
    "get_components": "get_components",
    "set_property": "set_property",
    "get_property": "get_property",
    "write_script": "write_script",
    "read_file": "read_file",
    "list_dir": "list_dir",
    "compile": "compile",
    "compile_status": "compile_status",
    "set_status": "set_status",
    "console": "console",
    "profiler": "profiler_read",
    "playmode": "playmode",
    "playmode_state": "playmode_state",
    "screenshot": "screenshot",
    "new_scene": "new_scene",
    "save_scene": "save_scene",
    "open_scene": "open_scene",
    "invoke_static": "invoke_static",
    "create_material": "create_material",
}


class AdapterError(RuntimeError):
    pass


def _b64(value):
    return base64.b64encode(json.dumps(value, ensure_ascii=False).encode("utf-8")).decode("ascii")


class UnityMcpAdapter:
    """Implements the 30 legacy methods on top of a UnityMcp session."""

    def __init__(self, project, mcp=None, url=None, timeout=120.0):
        self.project = Path(project).resolve()
        self._mcp = mcp
        self._url = url
        self._timeout = timeout

    # -- transport ---------------------------------------------------------
    def mcp(self):
        if self._mcp is None:
            self._mcp = UnityMcp(url=self._url, project=self.project, timeout=self._timeout)
            self._mcp.connect()
        return self._mcp

    def close(self):
        if self._mcp is not None:
            self._mcp.close()
            self._mcp = None

    def call(self, tool, arguments=None, timeout=None):
        try:
            return self.mcp().call(tool, arguments or {}, timeout=timeout)
        except UnityMcpError as exc:
            raise AdapterError(str(exc)) from exc

    def _code(self, template, params, prelude=""):
        payload = _b64(params)
        # The snippet is wrapped in its own block: codedom (C# 5) rejects a local that shares a
        # name with a prelude lambda parameter ("A local variable named `v' cannot be declared in
        # this scope because it would give a different meaning to `v', which is already used in a
        # `child' scope"). Keep the shared helpers in the method scope and the per-call snippet in
        # a child block so the two name spaces cannot collide.
        body = prelude + "{\n" + template + "\n}"
        source = (PRELUDE + body).replace("__PAYLOAD__", payload)
        reply = self.call("execute_code", {"action": "execute", "code": source, **SAFE})
        data = reply.get("data") if isinstance(reply, dict) else None
        return data["result"] if isinstance(data, dict) and "result" in data else data

    # -- identity / state --------------------------------------------------
    def _resource(self, uri):
        """Read one MCP resource and unwrap the ``{"success":…,"data":…}`` envelope."""
        payload = json.loads(self.mcp().read_resource(uri))
        if isinstance(payload, dict) and "data" in payload:
            if payload.get("success") is False:
                raise AdapterError(f"{uri} failed: {payload.get('message') or payload.get('error')}")
            return payload["data"]
        return payload

    def project_info(self):
        return self._resource("mcpforunity://project/info")

    def editor_state(self, deadline=45.0):
        """Read editor state, riding out the short window a domain reload makes it unavailable.

        Entering or leaving play mode (and any script compile) drops the MCP session and makes
        ``get_editor_state`` answer "ping not answered"; polling is what the server itself
        recommends instead of treating the first refusal as a failure.
        """
        end = time.time() + deadline
        while True:
            try:
                return self._resource("mcpforunity://editor/state")
            except AdapterError as exc:
                text = str(exc).lower()
                if time.time() >= end or not ("not ready" in text or "ping not answered" in text
                                              or "disconnected" in text):
                    raise
            self.close()
            time.sleep(0.5)

    def ping(self):
        info = self.project_info()
        root = info.get("projectRoot") or ""
        if not root:
            raise AdapterError("MCP server did not report a project root; refusing to dispatch")
        if Path(root).resolve() != self.project:
            raise AdapterError(
                f"Wrong Unity project at the MCP server: {root}; expected {self.project}")
        state = self.editor_state()
        play = state.get("editor", {}).get("play_mode", {})
        compile_ = state.get("compilation", {})
        advice = state.get("advice", {})
        return {
            "ok": True,
            "project": root,
            "projectId": state.get("unity", {}).get("instance_id"),
            "transport": "mcp-for-unity",
            "server": (self.mcp().server_info or {}).get("version"),            "playing": bool(play.get("is_playing")),
            "changing": bool(play.get("is_changing")),
            "compiling": bool(compile_.get("is_compiling")),
            "busy": bool(state.get("assets", {}).get("is_updating"))
                    or bool(state.get("tests", {}).get("is_running"))
                    or not advice.get("ready_for_tools", True),
            "mainThreadStalled": not advice.get("ready_for_tools", True),
            "ready": bool(advice.get("ready_for_tools")),
        }

    # -- reads -------------------------------------------------------------
    def hierarchy(self, params):
        return self._code(r'''
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
Func<Transform,int,object> dump = null;
dump = (tr, depth) => {
    var go = tr.gameObject;
    var d = new Dictionary<string,object>();
    d["name"] = go.name;
    d["id"] = go.GetInstanceID();
    d["active"] = go.activeSelf;
    d["position"] = new List<object> { tr.position.x, tr.position.y, tr.position.z };
    d["rotation"] = new List<object> { tr.rotation.eulerAngles.x, tr.rotation.eulerAngles.y, tr.rotation.eulerAngles.z };
    d["scale"] = new List<object> { tr.localScale.x, tr.localScale.y, tr.localScale.z };
    var comps = new List<object>();
    foreach (Component c in go.GetComponents<Component>()) if (c != null) comps.Add(c.GetType().FullName);
    d["components"] = comps;
    var kids = new List<object>();
    if (depth < 6) foreach (Transform ch in tr) kids.Add(dump(ch, depth + 1));
    d["children"] = kids;
    return d;
};
var roots = new List<object>();
foreach (GameObject r in scene.GetRootGameObjects()) roots.Add(dump(r.transform, 0));
return new Dictionary<string,object> {
    { "scene", scene.name }, { "path", scene.path }, { "roots", roots },
};
''', params)

    def find(self, params):
        return self._code(r'''
string term = ToStr(P.ContainsKey("name") ? P["name"] : null, "");
var hits = new List<object>();
foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>()) {
    if (go == null) continue;
    if (go.name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
    hits.Add(GoRef(go));
}
return hits;
''', params)

    def get_components(self, params):
        return self._code(r'''
GameObject go = FindGo(P.ContainsKey("target") ? P["target"] : null);
if (go == null) throw new System.Exception("target not found");
var comps = new List<object>();
foreach (Component c in go.GetComponents<Component>()) if (c != null) comps.Add(c.GetType().FullName);
return comps;
''', params)

    def get_property(self, params):
        return self._code(r'''
GameObject go = FindGo(P.ContainsKey("target") ? P["target"] : null);
if (go == null) throw new System.Exception("target not found");
string compTypeName = ToStr(P.ContainsKey("component") ? P["component"] : null, "");
string path = ToStr(P.ContainsKey("path") ? P["path"] : null, "");
object target = go;
if (!string.IsNullOrEmpty(compTypeName)) {
    Type ct = FindType(compTypeName);
    if (ct == null) throw new System.Exception("component type not found");
    target = go.GetComponent(ct);
    if (target == null) throw new System.Exception("component not present");
}
return ToJsonValue(GetMember(target, path), go);
''', params, COERCE)

    def read_file(self, params):
        path = self._safe_path(params.get("path"))
        if not path.is_file():
            raise AdapterError(f"file not found: {params.get('path')}")
        return path.read_text(encoding="utf-8-sig", errors="replace")

    def list_dir(self, params):
        rel = params.get("path") or "Assets"
        path = self._safe_path(rel)
        if not path.is_dir():
            raise AdapterError(f"dir not found: {rel}")
        return [{"name": entry.name, "isDir": entry.is_dir()} for entry in sorted(
            path.iterdir(), key=lambda p: (not p.is_dir(), p.name.lower()))]

    def compile_status(self, params):
        count = int(params.get("count") or 80)
        state = self.editor_state()
        logs = self.console({"count": count, "types": "all"})
        errors = [entry for entry in logs if entry["type"].lower() == "error"]
        warnings = [entry for entry in logs if entry["type"].lower() == "warning"]
        return {
            "compiling": bool(state.get("compilation", {}).get("is_compiling")),
            "errors": errors,
            "warnings": warnings,
        }

    def console(self, params):
        if params.get("clear"):
            raise AdapterError("Clearing other agents' console logs is not exposed")
        count = int(params.get("count") or 50)
        types = params.get("types") or params.get("type") or "all"
        if isinstance(types, str):
            types = [types]
        reply = self.call("read_console", {
            "action": "get", "format": "json", "types": list(types),
            "count": max(1, min(count, 1000)), "include_stacktrace": True,
        })
        entries = (reply or {}).get("data") or []
        return [{
            "type": entry.get("type") or "Log",
            "message": _strip_rich_text(entry.get("message") or ""),
            "stack": entry.get("stackTrace"),
            "time": None,
        } for entry in entries]

    def playmode_state(self, params):
        state = self.editor_state()
        play = state.get("editor", {}).get("play_mode", {})
        return {"playing": bool(play.get("is_playing")), "changing": bool(play.get("is_changing"))}

    def profiler_read(self, params):
        return self._code(r'''
var t = System.Type.GetType("Bpsim.Perf.EditorProfiler, Assembly-CSharp-Editor");
if (t == null) {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
        t = asm.GetType("Bpsim.Perf.EditorProfiler");
        if (t != null) break;
    }
}
if (t == null) throw new System.Exception("Bpsim.Perf.EditorProfiler not present in this project");
var mi = t.GetMethod("Read", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (mi == null) throw new System.Exception("Bpsim.Perf.EditorProfiler.Read(Dictionary<string,object>) not found");
var query = new Dictionary<string,object>();
if (P.ContainsKey("count")) query["count"] = P["count"];
if (P.ContainsKey("afterSequence")) query["afterSequence"] = P["afterSequence"];
if (P.ContainsKey("sessionId")) query["sessionId"] = P["sessionId"];
object result = mi.Invoke(null, new object[] { query });
var jv = result as Newtonsoft.Json.Linq.JValue;
return jv != null ? jv.Value : result;
''', params)

    # -- writes ------------------------------------------------------------
    def create_primitive(self, params):
        kind = str(params.get("type") or "cube")
        name = params.get("name") or kind
        created = self.call("manage_gameobject", {
            "action": "create", "name": str(name),
            "primitive_type": kind[:1].upper() + kind[1:].lower(),
        })
        instance = created["data"]["name"]
        self._apply_transform(instance, params)
        return self._go_ref(instance)

    def create_empty(self, params):
        name = params.get("name") or "GameObject"
        self.call("manage_gameobject", {"action": "create", "name": str(name)})
        self._apply_transform(str(name), params)
        return self._go_ref(str(name))

    def _apply_transform(self, target, params):
        keys = ("position", "rotation", "scale", "localPosition", "localRotation", "parent")
        if not any(key in params for key in keys):
            return
        payload = {"target": target}
        for key in keys:
            if key in params:
                payload[key] = params[key]
        self._code(r'''
GameObject go = FindGo(P.ContainsKey("target") ? P["target"] : null);
if (go == null) throw new System.Exception("target not found");
if (P.ContainsKey("position")) go.transform.position = ToVec3(P["position"], go.transform.position);
if (P.ContainsKey("rotation")) go.transform.rotation = Quaternion.Euler(ToVec3(P["rotation"], go.transform.rotation.eulerAngles));
if (P.ContainsKey("scale")) go.transform.localScale = ToVec3(P["scale"], go.transform.localScale);
if (P.ContainsKey("localPosition")) go.transform.localPosition = ToVec3(P["localPosition"], go.transform.localPosition);
if (P.ContainsKey("localRotation")) go.transform.localRotation = Quaternion.Euler(ToVec3(P["localRotation"], go.transform.localRotation.eulerAngles));
if (P.ContainsKey("parent")) {
    object spec = P["parent"];
    if (spec == null) go.transform.SetParent(null);
    else {
        GameObject parent = FindGo(spec);
        if (parent == null) throw new System.Exception("parent not found");
        go.transform.SetParent(parent.transform);
    }
}
return null;
''', payload)

    def destroy(self, params):
        target = params.get("target")
        try:
            self.call("manage_gameobject", {"action": "delete", "target": _as_target(target)})
        except AdapterError as first:
            if "not found" not in str(first).lower():
                raise
            # Coplay cannot delete inactive objects; the legacy shim could.
            self._code(r'''
GameObject go = null;
foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>()) {
    if (candidate == null) continue;
    if (candidate.hideFlags != HideFlags.None) continue;
    if (string.IsNullOrEmpty(candidate.scene.path)) continue;
    if (candidate.name != ToStr(P["target"], "")) continue;
    go = candidate;
    break;
}
if (go == null) throw new System.Exception("target not found");
if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(go);
else UnityEngine.Object.DestroyImmediate(go);
return true;
''', {"target": _as_target(target)})
        return True

    def set_transform(self, params):
        self._apply_transform(_as_target(params.get("target")), params)
        return self._go_ref(params.get("target"))

    def set_active(self, params):
        target = _as_target(params.get("target"))
        active = params.get("active")
        active = True if active is None else bool(active)
        try:
            reply = self.call("manage_gameobject", {
                "action": "modify", "target": target, "set_active": active})
        except AdapterError as first:
            if "not found" not in str(first).lower():
                raise
            self._code(r'''
GameObject go = null;
foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>()) {
    if (candidate == null) continue;
    if (candidate.hideFlags != HideFlags.None) continue;
    if (string.IsNullOrEmpty(candidate.scene.path)) continue;
    if (candidate.name != ToStr(P["target"], "")) continue;
    go = candidate;
    break;
}
if (go == null) throw new System.Exception("target not found");
go.SetActive(ToBool(P["active"], true));
return null;
''', {"target": target, "active": active})
        else:
            if reply.get("data", {}).get("activeSelf") != active:
                raise AdapterError(f"Unity did not change active state of {target}")
        return True

    def add_component(self, params):
        target = _as_target(params.get("target"))
        type_name = str(params.get("type") or params.get("component") or "")
        if not type_name:
            raise AdapterError("type not found: ")
        existing = self.get_components({"target": target})
        wanted = _match_component(existing, type_name)
        if wanted:
            return {"exists": True, "type": wanted}
        reply = self.call("manage_components", {
            "action": "add", "target": str(target), "component_type": type_name})
        added = (reply.get("data") or {}).get("componentType") or type_name
        return {"exists": False, "type": added}

    def remove_component(self, params):
        target = _as_target(params.get("target"))
        type_name = str(params.get("type") or params.get("component") or "")
        existing = self.get_components({"target": target})
        wanted = _match_component(existing, type_name)
        if not wanted:
            raise AdapterError("component not present")
        self.call("manage_components", {
            "action": "remove", "target": str(target), "component_type": wanted})
        return True

    def set_property(self, params):
        self._code(r'''
GameObject go = FindGo(P.ContainsKey("target") ? P["target"] : null);
if (go == null) throw new System.Exception("target not found");
string compTypeName = ToStr(P.ContainsKey("component") ? P["component"] : null, "");
string path = ToStr(P.ContainsKey("path") ? P["path"] : null, "");
object target = go;
if (!string.IsNullOrEmpty(compTypeName)) {
    Type ct = FindType(compTypeName);
    if (ct == null) throw new System.Exception("component type not found: " + compTypeName);
    target = go.GetComponent(ct);
    if (target == null) throw new System.Exception("component not present: " + compTypeName);
}
SetMember(target, path, P.ContainsKey("value") ? P["value"] : null);
UnityEditor.EditorUtility.SetDirty(target as UnityEngine.Object);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
return true;
''', params, COERCE)
        return True

    def write_script(self, params):
        rel = params.get("path") or ""
        content = params.get("content") or ""
        if not rel or ".." in rel or not rel.lower().startswith("assets/"):
            raise AdapterError("path must be relative under Assets/")
        full = self.project / rel
        full.parent.mkdir(parents=True, exist_ok=True)
        with full.open("w", encoding="utf-8", newline="") as handle:
            handle.write(content)
        self.call("refresh_unity", {"mode": "force", "scope": "assets", "compile": "none",
                                    "wait_for_ready": False})
        return {"path": rel, "bytes": len(content)}

    def compile(self, params):
        self.call("refresh_unity", {"mode": "force", "scope": "all", "compile": "request",
                                    "wait_for_ready": False})
        return {"started": True}

    def set_status(self, params):
        text = str(params.get("text") or "")
        target = self.project / "Temp" / "DshBridge"
        target.mkdir(parents=True, exist_ok=True)
        try:
            (target / "task.txt").write_text(text, encoding="utf-8")
        except OSError:
            pass
        return True

    def playmode(self, params):
        action = str(params.get("action") or "enter").lower()
        state = self.playmode_state({})
        playing = state["playing"]
        if action == "toggle":
            action = "exit" if playing else "enter"
        if action not in {"enter", "exit"}:
            raise AdapterError("playmode action must be enter, exit or toggle")
        if (action == "enter" and playing) or (action == "exit" and not playing):
            return state
        self._editor_tool("play" if action == "enter" else "stop")
        return self.playmode_state({})

    def _editor_tool(self, action):
        # The bridge session is dropped across the play-mode domain reload, so a
        # single retry after reconnecting is required (same reason the legacy
        # Tools/dsh.py retried).
        try:
            return self.call("manage_editor", {"action": action})
        except AdapterError as first:
            if "disconnected" not in str(first).lower() and "session" not in str(first).lower():
                raise
            self.close()
            return self.call("manage_editor", {"action": action})

    def screenshot(self, params):
        width = int(params.get("width") or 640)
        height = int(params.get("height") or 480)
        position = params.get("position") or [0, 4, -7]
        look_at = params.get("lookAt") or [0, 1, 0]
        rotation = params.get("rotation") or _look_rotation(position, look_at)
        folder = "Temp/DshBridge"
        shot_dir = self.project / folder
        shot_dir.mkdir(parents=True, exist_ok=True)
        index = len(list(shot_dir.glob("shot_*.png")))
        name = f"shot_{index:03d}"
        reply = self.call("manage_camera", {
            "action": "screenshot", "capture_source": "game_view", "include_image": False,
            "screenshot_file_name": name, "output_folder": folder,
            "view_position": list(position), "view_rotation": list(rotation),
            "max_resolution": max(width, height),
        })
        data = reply.get("data") or {}
        rel = data.get("path") or f"{folder}/{name}.png"
        full = Path(data.get("fullPath") or (self.project / rel))
        return {
            "path": rel,
            "abs": str(full),
            "width": data.get("imageWidth"),
            "height": data.get("imageHeight"),
            "bytes": full.stat().st_size if full.exists() else None,
            "requested": {"width": width, "height": height},
        }

    def new_scene(self, params):
        return self._code(r'''
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
    UnityEditor.SceneManagement.NewSceneMode.Single);
return new Dictionary<string,object> { { "scene", scene.name } };
''', params)

    def save_scene(self, params):
        rel = params.get("path") or ""
        if rel and (".." in rel or not rel.lower().startswith("assets/")):
            raise AdapterError("path must be relative under Assets/")
        if rel:
            full = self.project / rel
            full.parent.mkdir(parents=True, exist_ok=True)
        # An empty path saves the active scene in place; a path saves it under that asset.
        saved = self._code(r'''
string rel = ToStr(P["path"], "");
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (rel.Length == 0) {
    if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) {
        throw new System.Exception("scene not saved (untitled scene needs a path)");
    }
    UnityEditor.AssetDatabase.Refresh();
    return scene.path.Replace("\\", "/");
}
string full = System.IO.Path.Combine(_projectRoot, rel);
string dir = System.IO.Path.GetDirectoryName(full);
if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, full);
UnityEditor.AssetDatabase.Refresh();
return rel;
''', {"path": Path(rel).as_posix() if rel else ""})
        text = str(saved or rel).replace("\\", "/")
        # Scene.path is absolute; the legacy contract reports a project-relative path.
        prefix = self.project.as_posix() + "/"
        if text.startswith(prefix):
            text = text[len(prefix):]
        return {"path": text}

    def open_scene(self, params):
        rel = params.get("path") or ""
        rel = Path(rel).as_posix()
        if not (self.project / rel).is_file():
            raise AdapterError(f"scene not found: {rel}")
        try:
            self.call("manage_scene", {"action": "load", "path": rel})
        except AdapterError as first:
            if "unsaved changes" not in str(first).lower():
                raise
            # The legacy command discarded unsaved edits; callers under the lease
            # still expect that, so it is reproduced explicitly and visibly.
            self._code(r'''
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    ToStr(P["path"], ""), UnityEditor.SceneManagement.OpenSceneMode.Single);
return true;
''', {"path": rel})
        return {"path": rel}

    def invoke_static(self, params):
        type_name = str(params.get("type") or "")
        if not type_name.startswith("Bpsim."):
            raise AdapterError("Static invocation is limited to this project's Bpsim.* types")
        return self._code(r'''
string typeName = ToStr(P["type"], "");
string methodName = ToStr(P["method"], "");
var rawArgs = P.ContainsKey("args") ? P["args"] as List<object> : null;
if (rawArgs == null) rawArgs = new List<object>();
Type t = FindType(typeName);
if (t == null) throw new System.Exception("type not found: " + typeName);
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
System.Reflection.MethodInfo mi = t.GetMethod(methodName, flags);
if (mi == null) {
    foreach (var candidate in t.GetMethods(flags)) {
        if (candidate.Name == methodName && candidate.GetParameters().Length == rawArgs.Count) { mi = candidate; break; }
    }
}
if (mi == null) throw new System.Exception("method not found: " + typeName + "." + methodName);
var ps = mi.GetParameters();
var argv = new object[ps.Length];
for (int i = 0; i < ps.Length; i++) argv[i] = i < rawArgs.Count ? Coerce(ps[i].ParameterType, rawArgs[i], null) : null;
try { return ToJsonValue(mi.Invoke(null, argv), null); }
catch (System.Reflection.TargetInvocationException ex) {
    if (ex.InnerException != null) throw new System.Exception(ex.InnerException.Message + "\n" + ex.InnerException.StackTrace);
    throw;
}
''', params, COERCE)

    def create_material(self, params):
        name = params.get("name") or "Mat"
        if not name or any(ch in name for ch in '/\\:<>"|?*') or ".." in name:
            raise AdapterError("Material name must be a simple filename without an extension")
        rel = f"Assets/Materials/{name}.mat"
        folder = self.project / "Assets" / "Materials"
        exists = (folder / f"{name}.mat").exists()
        if not exists:
            self._ensure_asset_folder("Assets/Materials")
            arguments = {"action": "create", "material_path": rel,
                         "shader": params.get("shader") or "Standard"}
            if params.get("color") is not None:
                arguments["color"] = params["color"]
            self.call("manage_material", arguments)
        elif params.get("color") is not None:
            self.call("manage_material", {
                "action": "set_material_color", "material_path": rel,
                "color": params["color"]})
        return {"name": name, "path": rel}

    def _ensure_asset_folder(self, rel):
        """Create an asset folder, tolerating the stale-GUID state the Coplay action rejects.

        ``manage_asset create_folder`` refuses a path that Unity still maps to a GUID it no
        longer considers a valid folder (happens when a folder was deleted outside the editor).
        Creating the directory on disk and refreshing lets Unity re-adopt the path.
        """
        try:
            self.call("manage_asset", {"action": "create_folder", "path": rel})
            return
        except AdapterError as exc:
            if "already exists" not in str(exc).lower():
                raise
        path = self._safe_path(rel)
        path.mkdir(parents=True, exist_ok=True)
        self.call("refresh_unity", {"mode": "force", "scope": "assets", "compile": "none",
                                    "wait_for_ready": False})
        valid = self._code(r'''
return UnityEditor.AssetDatabase.IsValidFolder(ToStr(P["path"], ""));
''', {"path": rel})
        if not valid:
            raise AdapterError(f"could not create asset folder: {rel}")

    # -- helpers -----------------------------------------------------------
    def _go_ref(self, target):
        name = target if isinstance(target, str) else None
        if name is None:
            raise AdapterError("target not found")
        hits = self.call("find_gameobjects", {
            "search_term": name, "search_method": "by_name", "include_inactive": True})
        ids = hits.get("data", {}).get("instanceIDs") or []
        if not ids:
            raise AdapterError("target not found")
        instance_id = ids[0]
        info = json.loads(self.mcp().read_resource(
            f"mcpforunity://scene/gameobject/{instance_id}"))
        data = info.get("data") or {}
        return {"name": data.get("name", name), "id": instance_id, "path": data.get("path", name)}

    def _safe_path(self, rel):
        if not isinstance(rel, str) or not rel or ".." in rel:
            raise AdapterError("Use a relative path inside the project")
        path = (self.project / rel).resolve()
        if not path.is_relative_to(self.project):
            raise AdapterError("Path escapes the configured Unity project")
        return path

    def dispatch(self, method, params):
        handler_name = METHODS.get(method)
        if handler_name is None:
            raise AdapterError(f"unknown method: {method}")
        return getattr(self, handler_name)(dict(params or {}))


def _as_target(value):
    if isinstance(value, dict) and "name" in value:
        return value["name"]
    if isinstance(value, bool) or not isinstance(value, (str, int, float, dict)):
        raise AdapterError("target not found")
    if isinstance(value, (int, float)):
        return int(value)
    return value


def _match_component(names, wanted):
    if not wanted:
        return None
    for name in names:
        if name == wanted or name.split(".")[-1] == wanted:
            return name
    return None


def _look_rotation(position, look_at):
    """Euler angles (degrees) for a camera at `position` looking at `look_at`."""
    try:
        dx = float(look_at[0]) - float(position[0])
        dy = float(look_at[1]) - float(position[1])
        dz = float(look_at[2]) - float(position[2])
    except (TypeError, IndexError, ValueError):
        return [0, 0, 0]
    if dx == dz == 0:
        return [90.0 if dy < 0 else -90.0, 0, 0]
    pitch = math.degrees(math.atan2(-dy, math.hypot(dx, dz)))
    yaw = math.degrees(math.atan2(dx, dz))
    return [round(pitch, 4), round(yaw, 4), 0.0]


_RICH_TAG = __import__("re").compile(r"<[^<>]*>")


def _strip_rich_text(text):
    return _RICH_TAG.sub("", text)

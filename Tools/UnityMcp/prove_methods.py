"""Prove every legacy bridge capability is callable through the MCP-for-Unity transport.

Run from the project root:  py Tools/UnityMcp/prove_methods.py

Each legacy method is exercised against the live editor, using a scratch folder
(``Assets/DshBridgeProbe``) that is removed again.  The report lists one row per method so a
regression is obvious at a glance.
"""

import json
from pathlib import Path
import sys
import time

from bridge import Bridge, BridgeError


PROJECT = Path(__file__).resolve().parents[2]
SCRATCH = "Assets/DshBridgeProbe"
report = []


def record(method, ok, detail):
    report.append({"method": method, "ok": bool(ok), "detail": detail})
    flag = "PASS" if ok else "FAIL"
    print(f"[{flag}] {method}: {str(detail)[:220]}")


def check(method, fn, *, expect=None, contains=None):
    """Run one legacy call and check the expectations that used to be contractual."""
    try:
        result = fn()
    except (BridgeError, Exception) as exc:  # noqa: BLE001 - a failure is data here
        record(method, False, f"{type(exc).__name__}: {exc}")
        return None
    if expect is not None and result != expect:
        record(method, False, f"expected {expect!r}, got {result!r}")
        return result
    if contains is not None:
        blob = json.dumps(result, ensure_ascii=False, default=str)
        missing = [key for key in contains if key not in blob]
        if missing:
            record(method, False, f"missing {missing} in {blob[:220]}")
            return result
    record(method, True, result if not isinstance(result, (dict, list)) else
           json.dumps(result, ensure_ascii=False, default=str)[:200])
    return result


def reset_pending(bridge, token):
    """Clear a quarantined write so one failure does not poison the remaining checks."""
    try:
        bridge.acknowledge(token, "prove_methods: acknowledged after a probe failure", True)
    except BridgeError:
        pass


def main():
    bridge = Bridge(PROJECT)
    token = None
    try:
        # ---------------------------------------------------------------- connection
        status = check("status", bridge.status, contains=["project", "transport", "server", "editor_busy"])
        print("    transport:", (status or {}).get("transport"), "server:", (status or {}).get("server"))
        token = bridge.acquire("dsh-bridge-probe", "prove every legacy method still works", 600)["token"]
        _wait_ready(bridge)

        def w(method, params=None):
            """Every legacy write, with quarantine cleared afterwards so one failure cannot
            poison the remaining checks (a failed write pins the lease until acknowledged).
            A brief readiness wait absorbs the domain reload that compile/refresh triggers;
            a busy editor is retried because refusing to dispatch while busy is the contract,
            not a failure."""
            last = None
            for _ in range(4):
                _wait_ready(bridge, deadline=90)
                try:
                    return bridge.write(method, token, params)
                except BridgeError as exc:
                    last = exc
                    if "no write was dispatched" not in str(exc):
                        reset_pending(bridge, token)
                        raise
                    time.sleep(2)
            raise last

        # ---------------------------------------------------------------- reads
        check("hierarchy", lambda: bridge.read("hierarchy"), contains=["scene", "roots"])
        check("find", lambda: bridge.read("find", {"name": "CoreManager"}), contains=["CoreManager"])
        check("get_components", lambda: bridge.read("get_components", {"target": "CoreManager"}),
              contains=["CoreManager"])
        check("get_property", lambda: bridge.read("get_property", {"target": "CoreManager",
                                                                  "component": "Transform",
                                                                  "path": "position"}))
        check("read_file", lambda: bridge.read("read_file", {"path": "ProjectSettings/ProjectVersion.txt"}),
              contains=["2022.2.23f1c1"])
        check("list_dir", lambda: bridge.read("list_dir", {"path": "Assets/Editor"}), contains=["isDir"])
        check("compile_status", lambda: bridge.read("compile_status", {"count": 5}), contains=["compiling"])
        check("console", lambda: _console_roundtrip(bridge), contains=["message"])
        check("playmode_state", lambda: bridge.read("playmode_state"), contains=["playing"])
        check("profiler", lambda: bridge.read("profiler", {"count": 3}))

        # ---------------------------------------------------------------- writes: scratch object
        built = check("create_primitive", lambda: w("create_primitive", {"type": "Cube", "name": "ProbeCube",
                                                                        "position": [1, 2, 3]}),
                      contains=["ProbeCube"])
        check("create_empty", lambda: w("create_empty", {"name": "ProbeEmpty", "position": [4, 5, 6]}),
              contains=["ProbeEmpty"])
        check("set_transform", lambda: w("set_transform", {"target": "ProbeCube", "position": [7, 8, 9],
                                                           "scale": [2, 2, 2]}),
              contains=["ProbeCube"])
        check("set_active", lambda: w("set_active", {"target": "ProbeEmpty", "active": False}), expect=True)
        check("add_component", lambda: w("add_component", {"target": "ProbeCube", "type": "BoxCollider"}),
              contains=["type"])
        check("add_component (idempotent)", lambda: w("add_component", {"target": "ProbeCube",
                                                                        "type": "BoxCollider"}),
              contains=["exists"])
        check("set_property", lambda: w("set_property", {"target": "ProbeCube", "component": "BoxCollider",
                                                         "path": "enabled", "value": False}), expect=True)
        check("get_property (after write)", lambda: bridge.read("get_property",
                                                                {"target": "ProbeCube", "component": "BoxCollider",
                                                                 "path": "enabled"}))
        check("remove_component", lambda: w("remove_component", {"target": "ProbeCube",
                                                                 "type": "BoxCollider"}), expect=True)

        # ---------------------------------------------------------------- writes: files / assets
        check("write_script", lambda: w("write_script", {"path": f"{SCRATCH}/ProbeScript.cs",
                                                         "content": "// probe\n"}), contains=["path"])
        check("create_material", lambda: w("create_material", {"name": "ProbeMat",
                                                               "color": [1, 0, 0, 1]}), contains=["path"])
        check("compile", lambda: w("compile"), contains=["started"])
        check("set_status", lambda: w("set_status", {"text": "dsh-bridge probe running"}), expect=True)
        check("screenshot", lambda: w("screenshot", {"width": 640, "height": 480, "name": "probe_shot"}),
              contains=["path"])

        # Destroy while the scratch object still exists: `new_scene` below replaces the scene,
        # and destroying a missing target is a legitimate "target not found" error, not a pass.
        check("destroy", lambda: w("destroy", {"target": "ProbeEmpty"}), expect=True)

        # ---------------------------------------------------------------- writes: scenes
        check("new_scene", lambda: w("new_scene"), contains=["scene"])
        check("save_scene", lambda: w("save_scene", {"path": f"{SCRATCH}/ProbeScene.unity"}), contains=["path"])
        check("open_scene", lambda: w("open_scene", {"path": "Assets/Scenes/MainScene.unity"}),
              contains=["path"])

        # ---------------------------------------------------------------- writes: editor / statics
        check("invoke_static", lambda: w("invoke_static", {"type": "Bpsim.EditorTools.FrameShapesAudit",
                                                           "method": "Run", "args": []}),
              contains=[])
        check("playmode (enter)", lambda: w("playmode", {"action": "enter"}), contains=["playing"])
        _wait_play(bridge, True)
        check("playmode (exit)", lambda: w("playmode", {"action": "exit"}))
        _wait_play(bridge, False)

        # `quit` is intentionally refused by the bridge contract.
        try:
            bridge.write("quit", token)
        except BridgeError as exc:
            record("quit (refused by design)", "intentionally disabled" in str(exc), str(exc)[:160])
        else:
            record("quit (refused by design)", False, "quit was dispatched")
    finally:
        _cleanup(bridge, token)
        bridge.close()

    passed = sum(1 for row in report if row["ok"])
    print(f"\n{passed}/{len(report)} capabilities verified")
    print(json.dumps({"ok": passed == len(report), "verified": passed, "total": len(report),
                      "transport": (status or {}).get("transport"),
                      "server": (status or {}).get("server"),
                      "failures": [row for row in report if not row["ok"]]}, ensure_ascii=False, indent=2))
    return 0 if passed == len(report) else 1


def _console_roundtrip(bridge):
    """Log a unique marker, then read it back through the console method.

    The editor's console is genuinely empty right after a restart, so an empty read proves
    nothing; a marker that comes back through the legacy shape proves the whole path.
    """
    marker = f"dsh-bridge-probe-{int(time.time())}"
    bridge.adapter.call("execute_code", {
        "action": "execute", "safety_checks": False,
        "code": f'UnityEngine.Debug.Log("{marker}"); return true;',
    })
    entries = bridge.read("console", {"count": 20})
    hits = [entry for entry in entries if marker in entry.get("message", "")]
    if not hits:
        raise AssertionError(f"marker {marker} not found in {len(entries)} console entries")
    return hits


def _wait_ready(bridge, deadline=120):
    """Wait until the editor reports itself ready (a domain reload or asset refresh blocks calls)."""
    end = time.time() + deadline
    while time.time() < end:
        try:
            info = bridge.ping()
        except BridgeError:
            time.sleep(1)
            continue
        if not bridge.is_busy(info):
            return info
        time.sleep(1)
    return None


def _wait_play(bridge, want, deadline=120):
    end = time.time() + deadline
    while time.time() < end:
        try:
            state = bridge.read("playmode_state")
        except BridgeError:
            time.sleep(1)
            continue
        if bool(state.get("playing")) == want and not state.get("changing"):
            return state
        time.sleep(1)
    return None


def _cleanup(bridge, token):
    """Remove the scratch folder and any probe objects left behind."""
    if token is None:
        return
    for target in ["ProbeCube", "ProbeEmpty", "DbgDestroy"]:
        try:
            bridge.write("destroy", token, {"target": target})
        except BridgeError:
            reset_pending(bridge, token)
    try:
        bridge.write("open_scene", token, {"path": "Assets/Scenes/MainScene.unity"})
    except BridgeError:
        reset_pending(bridge, token)
    scratch = PROJECT / SCRATCH
    if scratch.exists():
        for path in sorted(scratch.rglob("*"), reverse=True):
            path.unlink() if path.is_file() else path.rmdir()
        scratch.rmdir()
        meta = PROJECT / (SCRATCH + ".meta")
        if meta.exists():
            meta.unlink()
    # `create_material` puts ProbeMat under Assets/Materials, a folder this project does not
    # otherwise use; remove the probe's material and the folder if the probe created it.
    materials = PROJECT / "Assets" / "Materials"
    probe_mat = materials / "ProbeMat.mat"
    if probe_mat.exists():
        probe_mat.unlink()
        sidecar = probe_mat.with_suffix(".mat.meta")
        if sidecar.exists():
            sidecar.unlink()
        if not any(materials.iterdir()):
            materials.rmdir()
            materials_meta = PROJECT / "Assets" / "Materials.meta"
            if materials_meta.exists():
                materials_meta.unlink()
    screen = PROJECT / "Temp/DshBridge"
    for pattern in ("probe_shot*.png", "shot_*.png"):
        for path in screen.glob(pattern):
            path.unlink()
    # Always hand the editor back: a crashed run must not leave the lease pinned, or the next
    # agent is locked out until the TTL expires.
    reset_pending(bridge, token)
    try:
        bridge.release(token)
    except BridgeError as exc:
        print("    (lease release failed:", exc, ")")


if __name__ == "__main__":
    sys.exit(main())

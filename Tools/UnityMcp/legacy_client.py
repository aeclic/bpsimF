"""Small drop-in client for the legacy method names, backed by the MCP transport.

`Tools/dsh.py`, `Tools/Brand/ubridge.py` and `Tools/_bridge_build.py` used to POST
``{method, params}`` at ``http://127.0.0.1:8791/api`` — a port the old Unity-side plugin
picked for itself.  That plugin is no longer the transport, so those scripts would fail on a
stale port.  This module keeps their call shape (``{"ok": ..., "result": ...}``) while routing
through :class:`bridge.Bridge`, which talks to the Coplay "MCP for Unity" server.

Reads need no control lease.  Writes do, so :func:`write` acquires one for the duration of a
single call: these scripts are one-shot helpers, not long agent sessions.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from bridge import Bridge, BridgeError  # noqa: E402


DEFAULT_PROJECT = Path(__file__).resolve().parents[2]


def client(project=None, url=None):
    """Open a bridge for `project` (defaults to this checkout)."""
    return Bridge(project or DEFAULT_PROJECT, url=url)


def call(method, params=None, project=None, url=None):
    """Run a read-only legacy method and return ``{"ok": True, "result": ...}``."""
    bridge = client(project, url)
    try:
        return {"ok": True, "result": bridge.read(method, params or {})}
    except BridgeError as exc:
        return {"ok": False, "error": str(exc)}
    finally:
        bridge.close()


def write(method, params=None, project=None, url=None, agent="legacy-client", ttl=120):
    """Run a legacy editing method under a short-lived control lease."""
    bridge = client(project, url)
    token = None
    try:
        token = bridge.acquire(agent, f"legacy client: {method}", ttl)["token"]
        return {"ok": True, "result": bridge.write(method, token, params or {})}
    except BridgeError as exc:
        return {"ok": False, "error": str(exc)}
    finally:
        if token is not None:
            try:
                bridge.release(token)
            except BridgeError:
                pass
        bridge.close()


def status(project=None, url=None):
    """Editor status in the shape the old ``/status`` endpoint returned."""
    bridge = client(project, url)
    try:
        info = bridge.status()
        return {"ok": True, **{key: info.get(key) for key in
                               ("playing", "changing", "compiling", "transport", "server", "url")},
                "busy": info.get("editor_busy")}
    except BridgeError as exc:
        return {"ok": False, "error": str(exc)}
    finally:
        bridge.close()


def main(argv):
    """CLI mirroring the old helper scripts: status | logs | read <m> | write <m> [json]."""
    if not argv or argv[0] == "status":
        print(status())
        return 0
    command = argv[0]
    if command == "logs":
        count = int(argv[1]) if len(argv) > 1 else 40
        pattern = argv[2] if len(argv) > 2 else None
        reply = call("console", {"count": count})
        entries = reply.get("result") or []
        if isinstance(entries, dict):
            entries = entries.get("logs", [])
        if pattern:
            needle = pattern.lower()
            entries = [e for e in entries
                       if needle in ((e.get("message") or "") + (e.get("stack") or "")).lower()]
        for entry in entries[-count:]:
            text = (entry.get("message") or "")[:220].replace("\n", " ")
            print(f"[{entry.get('type')}] {text}")
        return 0
    method = argv[1] if command in {"read", "write"} and len(argv) > 1 else command
    params = argv[2] if len(argv) > 2 else (argv[1] if len(argv) > 1 and command not in {"read", "write"} else "{}")
    import json
    try:
        payload = json.loads(params) if params else {}
    except json.JSONDecodeError:
        payload = {}
    print((write if command == "write" else call)(method, payload))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))


"""Tiny Unity bridge client.

The transport is the project's "MCP for Unity" server; `legacy_client` finds it and keeps the
old ``{"ok": ..., "result": ...}`` reply shape, so the call sites below did not have to change.
"""
import json, os, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "UnityMcp")))
import legacy_client
from bridge import WRITE_METHODS


def call(method, params=None, timeout=120):
    """Run a legacy method; editing methods take a short control lease automatically."""
    run = legacy_client.write if method in WRITE_METHODS else legacy_client.call
    reply = run(method, params)
    if not reply.get("ok"):
        raise RuntimeError(reply.get("error") or "unity bridge error")
    return {"ok": True, "result": reply["result"]}


def status():
    return legacy_client.status()


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "status":
        s = status()
        print(json.dumps({k: s[k] for k in ("playing", "changing", "compiling", "busy") if k in s}))
    elif cmd == "logs":
        n = int(sys.argv[2]) if len(sys.argv) > 2 else 40
        pat = sys.argv[3] if len(sys.argv) > 3 else None
        res = call("console", {"count": n})
        logs = (res.get("result") or {})
        if isinstance(logs, dict):
            logs = logs.get("logs", [])
        if pat:
            logs = [l for l in logs if pat.lower() in ((l.get("message") or "") + (l.get("stack") or "")).lower()]
        for l in logs[-n:]:
            print("[%s] %s" % (l.get("type"), (l.get("message") or "")[:220].replace("\n", " ")))
    else:
        params = json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}
        print(json.dumps(call(cmd, params))[:1500])

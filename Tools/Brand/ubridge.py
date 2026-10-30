
"""Tiny Unity bridge client (port 8791 for this project)."""
import json, sys, urllib.request

BASE = "http://127.0.0.1:8791"


def call(method, params=None, timeout=120):
    data = json.dumps({"method": method, "params": params or {}}).encode("utf-8")
    req = urllib.request.Request(BASE + "/api", data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def status():
    with urllib.request.urlopen(BASE + "/status", timeout=10) as r:
        return json.loads(r.read().decode("utf-8"))


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

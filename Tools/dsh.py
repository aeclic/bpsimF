"""Bridge client for the bpsim-main editor (DshBridge on 127.0.0.1:8791).

Why the retry loop: every play-mode entry / script recompile does a domain
reload. DshBridge's static constructor starts a *new* TcpListener each time
while the previous accept thread can survive as a zombie, so the port ends up
with two listeners and roughly half of all connections land on the dead one and
never get an answer. Short per-attempt timeouts plus retries get through.
"""
import json
import sys
import time
import urllib.request

BASE = "http://127.0.0.1:8791/api"


def call(method, params=None, timeout=8):
    body = json.dumps({"method": method, "params": params or {}}).encode()
    req = urllib.request.Request(BASE, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode())


def retry(method, params=None, timeout=8, deadline=180, quiet=False):
    end = time.time() + deadline
    last = None
    attempt = 0
    while time.time() < end:
        attempt += 1
        try:
            out = call(method, params, timeout)
            if out.get("ok"):
                return out["result"]
            last = out
        except Exception as exc:  # noqa: BLE001
            last = type(exc).__name__ + ": " + str(exc)
        if not quiet:
            print("  .. attempt %d failed: %s" % (attempt, last), file=sys.stderr)
    raise RuntimeError("gave up on %s after %ds: %s" % (method, deadline, last))


def state():
    end = time.time() + 180
    while time.time() < end:
        try:
            return retry("playmode_state", {}, 8, 200, quiet=True)
        except Exception:  # noqa: BLE001
            time.sleep(3)
    raise RuntimeError("no playmode_state")


if __name__ == "__main__":
    cmd = sys.argv[1]
    arg = json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}
    if cmd == "enter":
        print(retry("playmode", {"action": "enter"}))
        for _ in range(40):
            st = state()
            if st["playing"] and not st["changing"]:
                print("PLAYING", st)
                break
            time.sleep(3)
    elif cmd == "exit":
        print(retry("playmode", {"action": "exit"}))
        for _ in range(40):
            st = state()
            if not st["playing"] and not st["changing"]:
                print("EDIT", st)
                break
            time.sleep(3)
    else:
        print(retry(cmd, arg))

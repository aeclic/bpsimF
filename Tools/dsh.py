"""Bridge client for the bpsim-main editor, using the legacy method names.

The transport is now the project's "MCP for Unity" server (the old Unity-side plugin on port
8791 is gone). ``legacy_client`` handles discovery and reply shapes, so this module keeps the
``call``/``retry``/``state`` helpers that the QA scripts import.

Why a retry loop is still worth keeping: entering or leaving Play Mode reloads the domain, and
the MCP session is dropped across that reload. Short per-attempt deadlines plus retries ride
over the transition instead of surfacing "session not found" to the caller.
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "UnityMcp"))

import legacy_client
from bridge import WRITE_METHODS


def call(method, params=None, timeout=8):
    run = legacy_client.write if method in WRITE_METHODS else legacy_client.call
    return run(method, params)


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

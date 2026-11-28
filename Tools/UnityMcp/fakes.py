"""Test doubles for the Unity MCP transport.

``Bridge`` talks to Unity through ``Bridge.adapter`` (a ``UnityMcpAdapter``).  Tests replace that
attribute with :class:`FakeAdapter`, so the lease/pending contract can be exercised without a
running editor and without any socket.
"""

from pathlib import Path


class FakeAdapter:
    """Stand-in for :class:`adapter.UnityMcpAdapter` that records what it was asked to do."""

    def __init__(self, project, info=None, result=None):
        self.project = Path(project)
        self._url = "http://127.0.0.1:8080/mcp"
        self.info = dict(info) if info is not None else {
            "ok": True, "project": str(self.project), "playing": False, "changing": False,
            "compiling": False, "busy": False, "mainThreadStalled": False,
        }
        self.result = {"done": True} if result is None else result
        self.calls = []
        self.pings = 0
        self.ping_failure = None
        self.dispatch_failure = None
        self.closed = False

    def ping(self):
        self.pings += 1
        if self.ping_failure is not None:
            raise self.ping_failure
        return dict(self.info)

    def dispatch(self, method, params):
        self.calls.append((method, dict(params or {})))
        if self.dispatch_failure is not None:
            raise self.dispatch_failure
        return dict(self.result) if isinstance(self.result, dict) else self.result

    def close(self):
        self.closed = True


def make_bridge(project, info=None, result=None):
    """Return a :class:`bridge.Bridge` whose adapter is a :class:`FakeAdapter`."""
    from bridge import Bridge

    bridge = Bridge(project)
    bridge.adapter = FakeAdapter(project, info=info, result=result)
    return bridge

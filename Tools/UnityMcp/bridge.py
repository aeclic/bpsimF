"""Project-pinned editor bridge: legacy DshBridge methods over the Unity MCP server.

The Unity-side plugin that used to answer on a loopback socket is gone; the same
30 method names now travel through the Coplay "MCP for Unity" server that this
project's ``com.coplaydev.unity-mcp`` package starts for the editor.  Everything
this module owns is still here and still authoritative:

* the cross-process control lease (``.runtime/control.json`` + ``control.lock``),
* the write quarantine used when a dispatch ends ambiguously,
* path/claim/material/static-invocation validation.

Only the transport below the dispatch point changed: ``UnityMcpAdapter`` maps each
legacy method onto an MCP tool (or an ``execute_code`` snippet when the typed tool
would change the legacy semantics).
"""

from contextlib import contextmanager
import json
import os
from pathlib import Path
import secrets
import threading
import time
from urllib.parse import urlsplit

from adapter import AdapterError, UnityMcpAdapter
from profiler import read_profiler_log, validate_profiler_params
from unity_mcp import discover_mcp_url


READ_METHODS = frozenset({
    "hierarchy", "find", "get_components", "get_property", "read_file",
    "list_dir", "compile_status", "console", "playmode_state", "profiler",
})
WRITE_METHODS = frozenset({
    "create_primitive", "create_empty", "destroy", "set_transform",
    "set_active", "add_component", "remove_component", "set_property",
    "write_script", "compile", "set_status", "playmode", "screenshot",
    "new_scene", "save_scene", "open_scene", "invoke_static", "create_material",
})

#: Paths a loopback bridge URL may carry. ``/mcp`` is the MCP streamable-HTTP endpoint.
_ALLOWED_PATHS = ("", "/", "/mcp")


class BridgeError(RuntimeError):
    pass


def discover_bridge_url(project):
    """Discover this project's MCP endpoint; ``ping`` still verifies identity."""
    return discover_mcp_url(project)


class Bridge:
    def __init__(self, project, url=None, runtime=None):
        self.project = Path(project).resolve()
        self.auto_discover = url is None
        url = discover_bridge_url(self.project) if self.auto_discover else url
        endpoint = urlsplit(url)
        if (endpoint.scheme != "http" or endpoint.hostname != "127.0.0.1"
                or endpoint.path not in _ALLOWED_PATHS or endpoint.query
                or endpoint.fragment or endpoint.username or endpoint.password):
            raise ValueError("Bridge URL must be http://127.0.0.1:<port>[/mcp] without a query")
        self.url = url.rstrip("/")
        # All processes for this project MUST use the same runtime directory.
        self.runtime = Path(runtime) if runtime else self.project / "Tools/UnityMcp/.runtime"
        self.runtime.mkdir(parents=True, exist_ok=True)
        self.state_path = self.runtime / "control.json"
        self.session = secrets.token_hex(16)
        self.thread_lock = threading.Lock()
        # Constructing the adapter opens no socket; the MCP handshake is lazy.
        self.adapter = UnityMcpAdapter(self.project, url=self.url)

    def close(self):
        self.adapter.close()

    @contextmanager
    def gate(self):
        if not self.thread_lock.acquire(timeout=3):
            raise BridgeError("Another MCP request is in flight; retry after it finishes")
        handle = None
        locked = False
        try:
            handle = (self.runtime / "control.lock").open("a+b")
            handle.seek(0, 2)
            if handle.tell() == 0:
                handle.write(b"0")
                handle.flush()
            deadline = time.monotonic() + 3
            while True:
                try:
                    handle.seek(0)
                    if os.name == "nt":
                        import msvcrt
                        msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                    else:
                        import fcntl
                        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                    locked = True
                    break
                except OSError:
                    if time.monotonic() >= deadline:
                        raise BridgeError("Another agent has a bridge request in flight; retry later")
                    time.sleep(0.05)
            yield
        finally:
            if handle:
                if locked:
                    handle.seek(0)
                    if os.name == "nt":
                        import msvcrt
                        msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
                    else:
                        import fcntl
                        fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
                handle.close()
            self.thread_lock.release()

    def load(self):
        if not self.state_path.exists():
            return {"lease": None, "pending": None}
        try:
            state = json.loads(self.state_path.read_text(encoding="utf-8"))
            if not isinstance(state, dict) or not {"lease", "pending"} <= state.keys():
                raise ValueError("invalid control state")
            return state
        except (ValueError, OSError) as exc:
            raise BridgeError("Control state is unreadable; do not bypass or delete it") from exc

    def store(self, state):
        temp = self.state_path.with_suffix(".tmp")
        with temp.open("w", encoding="utf-8") as handle:
            json.dump(state, handle, ensure_ascii=False)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temp, self.state_path)

    @staticmethod
    def public_state(state):
        lease = state.get("lease")
        return {
            "control": None if not lease else {
                "agent": lease["agent"], "purpose": lease["purpose"],
                "expires_at_unix": lease["expires"],
                "expired": lease["expires"] <= time.time(),
            },
            "pending": state.get("pending"),
        }

    def dispatch(self, method, params):
        """Run one legacy method through the adapter, normalizing its failures."""
        try:
            return self.adapter.dispatch(method, params)
        except AdapterError as exc:
            raise BridgeError(str(exc)) from exc

    def ping(self):
        if self.auto_discover:
            self.url = discover_bridge_url(self.project)
            self.adapter._url = self.url
        try:
            info = self.adapter.ping()
        except AdapterError as exc:
            raise BridgeError(
                f"Unity MCP is unavailable at {self.url}; open this project in Unity with the "
                f"MCP for Unity server running: {exc}") from exc
        actual = info.get("project")
        if not info.get("ok") or not actual:
            raise BridgeError("Bridge did not supply its project identity; refusing to dispatch")
        if Path(actual).resolve() != self.project:
            raise BridgeError(f"Wrong Unity project at {self.url}: {actual}; expected {self.project}")
        return info

    def status(self):
        info = self.ping()
        info["url"] = self.url
        info["editor_busy"] = self.is_busy(info)
        with self.gate():
            info.update(self.public_state(self.load()))
        return info

    @staticmethod
    def is_busy(info):
        # DshBridge 'changing' uses isPlayingOrWillChangePlaymode, which stays
        # true throughout Play Mode. Its raw 'busy' therefore also stays true.
        return bool(info.get("compiling") or info.get("mainThreadStalled")
                    or (info.get("changing") and not info.get("playing"))
                    or (info.get("busy") and not (info.get("playing") and info.get("changing"))))

    @staticmethod
    def ttl(seconds):
        if not 15 <= seconds <= 600:
            raise BridgeError("Lease TTL must be between 15 and 600 seconds")
        return seconds

    def acquire(self, agent, purpose, ttl_seconds=180):
        self.ttl(ttl_seconds)
        if not agent.strip() or not purpose.strip():
            raise BridgeError("Supply a distinct agent/chat label and a concrete purpose")
        self.ping()
        with self.gate():
            state = self.load()
            current = state["lease"]
            if current and current["expires"] > time.time():
                raise BridgeError(f"Editor controlled by {current['agent']}: {current['purpose']}")
            token = secrets.token_urlsafe(32)
            state["lease"] = {
                "agent": agent, "purpose": purpose, "token": token,
                "session": self.session, "expires": time.time() + ttl_seconds,
            }
            self.store(state)
            return {"token": token, **self.public_state(state)}

    def require_lease(self, state, token):
        lease = state.get("lease")
        if (not lease or not secrets.compare_digest(lease["token"], token)
                or lease["session"] != self.session or lease["expires"] <= time.time()):
            raise BridgeError("Acquire a valid control lease in this MCP session before editing Unity")
        return lease

    def renew(self, token, ttl_seconds=180):
        self.ttl(ttl_seconds)
        with self.gate():
            state = self.load()
            lease = self.require_lease(state, token)
            lease["expires"] = time.time() + ttl_seconds
            self.store(state)
            return self.public_state(state)

    def release(self, token):
        with self.gate():
            state = self.load()
            self.require_lease(state, token)
            if state["pending"]:
                raise BridgeError("A write has an uncertain outcome; inspect Unity and acknowledge it first")
            state["lease"] = None
            self.store(state)
            return {"released": True}

    def project_path(self, value):
        if not isinstance(value, str) or "\\" in value or ":" in value:
            raise BridgeError("Use a relative project path with forward slashes")
        path = (self.project / value).resolve()
        if Path(value).is_absolute() or not path.is_relative_to(self.project):
            raise BridgeError("Path escapes the configured Unity project")
        return path

    def check_claims(self, path):
        for candidate in (path, *path.parents):
            if candidate == self.project.parent:
                break
            marker = Path(str(candidate) + ".dshclaim")
            if marker.exists():
                raise BridgeError(f"Existing agent file claim: {marker}; coordinate with its owner first")

    def validate(self, method, params):
        params = dict(params or {})
        if method == "profiler":
            try:
                return validate_profiler_params(params)
            except ValueError as exc:
                raise BridgeError(str(exc)) from exc
        if method in {"read_file", "list_dir", "write_script", "save_scene", "open_scene"}:
            value = params.get("path", "Assets" if method == "list_dir" else "")
            if method == "save_scene" and not value:
                # Legacy defaulted to Assets/Scenes/Game.unity, which silently forks the active
                # scene into a second asset. Saving in place is what an empty path should mean.
                return params
            path = self.project_path(value)
            if method in {"write_script", "save_scene", "open_scene"}:
                if not value.startswith("Assets/"):
                    raise BridgeError("Use an explicit path below Assets/")
                suffix = ".cs" if method == "write_script" else ".unity"
                if path.suffix.lower() != suffix:
                    raise BridgeError(f"This command requires a {suffix} path")
                self.check_claims(path)
            params["path"] = value
        if method == "create_material":
            name = params.get("name", "Mat")
            if not isinstance(name, str) or not name or any(c in name for c in '/\\:<>"|?*') or ".." in name:
                raise BridgeError("Material name must be a simple filename without an extension")
            self.check_claims(self.project_path(f"Assets/Materials/{name}.mat"))
        if method == "console" and params.get("clear"):
            raise BridgeError("Clearing other agents' console logs is not exposed")
        if method == "invoke_static" and not str(params.get("type", "")).startswith("Bpsim."):
            raise BridgeError("Static invocation is limited to this project's Bpsim.* types")
        return params

    def read(self, method, params=None):
        if method not in READ_METHODS:
            raise BridgeError("Method is not a supported query")
        params = self.validate(method, params)
        with self.gate():
            info = self.ping()
            if self.is_busy(info):
                raise BridgeError("Unity is busy/compiling/stalled; wait and query status before retrying")
            return self.dispatch(method, params)

    def profiler(self, count=10, after_sequence=0, session_id="", source="auto"):
        if source not in {"auto", "live", "log"}:
            raise BridgeError("Profiler source must be auto, live or log")
        params = self.validate("profiler", {"count": count, "afterSequence": after_sequence, "sessionId": session_id})
        live_error = None
        if source != "log":
            try:
                result = self.read("profiler", params)
                return {"method": "profiler", "source": "live", "result": result}
            except BridgeError as exc:
                if source == "live":
                    raise
                live_error = str(exc)
        try:
            result = read_profiler_log(self.project, params)
        except (OSError, ValueError) as exc:
            raise BridgeError(str(exc)) from exc
        return {"method": "profiler", "source": "log", "liveError": live_error, "result": result}

    def write(self, method, token, params=None):
        if method not in WRITE_METHODS:
            raise BridgeError("Method is not a supported editing command; quit is intentionally disabled")
        params = self.validate(method, params)
        with self.gate():
            state = self.load()
            lease = self.require_lease(state, token)
            if state["pending"]:
                raise BridgeError("Previous write outcome is uncertain; inspect and acknowledge before editing")
            info = self.ping()
            if self.is_busy(info):
                raise BridgeError("Unity is busy/compiling/stalled; no write was dispatched")
            # Check markers again while serialized, just before dispatch.
            params = self.validate(method, params)
            state["pending"] = {"method": method, "agent": lease["agent"], "started_at_unix": time.time()}
            self.store(state)
            try:
                result = self.dispatch(method, params)
            except Exception as exc:
                state["pending"]["error"] = str(exc)
                self.store(state)
                raise BridgeError(
                    "Write outcome uncertain; NOT retried. Inspect editor/console, then use "
                    f"unity_acknowledge_uncertain before proceeding: {exc}") from exc
            state["pending"] = None
            self.store(state)
            return result

    def acknowledge(self, token, inspection, confirmed=False):
        if not confirmed or not inspection.strip():
            raise BridgeError("Explicitly confirm inspection of Unity and describe the observed outcome")
        with self.gate():
            state = self.load()
            self.require_lease(state, token)
            if not state["pending"]:
                raise BridgeError("No uncertain write to acknowledge")
            if self.is_busy(self.ping()):
                raise BridgeError("Unity is still busy; do not acknowledge until it is stable")
            previous = state["pending"]
            state["pending"] = None
            state["last_acknowledgement"] = {"request": previous, "inspection": inspection, "at": time.time()}
            self.store(state)
            return {"acknowledged": previous, "inspection": inspection}

"""Minimal stdlib MCP client for the Coplay "MCP for Unity" server (Streamable HTTP).

The server is started by the Unity package itself (Window > MCP For Unity) and listens on a
loopback port, by default ``http://127.0.0.1:8080/mcp``.  This module speaks just enough of
MCP 2025-06-18 to run ``tools/list``, ``tools/call`` and ``resources/read`` with no third-party
dependencies, so the DshBridge compatibility layer can live inside a plain virtualenv.

Design notes
------------
* One :class:`UnityMcp` instance owns one MCP session (``mcp-session-id`` header) and one
  ``initialize`` handshake, performed lazily on first use.
* Responses may arrive as ``application/json`` or as a ``text/event-stream``; both are handled.
* A dropped/expired session is detected and the handshake is retried once, transparently.
* The socket is never reused across processes, so every CLI invocation is self-contained.
"""

from __future__ import annotations

import json
import os
import re
import socket
import threading
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

PROTOCOL_VERSION = "2025-06-18"
DEFAULT_PORT = 8080
DEFAULT_TIMEOUT = 120.0
_INIT_TIMEOUT = 30.0

#: Errors the server returns when the session is gone; they are safe to retry after a handshake.
_SESSION_LOST = ("session", "not found", "404", "400", "missing", "expired")


class UnityMcpError(RuntimeError):
    """Any failure talking to the Unity MCP server."""


# --------------------------------------------------------------------------------------------
# endpoint discovery


def _pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    if os.name == "nt":
        import ctypes

        process = ctypes.windll.kernel32.OpenProcess(0x1000, False, pid)  # QUERY_LIMITED_INFORMATION
        if not process:
            return False
        try:
            code = ctypes.c_ulong()
            if ctypes.windll.kernel32.GetExitCodeProcess(process, ctypes.byref(code)) == 0:
                return False
            return code.value == 259  # STILL_ACTIVE
        finally:
            ctypes.windll.kernel32.CloseHandle(process)
    try:
        os.kill(pid, 0)
    except OSError:
        return False
    return True


def _run_state_dirs(project: Path) -> list[Path]:
    return [project / "Library" / "MCPForUnity" / "RunState"]


def discover_endpoints(project: str | os.PathLike[str]) -> list[str]:
    """Return candidate ``/mcp`` URLs for *project*, most recently launched first.

    The Unity package writes ``mcp_http_<port>.pid`` next to a live server; the file name carries
    the port and the content carries the process id.  Stale pid files are ignored so a crashed
    server never shadows a fresh one.
    """
    project = Path(project).resolve()
    found: list[tuple[float, str]] = []
    for directory in _run_state_dirs(project):
        if not directory.is_dir():
            continue
        for pidfile in directory.glob("mcp_http_*.pid"):
            match = re.fullmatch(r"mcp_http_(\d{1,5})\.pid", pidfile.name)
            if not match:
                continue
            port = int(match.group(1))
            if not 1 <= port <= 65535:
                continue
            try:
                raw = pidfile.read_text(encoding="utf-8", errors="replace").strip()
                pid = int(raw.split()[0]) if raw else 0
            except (OSError, ValueError):
                pid = 0
            if pid and not _pid_alive(pid):
                continue
            try:
                stamp = pidfile.stat().st_mtime
            except OSError:
                stamp = 0.0
            found.append((stamp, f"http://127.0.0.1:{port}/mcp"))
    found.sort(reverse=True)
    return [url for _, url in found]


def discover_mcp_url(project: str | os.PathLike[str]) -> str:
    """First live endpoint for *project*, else the package default."""
    candidates = discover_endpoints(project)
    return candidates[0] if candidates else f"http://127.0.0.1:{DEFAULT_PORT}/mcp"


# --------------------------------------------------------------------------------------------
# transport helpers


def _parse_body(raw: str, content_type: str) -> dict[str, Any]:
    """Decode a JSON or text/event-stream response body into the single JSON-RPC reply."""
    if "event-stream" not in (content_type or "").lower():
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            pass
    messages: list[dict[str, Any]] = []
    data: list[str] = []
    for line in raw.splitlines() + [""]:
        if not line.strip():
            if data:
                chunk = "\n".join(data)
                data.clear()
                try:
                    messages.append(json.loads(chunk))
                except json.JSONDecodeError:
                    continue
        elif line.startswith("data:"):
            data.append(line[5:].lstrip())
    for message in reversed(messages):
        if isinstance(message, dict) and ("result" in message or "error" in message):
            return message
    if messages:
        return messages[-1]
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        raise UnityMcpError(f"Unreadable MCP response from {content_type or 'server'}: {raw[:400]}")


def _error_text(message: Any) -> str:
    """Flatten whatever the server put in an error slot into one readable line."""
    if isinstance(message, dict):
        error = message.get("error")
        if isinstance(error, dict):
            return str(error.get("message") or error)
        if error is not None:
            return str(error)
        return str(message.get("message") or message)
    if isinstance(message, (list, tuple)):
        return "; ".join(_error_text(item) for item in message)
    return str(message)


# --------------------------------------------------------------------------------------------
# client


class UnityMcp:
    """A single MCP session against the Unity bridge server."""

    def __init__(self, url: str | None = None, project: str | os.PathLike[str] | None = None,
                 timeout: float = DEFAULT_TIMEOUT) -> None:
        self.project = Path(project).resolve() if project else None
        self.url = url or (discover_mcp_url(self.project) if self.project
                           else f"http://127.0.0.1:{DEFAULT_PORT}/mcp")
        self.timeout = float(timeout)
        self.session_id: str | None = None
        self.server_info: dict[str, Any] = {}
        self._lock = threading.Lock()
        self._ready = False
        self._opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    # -- low level ---------------------------------------------------------------------------
    def _post(self, payload: dict[str, Any], timeout: float, notify: bool = False) -> dict[str, Any]:
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if self.session_id:
            headers["mcp-session-id"] = self.session_id
        request = urllib.request.Request(
            self.url, data=json.dumps(payload).encode("utf-8"), headers=headers, method="POST")
        try:
            with self._opener.open(request, timeout=timeout) as response:
                session = response.headers.get("mcp-session-id")
                if session:
                    self.session_id = session
                raw = response.read().decode("utf-8", "replace")
                content_type = response.headers.get("content-type", "")
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", "replace")[:800] if exc.fp else ""
            raise UnityMcpError(f"MCP HTTP {exc.code} from {self.url}: {detail or exc.reason}") from exc
        except (urllib.error.URLError, socket.timeout, TimeoutError) as exc:
            raise UnityMcpError(f"Cannot reach the Unity MCP server at {self.url}: {exc}") from exc
        if notify or not raw.strip():
            return {}
        message = _parse_body(raw, content_type)
        if isinstance(message, dict) and message.get("error"):
            raise UnityMcpError(_error_text(message))
        return message

    def _rpc(self, method: str, params: dict[str, Any] | None = None,
             timeout: float | None = None) -> dict[str, Any]:
        payload: dict[str, Any] = {"jsonrpc": "2.0", "id": 1, "method": method}
        if params is not None:
            payload["params"] = params
        message = self._post(payload, timeout or self.timeout)
        result = message.get("result")
        return result if isinstance(result, dict) else {"result": result}

    # -- handshake ---------------------------------------------------------------------------
    def _handshake(self) -> None:
        self.session_id = None
        reply = self._rpc("initialize", {
            "protocolVersion": PROTOCOL_VERSION,
            "capabilities": {},
            "clientInfo": {"name": "dsh-bridge", "version": "2.0"},
        }, timeout=_INIT_TIMEOUT)
        info = reply.get("serverInfo") or {}
        self.server_info = dict(self.server_info)
        self.server_info.update({"name": info.get("name"), "version": info.get("version"),
                                 "protocolVersion": reply.get("protocolVersion")})
        self._post({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}},
                   timeout=_INIT_TIMEOUT, notify=True)
        self._ready = True

    def _ensure(self) -> None:
        with self._lock:
            if not self._ready:
                self._handshake()

    def connect(self) -> "UnityMcp":
        """Perform the handshake now (optional) and return ``self``."""
        self._ensure()
        tools = self._rpc("tools/list", {})
        names = [tool.get("name") for tool in tools.get("tools", [])]
        self.server_info.update({"url": self.url, "tools": len(names)})
        return self

    def close(self) -> None:
        if not self.session_id:
            self._ready = False
            return
        try:
            self._post({"jsonrpc": "2.0", "method": "notifications/cancelled", "params": {}},
                       timeout=5.0, notify=True)
        except UnityMcpError:
            pass
        self.session_id = None
        self._ready = False

    # -- public API --------------------------------------------------------------------------
    def _call_once(self, name: str, arguments: dict[str, Any], timeout: float) -> dict[str, Any]:
        return self._rpc("tools/call", {"name": name, "arguments": arguments}, timeout=timeout)

    def call(self, name: str, arguments: dict[str, Any] | None = None,
             timeout: float | None = None) -> Any:
        """Run one MCP tool and return its decoded payload.

        Coplay tools answer with a single ``content[0].text`` JSON document; it is decoded when
        possible and returned verbatim otherwise.
        """
        self._ensure()
        arguments = dict(arguments or {})
        timeout = timeout or self.timeout
        try:
            reply = self._call_once(name, arguments, timeout)
        except UnityMcpError as exc:
            if not any(hint in str(exc).lower() for hint in _SESSION_LOST) or self.session_id is None:
                raise
            self._handshake()
            reply = self._call_once(name, arguments, timeout)
        return self._unwrap(name, reply)

    def _unwrap(self, name: str, reply: dict[str, Any]) -> Any:
        if reply.get("error"):
            raise UnityMcpError(_error_text(reply))
        content = reply.get("content") or []
        texts = [part.get("text", "") for part in content
                 if isinstance(part, dict) and part.get("type") == "text"]
        structured = reply.get("structuredContent")
        if structured is not None:
            payload: Any = structured
        elif texts:
            joined = "\n".join(texts)
            try:
                payload = json.loads(joined)
            except json.JSONDecodeError:
                payload = {"text": joined}
        else:
            payload = {}
        if reply.get("isError"):
            detail = None
            if isinstance(payload, dict):
                detail = payload.get("error") or payload.get("message")
            raise UnityMcpError(f"{name} failed: {_error_text(detail or payload)}")
        if isinstance(payload, dict) and payload.get("success") is False:
            detail = payload.get("error") or payload.get("message") or payload.get("data")
            extra = payload.get("data")
            if isinstance(extra, dict):
                # execute_code reports compiler diagnostics under data.errors; without them a
                # "Compilation failed" is untraceable.
                problems = extra.get("errors") or extra.get("exceptionType")
                if problems:
                    detail = f"{detail} ({_error_text(problems)})" if detail else _error_text(problems)
            raise UnityMcpError(f"{name} failed: {_error_text(detail)}")
        return payload

    def list_tools(self) -> list[str]:
        self._ensure()
        return [tool.get("name", "") for tool in self._rpc("tools/list", {}).get("tools", [])]

    def read_resource(self, uri: str, timeout: float | None = None) -> str:
        self._ensure()
        result = self._rpc("resources/read", {"uri": uri}, timeout=timeout or self.timeout)
        chunks: list[str] = []
        for item in result.get("contents") or []:
            if isinstance(item, dict):
                chunks.append(item.get("text") or "")
        return "".join(chunks)

    # -- context manager ---------------------------------------------------------------------
    def __enter__(self) -> "UnityMcp":
        self._ensure()
        return self

    def __exit__(self, *_exc: object) -> None:
        self.close()


def main() -> int:
    """``py unity_mcp.py [url] tools|calls|read <uri>|call <tool> <json>``"""
    import sys

    argv = sys.argv[1:]
    url = None
    if argv and argv[0].startswith("http"):
        url = argv.pop(0)
    what = argv[0] if argv else "tools"
    with UnityMcp(url) as client:
        if what == "tools":
            for name in sorted(client.list_tools()):
                print(name)
        elif what == "read":
            print(client.read_resource(argv[1]))
        elif what == "call":
            arguments = json.loads(argv[2]) if len(argv) > 2 else {}
            print(json.dumps(client.call(argv[1], arguments), ensure_ascii=False, indent=1))
        else:
            raise SystemExit(f"unknown command: {what}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

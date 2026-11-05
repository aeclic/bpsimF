"""MCP stdio adapter. stdout is reserved exclusively for MCP messages."""

import argparse
from pathlib import Path
from typing import Any, Literal

from mcp.server.fastmcp import FastMCP
from mcp.types import ToolAnnotations

from bridge import Bridge


parser = argparse.ArgumentParser()
parser.add_argument("--project", default=str(Path(__file__).resolve().parents[2]))
parser.add_argument("--bridge-url", default="http://127.0.0.1:8791")
args = parser.parse_args()
bridge = Bridge(args.project, args.bridge_url)
mcp = FastMCP("bpsim-unity")
query = ToolAnnotations(readOnlyHint=True, destructiveHint=False, openWorldHint=False)
control = ToolAnnotations(readOnlyHint=False, destructiveHint=False, openWorldHint=False)
editing = ToolAnnotations(readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False)


@mcp.tool(annotations=query)
def unity_status() -> dict[str, Any]:
    """Check the pinned project's Unity connection, Play Mode, compilation and current control owner."""
    return bridge.status()


@mcp.tool(annotations=query)
def unity_read(
    method: Literal["hierarchy", "find", "get_components", "get_property", "read_file",
                    "list_dir", "compile_status", "console", "playmode_state"],
    params: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Query Unity without taking control. See unity://bpsim/guide for parameter examples.

    find: {name}; get_components: {target}; get_property: {target,component,path};
    read_file/list_dir: {path}; console/compile_status: {count}; hierarchy/playmode_state: {}.
    Never clear console logs. Busy requests fail promptly; retry queries only when Unity is stable.
    """
    return {"method": method, "result": bridge.read(method, params)}


@mcp.tool(annotations=control)
def unity_acquire_control(agent: str, purpose: str, ttl_seconds: int = 180) -> dict[str, Any]:
    """Acquire the project-wide exclusive editor lease. Use a unique chat label, not 'agent'.

    Keep the returned token private to this MCP session, renew before expiry, and release when done.
    Other agents may still query. Terminal file edits and legacy HTTP clients bypass this lease.
    """
    return bridge.acquire(agent, purpose, ttl_seconds)


@mcp.tool(annotations=control)
def unity_renew_control(token: str, ttl_seconds: int = 180) -> dict[str, Any]:
    """Renew your session's editing lease, TTL 15..600 seconds. Cannot renew an expired lease."""
    return bridge.renew(token, ttl_seconds)


@mcp.tool(annotations=control)
def unity_release_control(token: str) -> dict[str, Any]:
    """Release control immediately after the task; uncertain writes must be inspected first."""
    return bridge.release(token)


@mcp.tool(annotations=editing)
def unity_write(
    method: Literal["create_primitive", "create_empty", "destroy", "set_transform", "set_active",
                    "add_component", "remove_component", "set_property", "write_script", "compile",
                    "set_status", "playmode", "screenshot", "new_scene", "save_scene", "open_scene",
                    "invoke_static", "create_material"],
    token: str,
    params: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Edit Unity only with your exclusive lease. See unity://bpsim/guide for parameters.

    Play Mode, scenes, compilation, screenshots and static test/build invocation all need control.
    Scene commands can discard unsaved edits: coordinate first. invoke_static only accepts Bpsim.*.
    Explicit project-local paths and existing .dshclaim markers are enforced for file-writing tools.
    Writes are never retried automatically: on ambiguous failure inspect Unity before acknowledgement.
    """
    return {"method": method, "result": bridge.write(method, token, params)}


@mcp.tool(annotations=editing)
def unity_acknowledge_uncertain(token: str, inspection: str, confirmed: bool = False) -> dict[str, Any]:
    """After an uncertain write, explicitly confirm inspecting Unity/console and its resulting state.

    Do not use just to clear a lock. A timed-out command may still execute in Unity.
    If the original client crashed, wait for lease expiry, acquire control, inspect, then acknowledge.
    """
    return bridge.acknowledge(token, inspection, confirmed)


@mcp.resource("unity://bpsim/guide")
def guide() -> str:
    """Installation, method parameters and multi-agent coordination guide."""
    return Path(__file__).with_name("README.md").read_text(encoding="utf-8")


if __name__ == "__main__":
    mcp.run(transport="stdio")

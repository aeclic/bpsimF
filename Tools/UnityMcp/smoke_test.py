"""Real MCP handshake + Unity read-only smoke test; no editor mutations."""

import asyncio
from contextlib import AsyncExitStack
import json
from pathlib import Path
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def decode(result):
    if result.isError:
        raise RuntimeError(str(result.content))
    texts = [part.text for part in result.content if part.type == "text"]
    return json.loads("\n".join(texts))


async def main():
    root = Path(__file__).resolve().parents[2]
    parameters = StdioServerParameters(
        command=sys.executable,
        args=[str(Path(__file__).with_name("server.py")), "--project", str(root)],
        cwd=str(root),
        env={"PYTHONUTF8": "1"},
    )
    async with AsyncExitStack() as stack:
        sessions = []
        for _ in range(2):
            read, write = await stack.enter_async_context(stdio_client(parameters))
            session = await stack.enter_async_context(ClientSession(read, write))
            await session.initialize()
            sessions.append(session)
        first, second = sessions
        tools = await first.list_tools()
        names = {tool.name for tool in tools.tools}
        assert len(names) == 7, names
        resources = await first.list_resources()
        assert any(str(r.uri) == "unity://bpsim/guide" for r in resources.resources)
        resource = await first.read_resource("unity://bpsim/guide")
        assert resource.contents
        status = decode(await first.call_tool("unity_status", {}))
        assert Path(status["project"]).resolve() == root
        version = decode(await first.call_tool("unity_read", {
            "method": "read_file", "params": {"path": "ProjectSettings/ProjectVersion.txt"}}))["result"]
        assert "2022.2.23f1c1" in version
        console = decode(await first.call_tool("unity_read", {"method": "console", "params": {"count": 3}}))["result"]
        acquired = decode(await first.call_tool("unity_acquire_control", {
            "agent": "mcp-install-smoke-test", "purpose": "Read-only connection and lease validation", "ttl_seconds": 30}))
        token = acquired["token"]
        try:
            conflict = await second.call_tool("unity_acquire_control", {
                "agent": "mcp-install-smoke-test-second", "purpose": "Expect lease conflict", "ttl_seconds": 30})
            assert conflict.isError, "Second MCP session unexpectedly acquired control"
            denied = await second.call_tool("unity_write", {
                "method": "compile", "token": token, "params": {}})
            assert denied.isError, "Second session unexpectedly borrowed a token"
            decode(await first.call_tool("unity_renew_control", {"token": token, "ttl_seconds": 30}))
        finally:
            decode(await first.call_tool("unity_release_control", {"token": token}))
        status_after = decode(await second.call_tool("unity_status", {}))
        assert status_after["control"] is None
        print(json.dumps({
            "ok": True, "tools": sorted(names), "resource": "unity://bpsim/guide",
            "unity": status["unity"], "project": status["project"],
            "console_entries_read": len(console), "two_session_exclusion": "passed",
            "cross_session_token_rejected": "passed", "lease_released": True,
            "unity_write_commands_dispatched": 0,
        }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    asyncio.run(main())

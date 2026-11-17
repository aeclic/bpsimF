"""Read-only real MCP smoke test. Does not compile, enter Play Mode, or take a lease."""

import argparse
import asyncio
import json
from pathlib import Path
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def decode(response):
    for item in response.content:
        if getattr(item, "type", None) == "text":
            result = json.loads(item.text)
            if response.isError:
                raise AssertionError(result)
            return result
    raise AssertionError("Missing MCP text result")


async def main(root, output):
    params = StdioServerParameters(command=sys.executable,
        args=[str(root / "Tools/UnityMcp/server.py"), "--project", str(root)], cwd=str(root))
    checks = []
    def check(name, condition):
        checks.append({"name": name, "pass": bool(condition)})
        assert condition, name
    async with stdio_client(params) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            tools = await session.list_tools()
            tool = next(item for item in tools.tools if item.name == "unity_profiler")
            check("profiler tool is annotated read-only", tool.annotations.readOnlyHint is True)
            identity = decode(await session.call_tool("unity_status", {}))
            check("auto-discovered Editor identity matches the pinned project", Path(identity["project"]).resolve() == root)
            first = decode(await session.call_tool("unity_profiler", {"source": "live", "count": 2}))
            check("live query returns an Editor sample", first["source"] == "live" and first["result"]["latest"] is not None)
            before = first["result"]
            second = None
            for _ in range(6):
                await asyncio.sleep(1.1)
                second = decode(await session.call_tool("unity_profiler", {"source": "live", "count": 2,
                    "session_id": before["sessionId"], "after_sequence": before["nextSequence"]}))
                if second["result"]["latestSequence"] > before["latestSequence"]:
                    break
            live = second["result"]
            check("session-scoped cursor advances", live["sessionId"] == before["sessionId"] and
                  live["latestSequence"] > before["latestSequence"] and not live["cursorReset"])
            metrics = live["latest"]["metrics"]
            check("CPU timing recorder returns observed data", metrics["mainThreadMs"]["available"] and
                  metrics["mainThreadMs"]["statistics"]["sampleCount"] > 0)
            check("unobserved markers are not reported as zero-cost", all(
                metric["statistics"]["mean"] is None for metric in metrics.values()
                if not metric["available"] or metric.get("state") == "empty-window"))
            log = None
            for _ in range(6):
                log = decode(await session.call_tool("unity_profiler", {"source": "log", "count": 120}))["result"]
                if log["sessionId"] == live["sessionId"] and log["latestSequence"] >= live["latestSequence"]:
                    break
                await asyncio.sleep(.2)
            check("persisted sample matches the live session/cursor", log["sessionId"] == live["sessionId"] and
                  any(sample["sequence"] == live["latest"]["sequence"] and sample == live["latest"] for sample in log["samples"]))
            check("log query explicitly reports persisted freshness", log["freshness"] == "offline" and log["sampleAgeSeconds"] is not None)
            files = list((root / "Logs/Profiler").glob("editor-*.jsonl"))
            check("persisted logs respect file count/byte bounds", len(files) <= 4 and all(path.stat().st_size <= 4 * 1024 * 1024 for path in files))
            for arguments in [{"count": 0}, {"count": True}, {"after_sequence": -1}, {"source": "write"}]:
                rejected = await session.call_tool("unity_profiler", arguments)
                check("MCP rejects invalid input " + json.dumps(arguments), rejected.isError)
            report = {"schema": 1, "pass": len(checks), "fail": 0, "checks": checks,
                      "project": str(root), "port": identity["port"], "sessionId": live["sessionId"],
                      "sequence": live["latestSequence"], "context": live["latest"]["context"],
                      "collectionAndEnqueueMs": live["collectionAndEnqueueMs"],
                      "metrics": metrics, "log": live["log"]}
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes((json.dumps(report, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
            print(json.dumps({"pass": report["pass"], "fail": 0, "port": report["port"],
                              "sequence": report["sequence"], "report": str(output)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = args.project.resolve()
    asyncio.run(main(root, args.output or root / "Logs/editor-profiler-validation.json"))

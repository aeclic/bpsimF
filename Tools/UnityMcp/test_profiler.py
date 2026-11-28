from datetime import datetime, timezone
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from adapter import AdapterError
from bridge import Bridge, BridgeError, discover_bridge_url
from fakes import make_bridge
from profiler import MAX_SAMPLES, MAX_TAIL_BYTES, read_profiler_log, select_samples, validate_profiler_params


class ProfilerTests(unittest.TestCase):
    def setUp(self):
        scratch = Path(__file__).parent / ".runtime"
        scratch.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temp.cleanup)
        self.project = Path(self.temp.name).resolve()
        self.logs = self.project / "Logs/Profiler"
        self.logs.mkdir(parents=True)

    def sample(self, sequence, session="20261106T120000-session"):
        return {"schema": 1, "type": "editor_profiler_sample", "editorOnly": True,
                "sessionId": session, "sequence": sequence, "utc": datetime.now(timezone.utc).isoformat(),
                "context": {"mode": "edit"}, "metrics": {"unsupported": {"available": False, "last": None}}}

    def write(self, samples, session="20261106T120000-session", segment=0, suffix=b""):
        path = self.logs / f"editor-{session}-{segment:06d}.jsonl"
        header = {"schema": 1, "type": "editor_profiler_session", "sessionId": session}
        data = "\n".join(json.dumps(record) for record in [header, *samples]) + "\n"
        path.write_bytes(data.encode("utf-8") + suffix)
        return path

    def client(self, busy=False):
        client = make_bridge(self.project, info={
            "ok": True, "project": str(self.project), "compiling": busy,
            "playing": False, "changing": False, "busy": False,
        }, result={"latestSequence": 1})
        self.addCleanup(client.close)
        return client

    def test_default_read_is_recent_and_offline(self):
        self.write([self.sample(i) for i in range(1, 21)])
        result = read_profiler_log(self.project)
        self.assertEqual([s["sequence"] for s in result["samples"]], list(range(11, 21)))
        self.assertEqual(result["freshness"], "offline")
        self.assertIsNone(result["enabled"])
        self.assertGreaterEqual(result["sampleAgeSeconds"], 0)
        self.assertIsNone(result["latest"]["metrics"]["unsupported"]["last"])

    def test_incremental_read_pages_without_skipping(self):
        self.write([self.sample(i) for i in range(1, 21)])
        params = {"count": 3, "afterSequence": 5, "sessionId": "20261106T120000-session"}
        first = read_profiler_log(self.project, params)
        self.assertEqual([s["sequence"] for s in first["samples"]], [6, 7, 8])
        self.assertTrue(first["hasMore"])
        params["afterSequence"] = first["nextSequence"]
        self.assertEqual(read_profiler_log(self.project, params)["samples"][0]["sequence"], 9)

    def test_session_change_and_future_cursor_reset(self):
        self.write([self.sample(1)])
        for params in [{"afterSequence": 400, "sessionId": "old"}, {"afterSequence": 400}]:
            result = read_profiler_log(self.project, params)
            self.assertTrue(result["cursorReset"])
            self.assertEqual(result["nextSequence"], 1)

    def test_capacity_and_truncation_are_visible(self):
        self.write([self.sample(i) for i in range(1, 151)])
        result = read_profiler_log(self.project, {"count": MAX_SAMPLES, "afterSequence": 1})
        self.assertEqual(len(result["samples"]), MAX_SAMPLES)
        self.assertEqual(result["oldestSequence"], 31)
        self.assertTrue(result["historyTruncated"])

    def test_rotated_segments_merge_and_deduplicate(self):
        self.write([self.sample(1), self.sample(2)])
        self.write([self.sample(2), self.sample(3)], segment=1)
        result = read_profiler_log(self.project)
        self.assertEqual([s["sequence"] for s in result["samples"]], [1, 2, 3])

    def test_new_session_header_does_not_return_previous_session(self):
        self.write([self.sample(5)])
        self.write([], session="20261106T130000-new")
        result = read_profiler_log(self.project)
        self.assertEqual(result["sessionId"], "20261106T130000-new")
        self.assertIsNone(result["latest"])

    def test_explicit_retained_session_reads_history_after_domain_reload(self):
        old = "20261106T120000-old"
        latest = "20261106T130000-new"
        self.write([self.sample(1, session=old)], session=old)
        self.write([self.sample(1, session=latest)], session=latest)
        default = read_profiler_log(self.project)
        self.assertEqual(default["sessionId"], latest)
        previous = read_profiler_log(self.project, {"sessionId": old})
        self.assertEqual(previous["sessionId"], old)
        self.assertEqual(previous["latestSequence"], 1)
        self.assertEqual(len(previous["log"]["retainedSessions"]), 2)

    def test_incomplete_utf8_and_json_tail_are_ignored(self):
        self.write([self.sample(1)], suffix=b'{"partial":"\xe4\xb8')
        self.assertEqual(read_profiler_log(self.project)["latestSequence"], 1)

    def test_corrupt_and_nonfinite_lines_are_ignored(self):
        path = self.write([self.sample(1)])
        with path.open("ab") as stream:
            stream.write(b'not-json\n{"bad":NaN}\n\xff\n')
        self.assertEqual(read_profiler_log(self.project)["latestSequence"], 1)

    def test_invalid_metadata_cannot_become_a_sample(self):
        records = [self.sample(1)]
        for patch in [{"editorOnly": False}, {"sessionId": "wrong"}, {"sequence": True},
                      {"sequence": -5}, {"utc": "not-a-date"}, {"utc": "2026-11-06"},
                      {"type": "some-other-log"}, {"schema": 2}, {"schema": True}, {"schema": 1.0}]:
            record = self.sample(999)
            record.update(patch)
            records.append(record)
        self.write(records)
        self.assertEqual(read_profiler_log(self.project)["latestSequence"], 1)

    def test_tail_read_is_bounded(self):
        path = self.write([])
        path.write_bytes(b"x" * (MAX_TAIL_BYTES + 100) + b"\n" +
                         json.dumps(self.sample(7)).encode("utf-8") + b"\n")
        self.assertEqual(read_profiler_log(self.project)["latestSequence"], 7)

    def test_no_more_than_four_files_are_read(self):
        for part in range(6):
            self.write([self.sample(part + 1)], segment=part)
        result = read_profiler_log(self.project)
        self.assertEqual(result["log"]["filesRead"], 4)
        self.assertEqual(result["oldestSequence"], 3)

    def test_unrelated_files_are_ignored(self):
        (self.logs / "user.jsonl").write_text(json.dumps(self.sample(99)) + "\n", encoding="utf-8")
        self.assertFalse(read_profiler_log(self.project)["logAvailable"])

    def test_missing_logs_are_read_only_and_explicitly_unavailable(self):
        root = self.project / "no-project-yet"
        result = read_profiler_log(root)
        self.assertEqual(result["freshness"], "unavailable")
        self.assertFalse(root.exists())

    def test_escaping_log_symlink_is_rejected(self):
        outside = self.project.parent / (self.project.name + "-outside")
        outside.mkdir()
        self.addCleanup(outside.rmdir)
        self.logs.rmdir()
        try:
            self.logs.symlink_to(outside, target_is_directory=True)
        except OSError as exc:
            self.skipTest("Host does not permit directory symlinks: " + str(exc))
        with self.assertRaisesRegex(ValueError, "escapes"):
            read_profiler_log(self.project)

    def test_invalid_query_negative_controls(self):
        for params in [{"count": 0}, {"count": 121}, {"count": True}, {"count": 1.5},
                       {"afterSequence": -1}, {"afterSequence": True}, {"afterSequence": 2**53},
                       {"sessionId": "../escape"}, {"sessionId": None}, {"sessionId": "x" * 97},
                       {"clear": True}, {"enabled": False}, {"path": "../outside"}]:
            with self.subTest(params=params), self.assertRaises(ValueError):
                validate_profiler_params(params)

    def test_live_query_requires_no_lease_and_uses_read_method(self):
        client = self.client()
        result = client.profiler(source="live")
        self.assertEqual(result["source"], "live")
        self.assertEqual(client.adapter.calls[-1][0], "profiler")
        self.assertIsNone(client.load()["lease"])
        self.assertIsNone(client.load()["pending"])

    def test_live_query_works_during_another_session_lease(self):
        owner = self.client()
        token = owner.acquire("owner-chat", "test")['token']
        reader = self.client()
        self.assertEqual(reader.profiler(source="live")["source"], "live")
        owner.release(token)

    def test_busy_and_unavailable_editors_fall_back_to_logs(self):
        self.write([self.sample(3)])
        for client in [self.client(busy=True), self.client()]:
            if not client.ping()["compiling"]:
                client.adapter.ping_failure = AdapterError("offline")
            result = client.profiler()
            self.assertEqual(result["source"], "log")
            self.assertEqual(result["result"]["latestSequence"], 3)
            self.assertTrue(result["liveError"])

    def test_explicit_log_source_never_contacts_unity(self):
        client = self.client()
        client.profiler(source="log")
        self.assertEqual(client.adapter.calls, [])

    def test_explicit_live_does_not_hide_connection_failure(self):
        client = self.client()
        client.adapter.ping_failure = AdapterError("offline")
        with self.assertRaises(BridgeError):
            client.profiler(source="live")

    def test_wrong_project_is_not_queried(self):
        client = self.client()
        client.adapter.info["project"] = str(self.project.parent)
        with self.assertRaisesRegex(BridgeError, "Wrong Unity project"):
            client.profiler(source="live")
        self.assertEqual(client.adapter.calls, [])

    def test_profiler_is_not_a_write_method(self):
        client = self.client()
        with self.assertRaises(BridgeError):
            client.write("profiler", "not-a-token", {"clear": True})
        self.assertEqual(client.adapter.calls, [])

    def test_source_and_bridge_query_bounds_are_validated(self):
        client = self.client()
        for kwargs in [{"source": "write"}, {"count": 0}, {"after_sequence": -1}]:
            with self.assertRaises(BridgeError):
                client.profiler(**kwargs)
        self.assertEqual(client.adapter.calls, [])

    def pidfile(self, port=8080, pid=None):
        path = self.project / "Library/MCPForUnity/RunState" / f"mcp_http_{port}.pid"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(str(os.getpid() if pid is None else pid), encoding="utf-8")
        return path

    def test_auto_discovery_and_explicit_override(self):
        self.pidfile(port=8080)
        self.assertEqual(discover_bridge_url(self.project), "http://127.0.0.1:8080/mcp")
        self.assertEqual(Bridge(self.project, "http://127.0.0.1:8999").url, "http://127.0.0.1:8999")

    def test_discovery_falls_back_to_the_default_port(self):
        self.assertEqual(discover_bridge_url(self.project), "http://127.0.0.1:8080/mcp")

    def test_discovery_refreshes_after_editor_port_change(self):
        self.pidfile(port=8090)
        client = self.client()
        self.pidfile(port=8091)
        client.ping()
        self.assertEqual(client.url, "http://127.0.0.1:8091/mcp")

    def test_discovery_ignores_stale_and_malformed_pid_files(self):
        for name in ["mcp_http_0.pid", "mcp_http_65536.pid", "mcp_http_abc.pid", "mcp_http_42.pid.txt"]:
            path = self.project / "Library/MCPForUnity/RunState" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("1234", encoding="utf-8")
        self.assertEqual(discover_bridge_url(self.project), "http://127.0.0.1:8080/mcp")

    def test_discovery_skips_pid_files_whose_server_exited(self):
        self.pidfile(port=8095, pid=999999)
        with mock.patch("unity_mcp._pid_alive", return_value=False):
            self.assertEqual(discover_bridge_url(self.project), "http://127.0.0.1:8080/mcp")
        with mock.patch("unity_mcp._pid_alive", return_value=True):
            self.assertEqual(discover_bridge_url(self.project), "http://127.0.0.1:8095/mcp")

    def test_discovery_prefers_the_newest_pid_file(self):
        self.pidfile(port=8100)
        newest = self.pidfile(port=8101)
        stamp = newest.stat().st_mtime + 60
        os.utime(newest, (stamp, stamp))
        self.assertEqual(discover_bridge_url(self.project), "http://127.0.0.1:8101/mcp")

    def test_csharp_sources_are_editor_only(self):
        root = Path(__file__).resolve().parents[2]
        files = list((root / "Assets/Editor/BpsimProfiler").glob("*.cs"))
        self.assertGreaterEqual(len(files), 3)
        for path in files:
            source = path.read_text(encoding="utf-8").strip()
            self.assertTrue(source.startswith("#if UNITY_EDITOR"), path.name)
            self.assertTrue(source.endswith("#endif"), path.name)


if __name__ == "__main__":
    unittest.main()

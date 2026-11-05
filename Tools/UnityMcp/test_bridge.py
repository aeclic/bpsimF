import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock

from bridge import Bridge, BridgeError


class BridgeTests(unittest.TestCase):
    def setUp(self):
        scratch = Path(__file__).parent / ".runtime"
        scratch.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temp.cleanup)
        self.project = Path(self.temp.name).resolve()
        self.bridge = self.make_bridge()

    def make_bridge(self):
        result = Bridge(self.project)
        info = {"ok": True, "project": str(self.project), "playing": False,
                "changing": False, "compiling": False, "busy": False, "mainThreadStalled": False}
        result.http_json = Mock(side_effect=lambda path, body=None, timeout=8:
                                dict(info) if path == "/ping" else {"ok": True, "result": {"done": True}})
        return result

    def acquire(self, bridge=None, ttl_seconds=180):
        bridge = bridge or self.bridge
        return bridge.acquire("test-chat", "unit test", ttl_seconds)["token"]

    def test_status_hides_token(self):
        token = self.acquire()
        self.assertNotIn(token, json.dumps(self.bridge.status()))

    def test_read_needs_no_control(self):
        self.assertEqual(self.bridge.read("console", {"count": 2}), {"done": True})

    def test_write_requires_control_before_dispatch(self):
        with self.assertRaises(BridgeError):
            self.bridge.write("compile", "wrong")
        self.bridge.http_json.assert_not_called()

    def test_success_clears_pending_and_release(self):
        token = self.acquire()
        self.assertEqual(self.bridge.write("compile", token), {"done": True})
        self.assertIsNone(self.bridge.load()["pending"])
        self.assertTrue(self.bridge.release(token)["released"])

    def test_another_session_cannot_acquire_or_borrow_token(self):
        token = self.acquire()
        second = self.make_bridge()
        with self.assertRaises(BridgeError):
            second.acquire("second-chat", "competing")
        with self.assertRaises(BridgeError):
            second.write("compile", token)
        with self.assertRaises(BridgeError):
            second.release(token)

    def test_expired_lease_can_be_replaced(self):
        old_token = self.acquire()
        state = self.bridge.load()
        state["lease"]["expires"] = time.time() - 1
        self.bridge.store(state)
        second = self.make_bridge()
        new_token = self.acquire(second)
        self.assertNotEqual(old_token, new_token)
        with self.assertRaises(BridgeError):
            self.bridge.renew(old_token)
        second.release(new_token)

    def test_renew_updates_expiration(self):
        token = self.acquire(ttl_seconds=15)
        before = self.bridge.load()["lease"]["expires"]
        self.bridge.renew(token, 600)
        self.assertGreater(self.bridge.load()["lease"]["expires"], before + 500)

    def test_invalid_ttl_and_empty_label(self):
        for seconds in [0, 14, 601]:
            with self.assertRaises(BridgeError):
                self.acquire(ttl_seconds=seconds)
        with self.assertRaises(BridgeError):
            self.bridge.acquire("", "purpose")

    def test_wrong_project_is_rejected(self):
        self.bridge.http_json.return_value = {"ok": True, "project": str(self.project.parent)}
        self.bridge.http_json.side_effect = None
        with self.assertRaisesRegex(BridgeError, "Wrong Unity project"):
            self.bridge.read("hierarchy")
        self.assertEqual(self.bridge.http_json.call_count, 1)

    def test_missing_identity_is_rejected(self):
        self.bridge.http_json.side_effect = None
        self.bridge.http_json.return_value = {"ok": True}
        with self.assertRaises(BridgeError):
            self.bridge.status()

    def test_only_loopback_allowed(self):
        for url in ["http://localhost:8791", "https://127.0.0.1:8791", "http://example.com",
                    "http://127.0.0.1:8791/api", "http://user@127.0.0.1:8791"]:
            with self.assertRaises(ValueError):
                Bridge(self.project, url)

    def test_path_escapes_and_unclaimed_extensions(self):
        for path in ["../outside.cs", "D:/outside.cs", "Assets/../../outside.cs", "Assets\\outside.cs"]:
            with self.assertRaises(BridgeError):
                self.bridge.validate("read_file", {"path": path})
        for method, path in [("write_script", "Tools/file.cs"), ("write_script", "Assets/file.dll"),
                             ("save_scene", ""), ("save_scene", "Assets/file.cs")]:
            with self.assertRaises(BridgeError):
                self.bridge.validate(method, {"path": path})

    def test_file_and_parent_directory_claims(self):
        assets = self.project / "Assets"
        assets.mkdir()
        for marker in [assets / "Example.cs.dshclaim", self.project / "Assets.dshclaim"]:
            marker.write_text("another agent", encoding="utf-8")
            with self.assertRaisesRegex(BridgeError, "file claim"):
                self.bridge.validate("write_script", {"path": "Assets/Example.cs"})
            marker.unlink()
        self.bridge.validate("write_script", {"path": "Assets/Example.cs"})

    def test_material_paths_and_static_invocation_restricted(self):
        for name in ["../escape", "A/B", "A\\B", "C:foo", ""]:
            with self.assertRaises(BridgeError):
                self.bridge.validate("create_material", {"name": name})
        with self.assertRaises(BridgeError):
            self.bridge.validate("invoke_static", {"type": "System.IO.File"})
        self.bridge.validate("invoke_static", {"type": "Bpsim.QA.Example"})

    def test_quit_and_read_writes_disabled(self):
        with self.assertRaises(BridgeError):
            self.bridge.write("quit", "anything")
        with self.assertRaises(BridgeError):
            self.bridge.read("compile")
        with self.assertRaises(BridgeError):
            self.bridge.read("console", {"clear": True})

    def test_playmode_busy_flag_normalization(self):
        self.assertFalse(Bridge.is_busy({"playing": True, "changing": True, "busy": True}))
        self.assertTrue(Bridge.is_busy({"playing": True, "changing": True, "busy": True, "compiling": True}))
        self.assertTrue(Bridge.is_busy({"playing": True, "changing": True, "mainThreadStalled": True}))
        self.assertTrue(Bridge.is_busy({"playing": False, "changing": True}))
        self.assertTrue(Bridge.is_busy({"busy": True}))

    def test_busy_editor_never_dispatches_write(self):
        token = self.acquire()
        self.bridge.http_json.side_effect = None
        self.bridge.http_json.return_value = {"ok": True, "project": str(self.project), "compiling": True}
        self.bridge.http_json.reset_mock()
        with self.assertRaisesRegex(BridgeError, "no write was dispatched"):
            self.bridge.write("compile", token)
        self.assertEqual(self.bridge.http_json.call_count, 1)
        self.assertIsNone(self.bridge.load()["pending"])

    def test_ambiguous_failure_not_retried_blocks_release_and_writes(self):
        token = self.acquire()
        ping = self.bridge.ping()
        self.bridge.http_json.side_effect = [ping, TimeoutError("simulated lost response")]
        with self.assertRaisesRegex(BridgeError, "NOT retried"):
            self.bridge.write("compile", token)
        pending = self.bridge.load()["pending"]
        self.assertEqual(pending["method"], "compile")
        with self.assertRaises(BridgeError):
            self.bridge.write("compile", token)
        with self.assertRaises(BridgeError):
            self.bridge.release(token)
        self.bridge.http_json.side_effect = lambda *args, **kwargs: ping
        with self.assertRaises(BridgeError):
            self.bridge.acknowledge(token, "not actually confirmed")
        self.bridge.acknowledge(token, "Checked editor and logs; compilation finished", True)
        self.assertIsNone(self.bridge.load()["pending"])
        self.bridge.release(token)

    def test_bridge_error_response_is_also_quarantined(self):
        token = self.acquire()
        ping = self.bridge.ping()
        self.bridge.http_json.side_effect = [ping, {"ok": False, "error": "editor main thread timeout"}]
        with self.assertRaises(BridgeError):
            self.bridge.write("compile", token)
        self.assertIsNotNone(self.bridge.load()["pending"])

    def test_corrupt_state_fails_closed(self):
        self.bridge.state_path.write_text("broken", encoding="utf-8")
        with self.assertRaises(BridgeError):
            self.bridge.load()

    def test_cross_process_lease_is_shared(self):
        self.acquire()
        child = """
from bridge import Bridge, BridgeError
import sys
b = Bridge(sys.argv[1])
b.ping = lambda: {'ok': True}
try:
    b.acquire('subprocess', 'competing')
except BridgeError:
    sys.exit(0)
sys.exit(1)
"""
        result = subprocess.run([sys.executable, "-c", child, str(self.project)],
                                cwd=Path(__file__).parent, capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_cross_process_gate_rejects_concurrent_dispatch(self):
        child = """
from bridge import Bridge, BridgeError
import sys
b = Bridge(sys.argv[1])
try:
    with b.gate():
        sys.exit(1)
except BridgeError:
    sys.exit(0)
"""
        with self.bridge.gate():
            result = subprocess.run([sys.executable, "-c", child, str(self.project)],
                                    cwd=Path(__file__).parent, capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()

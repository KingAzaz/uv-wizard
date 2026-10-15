"""Tests for unity_tests.py and unity_bridge.py that need no Unity: python -m unittest discover tools"""

import io
import json
import os
import socket
import struct
import sys
import threading
import time
import unittest
from contextlib import redirect_stdout

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_bridge as bridge  # noqa: E402
import unity_tests  # noqa: E402


def succeeded(total, passed, failed=0):
    return {"status": "succeeded", "error": None,
            "result": {"summary": {"total": total, "passed": passed, "failed": failed, "skipped": 0,
                                   "durationSeconds": 1.0}}}


def failed_job(failures, capped=False):
    return {"status": "failed", "error": None, "result": None,
            "progress": {"failures_so_far": failures, "failures_capped": capped}}


class ReportTest(unittest.TestCase):
    def report(self, job):
        out = io.StringIO()
        with redirect_stdout(out):
            try:
                unity_tests.report(job)
                return "green", out.getvalue()
            except unity_tests.Failed:
                return "red", out.getvalue()
            except RuntimeError:
                return "broken", out.getvalue()

    def test_all_passed_is_green(self):
        self.assertEqual(self.report(succeeded(5, 5))[0], "green")

    def test_a_failing_test_is_red_and_named(self):
        outcome, out = self.report(failed_job([{"full_name": "Ns.Class.Test", "message": "Expected: True"}]))
        self.assertEqual(outcome, "red")
        self.assertIn("FAIL Ns.Class.Test", out)
        self.assertIn("Expected: True", out)

    def test_capped_failures_say_so(self):
        outcome, out = self.report(failed_job([{"full_name": "A", "message": ""}], capped=True))
        self.assertEqual(outcome, "red")
        self.assertIn("only the first 1 failures", out)

    def test_a_failed_job_with_an_error_is_broken_not_red(self):
        job = {"status": "failed", "error": "Job orphaned after domain reload"}
        self.assertEqual(self.report(job)[0], "broken")

    def test_no_tests_run_is_red(self):
        self.assertEqual(self.report(succeeded(0, 0))[0], "red")

    def test_a_failed_count_in_a_succeeded_summary_is_red(self):
        self.assertEqual(self.report(succeeded(3, 2, failed=1))[0], "red")


class EditorStateTest(unittest.TestCase):
    IDLE = {"activity": {"phase": "idle"},
            "compilation": {"is_compiling": False, "is_domain_reload_pending": False},
            "assets": {"is_updating": False, "refresh": {"is_refresh_in_progress": False}}}

    def test_idle(self):
        self.assertTrue(unity_tests.is_idle(self.IDLE))

    def test_each_busy_signal_is_not_idle(self):
        for path, value in [(("activity", "phase"), "compiling"),
                            (("compilation", "is_compiling"), True),
                            (("compilation", "is_domain_reload_pending"), True),
                            (("assets", "is_updating"), True)]:
            state = json.loads(json.dumps(self.IDLE))
            state[path[0]][path[1]] = value
            self.assertFalse(unity_tests.is_idle(state), path)

    def test_a_refresh_in_progress_is_not_idle(self):
        state = json.loads(json.dumps(self.IDLE))
        state["assets"]["refresh"]["is_refresh_in_progress"] = True
        self.assertFalse(unity_tests.is_idle(state))

    def test_a_compile_or_reload_after_the_refresh_counts(self):
        for key in ("last_compile_finished_unix_ms", "last_domain_reload_after_unix_ms"):
            self.assertTrue(unity_tests.compile_finished_since({"compilation": {key: 2000}}, 1000), key)

    def test_one_before_the_refresh_does_not(self):
        state = {"compilation": {"last_compile_finished_unix_ms": 500, "last_domain_reload_after_unix_ms": None}}
        self.assertFalse(unity_tests.compile_finished_since(state, 1000))


class FakeBridge:
    """A bridge on a free port: the real handshake and framing, and each new client closes the others."""

    def __init__(self, handler):
        self.handler = handler
        self.server = socket.socket()
        self.server.bind(("127.0.0.1", 0))
        self.server.listen()
        self.port = self.server.getsockname()[1]
        self.clients = []
        self.requests = []
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try:
                client, _ = self.server.accept()
            except OSError:
                return
            for stale in self.clients:
                stale.close()
            self.clients = [client]
            threading.Thread(target=self.talk, args=(client,), daemon=True).start()

    def talk(self, client):
        try:
            client.sendall(bridge.HANDSHAKE)
            (length,) = struct.unpack(">Q", bridge._read_exact(client, 8))
            request = json.loads(bridge._read_exact(client, length))
            self.requests.append(request)
            reply = self.handler(request, len(self.requests))
            if reply is None:
                client.close()
                return
            payload = json.dumps(reply).encode()
            client.sendall(struct.pack(">Q", len(payload)) + payload)
        except OSError:
            pass

    def close(self):
        self.server.close()


class BridgeTest(unittest.TestCase):
    def test_a_request_goes_out_framed_and_the_reply_comes_back(self):
        fake = FakeBridge(lambda request, n: {"status": "success", "result": {"success": True, "data": {"echo": request}}})
        self.addCleanup(fake.close)
        data = bridge.result_of(bridge.call(fake.port, "get_editor_state", {"x": 1}))
        self.assertEqual(data["echo"], {"type": "get_editor_state", "params": {"x": 1}})

    def test_a_dropped_connection_is_retried(self):
        fake = FakeBridge(lambda request, n: None if n < 3 else {"status": "success", "result": {"ok": n}})
        self.addCleanup(fake.close)
        self.assertEqual(bridge.result_of(bridge.call(fake.port, "ping")), {"ok": 3})

    def test_a_bridge_that_stays_down_raises_after_the_deadline(self):
        port = FakeBridge(lambda request, n: None)
        port.close()
        started = time.monotonic()
        with self.assertRaises(bridge.BridgeError):
            bridge.call(port.port, "ping", deadline=time.monotonic() + 1)
        self.assertLess(time.monotonic() - started, 5)

    def test_an_error_reply_raises(self):
        for reply in ({"status": "error", "error": "boom"},
                      {"status": "success", "result": {"success": False, "message": "boom"}}):
            with self.assertRaisesRegex(bridge.BridgeError, "boom"):
                bridge.result_of(reply)


class FindPortTest(unittest.TestCase):
    def test_the_named_projects_status_file_wins(self):
        import tempfile
        with tempfile.TemporaryDirectory() as tmp:
            for name, port in (("other", 6401), ("uv-wizard", 6402)):
                with open(os.path.join(tmp, "unity-mcp-status-%s.json" % name), "w") as f:
                    json.dump({"project_name": name, "unity_port": port}, f)
            os.environ["UNITY_MCP_STATUS_DIR"] = tmp
            self.addCleanup(os.environ.pop, "UNITY_MCP_STATUS_DIR")
            self.assertEqual(bridge.find_port("uv-wizard"), 6402)
            self.assertEqual(bridge.find_port("missing"), 6400)


if __name__ == "__main__":
    unittest.main()

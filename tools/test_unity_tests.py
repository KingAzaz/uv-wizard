"""Tests for unity_tests.py and unity_bridge.py that need no Unity: python -m unittest discover tools"""

import io
import json
import os
import re
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import unittest.mock
from contextlib import redirect_stdout

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_bridge as bridge  # noqa: E402
import unity_tests  # noqa: E402


def temp_dir(test):
    """A folder deleted when the test ends."""
    folder = tempfile.TemporaryDirectory()
    test.addCleanup(folder.cleanup)
    return folder.name


def succeeded(total, passed, failed=0, results=None):
    return {"status": "succeeded", "error": None,
            "result": {"summary": {"total": total, "passed": passed, "failed": failed, "skipped": 0,
                                   "durationSeconds": 1.0},
                       "results": results}}


def failed_job(failures, capped=False, results=None):
    return {"status": "failed", "error": None,
            "result": None if results is None else {"summary": {}, "results": results},
            "progress": {"failures_so_far": failures, "failures_capped": capped}}


def inconclusive(full_name, message="Expected: True"):
    """A test result as MCP for Unity v10.3.0 lists it: TestRunnerService.cs's
    TestRunTestResult, whose state is NUnit's ResultState.ToString()."""
    return {"name": full_name.split(".")[-1], "fullName": full_name, "state": "Inconclusive",
            "durationSeconds": 0.0, "message": message, "stackTrace": "", "output": ""}


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

    def test_a_failure_message_prints_on_a_cp1252_console(self):
        # AAO's reports put arrows in NDMF failure messages; Windows consoles default to cp1252.
        raw = io.BytesIO()
        console = io.TextIOWrapper(raw, encoding="cp1252")
        with unittest.mock.patch.object(sys, "stdout", console), \
                unittest.mock.patch.object(sys, "stderr", console):
            unity_tests.utf8_streams()
            with self.assertRaises(unity_tests.Failed):
                unity_tests.report(failed_job([{"full_name": "A", "message": "Body → Merged"}]))
            sys.stdout.flush()
        self.assertIn("Body → Merged".encode("utf-8"), raw.getvalue())

    def test_the_failure_count_is_the_number_of_failures(self):
        two = [{"full_name": "A", "message": ""}, {"full_name": "B", "message": ""}]
        for job, count in [(failed_job(two), "2 test(s) failed"),
                           (failed_job([]), "1 test(s) failed"),
                           (failed_job(two, capped=True), "2 or more tests failed"),
                           (succeeded(5, 2, failed=3), "3 test(s) failed")]:
            with redirect_stdout(io.StringIO()), self.assertRaisesRegex(unity_tests.Failed, re.escape(count)):
                unity_tests.report(job)

    def test_a_failed_job_with_an_error_is_broken_not_red(self):
        job = {"status": "failed", "error": "Job orphaned after domain reload"}
        self.assertEqual(self.report(job)[0], "broken")

    def test_no_tests_run_is_red(self):
        self.assertEqual(self.report(succeeded(0, 0))[0], "red")

    def test_a_failed_count_in_a_succeeded_summary_is_red(self):
        self.assertEqual(self.report(succeeded(3, 2, failed=1))[0], "red")

    # The bridge marks a run with Inconclusive tests "succeeded", and its summary
    # leaves them out of every count, total included: only the result list names them.
    def test_an_inconclusive_test_is_red_and_named_with_its_message(self):
        job = succeeded(2, 2, results=[inconclusive("Ns.Class.Test", "Expected: 4\n  But was: 3")])
        outcome, out = self.report(job)
        self.assertEqual(outcome, "red")
        self.assertIn("INCONCLUSIVE Ns.Class.Test\n  Expected: 4\n    But was: 3", out)

    def test_the_summary_line_counts_the_inconclusive_tests(self):
        job = succeeded(2, 2, results=[inconclusive("A"), inconclusive("B")])
        with redirect_stdout(io.StringIO()) as out, \
                self.assertRaisesRegex(unity_tests.Failed, re.escape("2 test(s) inconclusive")):
            unity_tests.report(job)
        self.assertIn("total 4, passed 2, failed 0, inconclusive 2, skipped 0", out.getvalue())

    def test_a_run_of_only_inconclusive_tests_says_so_not_that_none_ran(self):
        with redirect_stdout(io.StringIO()), \
                self.assertRaisesRegex(unity_tests.Failed, re.escape("1 test(s) inconclusive")):
            unity_tests.report(succeeded(0, 0, results=[inconclusive("A")]))

    def test_skipped_and_ignored_results_are_not_inconclusive(self):
        results = [dict(inconclusive("A"), state=state) for state in ("Skipped", "Skipped:Ignored", "Passed")]
        outcome, out = self.report(succeeded(3, 1, results=results))
        self.assertEqual(outcome, "green")
        self.assertIn("inconclusive 0", out)

    def test_a_failed_job_lists_its_inconclusive_tests_too(self):
        job = failed_job([{"full_name": "F", "message": ""}], results=[inconclusive("Ns.Class.Maybe")])
        outcome, out = self.report(job)
        self.assertEqual(outcome, "red")
        self.assertIn("FAIL F", out)
        self.assertIn("INCONCLUSIVE Ns.Class.Maybe", out)

    def test_a_failed_jobs_reason_counts_its_inconclusive_tests(self):
        job = failed_job([{"full_name": "F", "message": ""}], results=[inconclusive("A"), inconclusive("B")])
        with redirect_stdout(io.StringIO()), \
                self.assertRaisesRegex(unity_tests.Failed, re.escape("1 test(s) failed, 2 inconclusive")):
            unity_tests.report(job)


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



# The bridge's greeting, from MCP for Unity v10.3.0's StdioBridgeHost.cs, kept
# literal here so a wrong constant in unity_bridge fails these tests.
WELCOME = b"WELCOME UNITY-MCP 1 FRAMING=1\n"


def ok(data):
    return {"status": "success", "result": {"success": True, "data": data}}


class FakeBridge:
    """A bridge on a free port: the real handshake and framing, and each new client closes the others.

    handler(request, n) returns the reply to the n-th request, or None to drop
    the connection after reading it.
    """

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
            client.sendall(WELCOME)
            (length,) = struct.unpack(">Q", bridge.read_exact(client, 8))
            request = json.loads(bridge.read_exact(client, length))
            self.requests.append(request)
            reply = self.handler(request, len(self.requests))
            if reply is None:
                return
            payload = json.dumps(reply).encode()
            client.sendall(struct.pack(">Q", len(payload)) + payload)
        except OSError:
            pass
        finally:
            client.close()

    def sent(self, kind):
        return [r for r in self.requests if r["type"] == kind]

    def close(self):
        self.server.close()


class FakeEditor:
    """Answers the requests unity_tests sends, the way the v10.3.0 bridge does.

    A fast job finishes before the next editor-state snapshot: the snapshot
    then shows no current job, only the run's start and finish times
    (TestJobManager.cs clears the job id when the job ends).
    """

    IDLE = {"activity": {"phase": "idle"}, "compilation": {}, "assets": {}, "tests": {}}

    def __init__(self, reload_after_refresh=True, compile_failed=False, job=None,
                 drop_run_tests=0, busy_since=None, fast=False, forget_jobs=False, run_tests_error=None,
                 compiled_at=None, blind_for=0, drop_every_other_state=False, reloading_for=0):
        self.reload_after_refresh = reload_after_refresh
        self.compile_failed = compile_failed
        self.job = job if job is not None else {"status": "succeeded", "error": None, "result": {
            "summary": {"total": 2, "passed": 2, "failed": 0, "skipped": 0, "durationSeconds": 0.1}}}
        self.drop_run_tests = drop_run_tests   # how many run_tests replies to lose
        self.busy_since = busy_since   # a run another client started, in unix ms
        self.fast = fast
        self.forget_jobs = forget_jobs   # get_test_job answers "Unknown job_id."
        self.run_tests_error = run_tests_error
        self.compiled_at = compiled_at   # a compile that finished before the refresh, in unix ms
        self.blind_for = blind_for   # seconds after run_tests that snapshot requests are dropped
        self.drop_every_other_state = drop_every_other_state   # a main thread too busy to answer every poll
        self.states = 0
        self.reloading_for = reloading_for   # seconds after the refresh that the bridge is down for a reload
        self.refreshed_at = None
        self.reloaded_at = None
        self.started_at = None
        self.jobs = 0
        self.running = False

    def __call__(self, request, n):
        kind = request["type"]
        if kind == "refresh_unity":
            self.refreshed_at = time.time()
            if self.reload_after_refresh:
                self.reloaded_at = int(time.time() * 1000) + 50 + int(self.reloading_for * 1000)
            return ok({"refresh_triggered": True})
        if kind == "get_editor_state":
            if self.started_at and time.time() * 1000 < self.started_at + self.blind_for * 1000:
                return None
            if self.refreshed_at and time.time() < self.refreshed_at + self.reloading_for:
                return None
            self.states += 1
            if self.drop_every_other_state and self.states % 2 == 0:
                return None
            state = json.loads(json.dumps(self.IDLE))
            if self.compiled_at:
                state["compilation"]["last_compile_finished_unix_ms"] = self.compiled_at
            if self.reloaded_at and time.time() * 1000 >= self.reloaded_at:
                state["compilation"]["last_domain_reload_after_unix_ms"] = self.reloaded_at
            if self.busy_since:
                state["tests"] = {"current_job_id": "theirs", "started_unix_ms": self.busy_since}
            elif self.started_at:
                state["tests"] = {"current_job_id": "job-%d" % self.jobs if self.running else None,
                                  "started_unix_ms": self.started_at,
                                  "last_run": None if self.running else {"finished_unix_ms": self.started_at + 1}}
            return ok(state)
        if kind == "execute_code":
            return ok({"result": self.compile_failed})
        if kind == "run_tests":
            if self.run_tests_error:
                return {"status": "success", "result": {"success": False, "error": self.run_tests_error}}
            if self.busy_since or self.running:
                return {"status": "success", "result": {"success": False, "error": "tests_running",
                                                        "data": {"reason": "tests_running", "retry_after_ms": 5000}}}
            self.jobs += 1
            self.started_at = int(time.time() * 1000)
            self.running = not self.fast
            if self.jobs <= self.drop_run_tests:
                return None
            return ok({"job_id": "job-%d" % self.jobs, "status": "running"})
        if kind == "get_test_job":
            if self.forget_jobs:
                return {"status": "success", "result": {"success": False, "error": "Unknown job_id."}}
            return ok(dict(self.job, job_id=request["params"]["job_id"]))
        return {"status": "error", "error": "unknown command " + kind}


class BridgeTest(unittest.TestCase):
    def test_a_request_goes_out_framed_and_the_reply_comes_back(self):
        fake = FakeBridge(lambda request, n: ok({"echo": request}))
        self.addCleanup(fake.close)
        data = bridge.result_of(bridge.call(fake.port, "get_editor_state", {"x": 1}))
        self.assertEqual(data["echo"], {"type": "get_editor_state", "params": {"x": 1}})

    def test_a_dropped_connection_is_retried(self):
        fake = FakeBridge(lambda request, n: None if n < 3 else {"status": "success", "result": {"ok": n}})
        self.addCleanup(fake.close)
        self.assertEqual(bridge.result_of(bridge.call(fake.port, "ping")), {"ok": 3})

    def test_a_request_unsafe_to_repeat_is_not_resent_after_it_went_out(self):
        fake = FakeBridge(lambda request, n: None)
        self.addCleanup(fake.close)
        with self.assertRaises(bridge.DroppedAfterSending):
            bridge.call(fake.port, "run_tests", resend=False)
        self.assertEqual(len(fake.requests), 1)

    def test_a_bridge_that_stays_down_raises_after_the_deadline(self):
        fake = FakeBridge(lambda request, n: None)
        fake.close()
        started = time.monotonic()
        with self.assertRaises(bridge.BridgeError):
            bridge.call(fake.port, "ping", deadline=time.monotonic() + 1)
        self.assertLess(time.monotonic() - started, 5)

    def test_an_error_reply_raises(self):
        for reply in ({"status": "error", "error": "boom"},
                      {"status": "success", "result": {"success": False, "message": "boom"}}):
            with self.assertRaisesRegex(bridge.BridgeError, "boom"):
                bridge.result_of(reply)


class EditorTest(unittest.TestCase):
    def editor(self, **kwargs):
        editor = FakeEditor(**kwargs)
        fake = FakeBridge(editor)
        self.addCleanup(fake.close)
        return editor, fake

    def test_a_refresh_waits_for_the_reload(self):
        editor, fake = self.editor()
        unity_tests.refresh(fake.port, time.monotonic() + 30, poll=0.05)
        self.assertEqual(len(fake.sent("refresh_unity")), 1)

    def test_an_idle_editor_with_no_reload_is_not_taken_as_compiled(self):
        editor, fake = self.editor(reload_after_refresh=False)
        with self.assertRaises(TimeoutError):
            unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=1, poll=0.05)

    def test_a_compile_after_the_newest_source_counts_once_the_editor_settles(self):
        # A focused editor compiled the edit itself, and the requested compile never comes (#229).
        compiled = int(time.time() * 1000) - 5000
        editor, fake = self.editor(reload_after_refresh=False, compiled_at=compiled)
        unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=5, poll=0.05, settle=0.3,
                            source_ms=compiled - 1000)

    def test_a_compile_before_the_newest_source_does_not(self):
        compiled = int(time.time() * 1000) - 5000
        editor, fake = self.editor(reload_after_refresh=False, compiled_at=compiled)
        with self.assertRaises(TimeoutError):
            unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=1, poll=0.05, settle=0.3,
                                source_ms=compiled + 1000)

    def test_a_poll_that_times_out_does_not_restart_the_settle_window(self):
        # The #201 run's polls timed out about once every 15 s, and each reset the wait (#233).
        compiled = int(time.time() * 1000) - 5000
        editor, fake = self.editor(reload_after_refresh=False, compiled_at=compiled, drop_every_other_state=True)
        unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=5, poll=0.05, settle=0.3,
                            source_ms=compiled - 1000, poll_wait=0.05)

    def test_a_reload_longer_than_the_compile_wait_is_waited_out(self):
        # The #212 run's reloads took about 200 s against a 180 s wait, with the bridge down throughout (#237).
        editor, fake = self.editor(reloading_for=1.0)
        unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=0.5, poll=0.05, poll_wait=0.05)

    def test_a_reload_that_never_ends_stops_at_the_deadline(self):
        editor, fake = self.editor(reloading_for=60)
        with self.assertRaisesRegex(TimeoutError, "busy or unreachable until the deadline"):
            unity_tests.refresh(fake.port, time.monotonic() + 1.5, compile_wait=0.5, poll=0.05, poll_wait=0.05)

    def test_assemblies_on_disk_newer_than_their_sources_end_the_wait(self):
        # The editor answers idle but reports no compile at all, as after a domain reload it did itself (#233).
        package, dlls = assembly_tree(self, source_ages={"A": 60, "B": 60}, dll_ages={"A": 30, "B": 30})
        editor, fake = self.editor(reload_after_refresh=False)
        unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=5, poll=0.05, source_ms=now_ms(),
                            assemblies_dir=dlls, package_dir=package, disk_settle=0.3)

    def test_an_assembly_older_than_its_sources_holds_the_wait(self):
        package, dlls = assembly_tree(self, source_ages={"A": 60, "B": 10}, dll_ages={"A": 30, "B": 30})
        editor, fake = self.editor(reload_after_refresh=False)
        with self.assertRaises(TimeoutError):
            unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=1, poll=0.05, source_ms=now_ms(),
                                assemblies_dir=dlls, package_dir=package, disk_settle=0.3)

    def test_a_dll_written_just_now_waits_for_the_domain_reload(self):
        package, dlls = assembly_tree(self, source_ages={"A": 60, "B": 60}, dll_ages={"A": 30, "B": 0})
        editor, fake = self.editor(reload_after_refresh=False)
        with self.assertRaises(TimeoutError):
            unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=1, poll=0.05, source_ms=now_ms(),
                                assemblies_dir=dlls, package_dir=package, disk_settle=20)

    def test_a_compile_error_is_red(self):
        editor, fake = self.editor(compile_failed=True)
        with self.assertRaisesRegex(unity_tests.Failed, "failed to compile"):
            unity_tests.refresh(fake.port, time.monotonic() + 30, poll=0.05)

    def test_a_compile_error_with_no_reload_is_red_not_a_timeout(self):
        # A failed compile ends in no domain reload, so the wait runs out first.
        editor, fake = self.editor(reload_after_refresh=False, compile_failed=True)
        with self.assertRaisesRegex(unity_tests.Failed, "failed to compile"):
            unity_tests.refresh(fake.port, time.monotonic() + 30, compile_wait=1, poll=0.05)

    def test_a_job_started_before_the_drop_is_followed_not_restarted(self):
        editor, fake = self.editor(drop_run_tests=1)
        job = unity_tests.run(fake.port, ["A"], None, time.monotonic() + 30, poll=0.05)
        self.assertEqual(job["job_id"], "job-1")
        self.assertEqual(len(fake.sent("run_tests")), 1)

    def test_a_dropped_request_waits_out_an_editor_too_busy_to_answer(self):
        # After a restart the editor drops snapshot requests while the job starts (#229).
        editor, fake = self.editor(drop_run_tests=1, blind_for=1)
        job_id = unity_tests.start_job(fake.port, {}, time.monotonic() + 30, adopt_wait=0.3, poll=0.05,
                                       drop_wait=3)
        self.assertEqual(job_id, "job-1")

    def test_a_dropped_request_with_no_job_seen_says_so_not_that_tests_are_running(self):
        editor, fake = self.editor(drop_run_tests=1, blind_for=30)
        with self.assertRaisesRegex(RuntimeError, "no test job seen"):
            unity_tests.start_job(fake.port, {}, time.monotonic() + 30, adopt_wait=0.3, poll=0.05,
                                  drop_wait=1)

    def test_a_fast_job_whose_reply_was_lost_is_run_once_more(self):
        # It finished before a snapshot showed its id, so there is no job to follow.
        editor, fake = self.editor(drop_run_tests=1, fast=True)
        job = unity_tests.run(fake.port, ["A"], None, time.monotonic() + 30, poll=0.05)
        self.assertEqual((job["job_id"], job["status"]), ("job-2", "succeeded"))
        self.assertEqual(len(fake.sent("run_tests")), 2)

    def test_a_fast_job_whose_reply_is_lost_twice_is_not_run_a_third_time(self):
        editor, fake = self.editor(drop_run_tests=2, fast=True)
        with self.assertRaisesRegex(RuntimeError, "result unknown"):
            unity_tests.start_job(fake.port, {}, time.monotonic() + 30, adopt_wait=2, poll=0.05)
        self.assertEqual(len(fake.sent("run_tests")), 2)

    def test_a_refused_run_that_only_mentions_tests_running_is_an_error(self):
        editor, fake = self.editor(run_tests_error="Failed to start test job: no assembly 'tests_running'")
        with self.assertRaisesRegex(bridge.BridgeError, "no assembly"):
            unity_tests.start_job(fake.port, {}, time.monotonic() + 30, adopt_wait=0.5, poll=0.05)

    def test_tests_another_client_started_are_not_taken_as_ours(self):
        editor, fake = self.editor(busy_since=int(time.time() * 1000) - 60000)
        with self.assertRaisesRegex(RuntimeError, "didn't start"):
            unity_tests.start_job(fake.port, {}, time.monotonic() + 30, adopt_wait=0.5, poll=0.05)

    def test_a_job_the_editor_doesnt_know_ends_the_run_at_once(self):
        editor, fake = self.editor(forget_jobs=True)
        started = time.monotonic()
        with self.assertRaisesRegex(RuntimeError, "Unknown job_id"):
            unity_tests.run(fake.port, ["A"], None, time.monotonic() + 30, poll=0.05)
        self.assertLess(time.monotonic() - started, 5)

    def test_the_run_asks_for_the_assemblies_and_the_group(self):
        editor, fake = self.editor()
        unity_tests.run(fake.port, ["A", "B"], "Ns\\.Class\\.", time.monotonic() + 30, poll=0.05)
        params = fake.sent("run_tests")[0]["params"]
        self.assertEqual((params["assembly_names"], params["group_names"]), (["A", "B"], ["Ns\\.Class\\."]))

    def test_the_run_gives_the_job_longer_than_the_bridges_15_s_to_start(self):
        editor, fake = self.editor()
        unity_tests.run(fake.port, ["A"], None, time.monotonic() + 30, poll=0.05)
        self.assertGreaterEqual(fake.sent("run_tests")[0]["params"]["init_timeout"], 60000)


class MainTest(unittest.TestCase):
    """main()'s exit codes, against a fake editor found through its status file."""

    def main(self, editor, *args):
        fake = FakeBridge(editor)
        self.addCleanup(fake.close)
        tmp = temp_dir(self)
        with open(os.path.join(tmp, "unity-mcp-status-x.json"), "w") as f:
            json.dump({"project_name": "uv-wizard", "unity_port": fake.port}, f)
        os.environ["UNITY_MCP_STATUS_DIR"] = tmp
        self.addCleanup(os.environ.pop, "UNITY_MCP_STATUS_DIR")
        with redirect_stdout(io.StringIO()), unittest.mock.patch("sys.stderr", io.StringIO()):
            return unity_tests.main(["--no-refresh", "-a", "A", "-t", "10"] + list(args))

    def test_green_is_0(self):
        self.assertEqual(self.main(FakeEditor()), 0)

    def test_a_failing_test_is_1(self):
        job = {"status": "failed", "error": None,
               "progress": {"failures_so_far": [{"full_name": "T", "message": "m"}]}}
        self.assertEqual(self.main(FakeEditor(job=job)), 1)

    def test_an_inconclusive_test_is_1(self):
        self.assertEqual(self.main(FakeEditor(job=succeeded(2, 2, results=[inconclusive("T")]))), 1)

    def test_a_job_that_broke_is_2(self):
        self.assertEqual(self.main(FakeEditor(job={"status": "failed", "error": "orphaned"})), 2)

    def test_tests_already_running_is_2(self):
        self.assertEqual(self.main(FakeEditor(busy_since=1)), 2)

    def test_a_job_the_editor_lost_is_2(self):
        self.assertEqual(self.main(FakeEditor(forget_jobs=True)), 2)


class ToolingBehindTest(unittest.TestCase):
    """tooling_behind() against a throwaway repo whose origin/main is a plain ref."""

    def setUp(self):
        self.repo = temp_dir(self)
        self.git("init", "-q")
        self.commit("tools/unity_tests.py", "old")
        self.base = self.git("rev-parse", "HEAD")

    def git(self, *args):
        return subprocess.run(["git", "-C", self.repo, "-c", "user.name=t", "-c", "user.email=t@t"] + list(args),
                              capture_output=True, text=True, check=True).stdout.strip()

    def commit(self, path, text):
        full = os.path.join(self.repo, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w") as f:
            f.write(text)
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "change " + path)

    def main_moves_on(self, path):
        self.commit(path, "new")
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")
        self.git("checkout", "-q", self.base)

    def test_a_tools_fix_the_branch_lacks_is_listed(self):
        self.main_moves_on("tools/unity_tests.py")
        self.assertEqual(len(unity_tests.tooling_behind(self.repo)), 1)

    def test_a_main_commit_outside_tools_is_not(self):
        self.main_moves_on("Packages/a.cs")
        self.assertEqual(unity_tests.tooling_behind(self.repo), [])

    def test_a_branch_that_has_main_is_not_behind(self):
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")
        self.commit("tools/unity_tests.py", "branch edit")
        self.assertEqual(unity_tests.tooling_behind(self.repo), [])

    def test_no_origin_main_is_not_behind(self):
        self.assertEqual(unity_tests.tooling_behind(self.repo), [])


class TestAssembliesTest(unittest.TestCase):
    def test_the_default_is_every_test_assembly_in_the_package(self):
        # The SDK-, NDMF- and shader-gated ones sit in subfolders of Tests/Editor.
        names = unity_tests.test_assemblies()
        for name in ("UVWizard.Core.Editor.Tests", "UVWizard.NDMF.Editor.Tests",
                     "UVWizard.Core.Editor.Tests.LilToon", "UVWizard.Core.Editor.Tests.Poiyomi",
                     "UVWizard.Core.Editor.Tests.VRChat"):
            self.assertIn(name, names)


def now_ms():
    return int(time.time() * 1000)


def assembly_tree(test, source_ages, dll_ages):
    """A package with assembly A in A/ and B nested in A/B/, and their DLLs; ages in seconds."""
    package, dlls = temp_dir(test), temp_dir(test)
    for name, folder in (("A", "A"), ("B", os.path.join("A", "B"))):
        os.makedirs(os.path.join(package, folder), exist_ok=True)
        for path, text in ((os.path.join(package, folder, name + ".asmdef"), json.dumps({"name": name})),
                           (os.path.join(package, folder, name + ".cs"), "class %s {}" % name)):
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
            stamp = time.time() - source_ages[name]
            os.utime(path, (stamp, stamp))
        if name in dll_ages:
            path = os.path.join(dlls, name + ".dll")
            open(path, "wb").close()
            stamp = time.time() - dll_ages[name]
            os.utime(path, (stamp, stamp))
    return package, dlls


class CompiledOnDiskTest(unittest.TestCase):
    def test_each_assembly_is_compared_with_its_own_sources(self):
        # Unity rewrites only the assemblies whose sources changed: A's DLL is older than B's newer source.
        package, dlls = assembly_tree(self, source_ages={"A": 60, "B": 20}, dll_ages={"A": 40, "B": 10})
        self.assertIsNotNone(unity_tests.compiled_on_disk_ms(dlls, package))

    def test_a_nested_assemblys_script_is_not_counted_in_its_parent(self):
        package, dlls = assembly_tree(self, source_ages={"A": 60, "B": 5}, dll_ages={"A": 40, "B": 10})
        self.assertIsNone(unity_tests.compiled_on_disk_ms(dlls, package))

    def test_an_assembly_the_project_does_not_build_is_skipped(self):
        package, dlls = assembly_tree(self, source_ages={"A": 60, "B": 5}, dll_ages={"A": 40})
        self.assertIsNotNone(unity_tests.compiled_on_disk_ms(dlls, package))

    def test_the_package_maps_each_test_assembly(self):
        self.assertIn("UVWizard.Core.Editor.Tests.VRChat", unity_tests.assembly_sources())


class FindPortTest(unittest.TestCase):
    def status_dir(self, files):
        tmp = temp_dir(self)
        for i, (name, port) in enumerate(files):
            path = os.path.join(tmp, "unity-mcp-status-%d.json" % i)
            with open(path, "w") as f:
                json.dump({"project_name": name, "unity_port": port}, f)
            os.utime(path, (1000 + i, 1000 + i))
        os.environ["UNITY_MCP_STATUS_DIR"] = tmp
        self.addCleanup(os.environ.pop, "UNITY_MCP_STATUS_DIR")

    def test_the_named_projects_status_file_wins(self):
        self.status_dir([("other", 6401), ("uv-wizard", 6402)])
        self.assertEqual(bridge.find_port("uv-wizard"), 6402)
        self.assertEqual(bridge.find_port("missing"), 6400)

    def test_the_script_assemblies_sit_beside_the_projects_assets(self):
        tmp = temp_dir(self)
        with open(os.path.join(tmp, "unity-mcp-status-0.json"), "w") as f:
            json.dump({"project_name": "uv-wizard", "unity_port": 6401, "project_path": "C:/P/uv-wizard/Assets"}, f)
        os.environ["UNITY_MCP_STATUS_DIR"] = tmp
        self.addCleanup(os.environ.pop, "UNITY_MCP_STATUS_DIR")
        self.assertEqual(os.path.normpath(unity_tests.script_assemblies_dir("uv-wizard")),
                         os.path.normpath("C:/P/uv-wizard/Library/ScriptAssemblies"))
        self.assertIsNone(unity_tests.script_assemblies_dir("missing"))

    def test_the_newest_status_file_wins(self):
        self.status_dir([("uv-wizard", 6401), ("uv-wizard", 6403), ("uv-wizard", 6402)])
        self.assertEqual(bridge.find_port("uv-wizard"), 6402)


if __name__ == "__main__":
    unittest.main()

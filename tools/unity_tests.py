"""Run EditMode tests in the open Unity editor, through the MCP for Unity bridge.

Usage: python tools/unity_tests.py [--refresh-only | --no-refresh] [-a ASSEMBLY]... [-g GROUP_REGEX] [-t SECONDS]

By default it refreshes the asset database, recompiles and waits for the
editor, then runs every test assembly under Packages/com.kingazaz.uv-wizard/Tests
and polls until the run ends. It prints each failure and exits:
  0  every selected test passed;
  1  a test failed, the scripts didn't compile, or the selection ran no tests;
  2  the bridge or the editor couldn't run the tests (unreachable, timed out,
     or already running tests).
"""

import argparse
import glob
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_bridge as bridge  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TESTS_DIR = os.path.join(REPO, "Packages", "com.kingazaz.uv-wizard", "Tests")


class Failed(Exception):
    """The tests ran, or tried to, and the answer is red: exit 1."""


def log(message):
    print(message, file=sys.stderr, flush=True)


def utf8_streams():
    """Write UTF-8 whatever the console's code page.

    Failure messages can quote AAO's reports, which use arrows, and Windows
    consoles default to cp1252. `python -I` ignores PYTHONIOENCODING, so set it here.
    """
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def now_ms():
    return int(time.time() * 1000)


def test_assemblies():
    names = []
    for path in sorted(glob.glob(os.path.join(TESTS_DIR, "**", "*.asmdef"), recursive=True)):
        with open(path, encoding="utf-8-sig") as f:
            names.append(json.load(f)["name"])
    return names


def editor_state(port, deadline):
    return bridge.result_of(bridge.call(port, "get_editor_state", deadline=deadline))


def is_idle(state):
    compilation = state.get("compilation") or {}
    assets = state.get("assets") or {}
    return (
        (state.get("activity") or {}).get("phase") == "idle"
        and not compilation.get("is_compiling")
        and not compilation.get("is_domain_reload_pending")
        and not assets.get("is_updating")
        and not (assets.get("refresh") or {}).get("is_refresh_in_progress")
    )


def compile_finished_since(state, since_ms):
    """Whether a compile or a domain reload finished after since_ms.

    The bridge keeps its compile timestamps in static fields, which a domain
    reload clears; the reload's own timestamp survives it.
    """
    compilation = state.get("compilation") or {}
    for key in ("last_compile_finished_unix_ms", "last_domain_reload_after_unix_ms"):
        value = compilation.get(key)
        if value and value >= since_ms:
            return True
    return False


def compilation_failed(port, deadline):
    reply = bridge.call(
        port, "execute_code",
        {"action": "execute", "code": "return UnityEditor.EditorUtility.scriptCompilationFailed;"},
        deadline=deadline)
    return bridge.result_of(reply).get("result") is True


def refresh(port, deadline, compile_wait=180, poll=1.0):
    """Refresh, request a compile, and wait until the editor is idle again on the new code.

    A requested compile always ends in a compile or a domain reload, even with
    nothing changed, but only seconds after the refresh returns (about 8 s on
    the dev machine). So the wait ends only once one is seen: an idle editor
    alone may still be on the old code.
    """
    started = now_ms()
    try:
        bridge.result_of(bridge.call(
            port, "refresh_unity", {"mode": "force", "scope": "all", "compile": "request", "wait_for_ready": False},
            deadline=deadline))
    except bridge.BridgeError as error:
        raise RuntimeError("refresh failed: %s" % error)
    give_up = min(deadline, time.monotonic() + compile_wait)
    while True:
        try:
            state = editor_state(port, give_up)
        except bridge.BridgeError:
            state = None
        if state is not None and is_idle(state) and compile_finished_since(state, started):
            break
        if time.monotonic() > give_up:
            if compilation_failed(port, deadline):
                raise Failed("scripts failed to compile; the editor still runs the old code (see its console)")
            raise TimeoutError("no compile or domain reload seen within %d s of the refresh" % compile_wait)
        time.sleep(poll)
    if compilation_failed(port, deadline):
        raise Failed("scripts failed to compile; the editor still runs the old code (see its console)")
    log("unity-tests: refreshed and compiled")


def start_job(port, params, deadline, adopt_wait=10, poll=1.0):
    """Start a test run and return its job id.

    run_tests isn't safe to send twice, so it is never resent blind. If the
    connection drops after the request goes out, or the editor answers that
    tests are running, the job the editor started since this call is ours.
    """
    requested = now_ms()
    try:
        reply = bridge.call(port, "run_tests", params, deadline=deadline, resend=False)
        if "tests_running" not in json.dumps(reply):
            return bridge.result_of(reply)["job_id"]
    except bridge.DroppedAfterSending as error:
        log("unity-tests: %s; looking for the job it started" % error)
    give_up = min(deadline, time.monotonic() + adopt_wait)
    while True:
        try:
            tests = editor_state(port, deadline).get("tests") or {}
        except bridge.BridgeError:
            tests = {}
        if tests.get("current_job_id") and (tests.get("started_unix_ms") or 0) >= requested:
            return tests["current_job_id"]
        if time.monotonic() > give_up:
            raise RuntimeError("the editor is already running tests that this run didn't start")
        time.sleep(poll)


def run(port, assemblies, group, deadline, poll=2.0):
    params = {"mode": "EditMode", "assembly_names": assemblies, "include_failed_tests": True}
    if group:
        params["group_names"] = [group]
    job_id = start_job(port, params, deadline)
    log("unity-tests: started job %s" % job_id)
    while True:
        if time.monotonic() > deadline:
            raise TimeoutError("tests still running; job %s" % job_id)
        time.sleep(poll)
        try:
            job = bridge.result_of(bridge.call(
                port, "get_test_job", {"job_id": job_id, "include_failed_tests": True}, deadline=deadline))
        except bridge.BridgeError as error:
            # The editor's main thread is busy while a test runs; the next poll gets through.
            log("unity-tests: poll missed (%s)" % error)
            continue
        if job.get("status") != "running":
            return job


def report(job):
    """Print the job's outcome and raise Failed when it isn't all green.

    The bridge marks a job "failed" both when a test fails (error null) and when
    the job itself breaks (error set). Only a succeeded job carries the result
    summary; a failed one carries its first 25 failures under progress.
    """
    status, error = job.get("status"), job.get("error")
    if status == "failed" and not error:
        failures = (job.get("progress") or {}).get("failures_so_far") or []
        for failure in failures:
            print("FAIL %s\n  %s" % (failure.get("full_name"),
                                     (failure.get("message") or "").strip().replace("\n", "\n  ")))
        if (job.get("progress") or {}).get("failures_capped"):
            print("(only the first %d failures are listed)" % len(failures))
            raise Failed("%d or more tests failed" % len(failures))
        raise Failed("%d test(s) failed" % max(len(failures), 1))
    if status != "succeeded":
        raise RuntimeError("test job %s: %s" % (status, error))
    summary = (job.get("result") or {}).get("summary") or {}
    total = summary.get("total", 0)
    print("total %s, passed %s, failed %s, skipped %s, %.1f s" % (
        total, summary.get("passed"), summary.get("failed"), summary.get("skipped"),
        summary.get("durationSeconds") or 0))
    if not total:
        raise Failed("no tests ran: check the assembly and group selection")
    if summary.get("failed"):
        raise Failed("%d test(s) failed" % summary["failed"])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    which = parser.add_mutually_exclusive_group()
    which.add_argument("--refresh-only", action="store_true", help="refresh and compile; run no tests")
    which.add_argument("--no-refresh", action="store_true", help="run the tests on the code already compiled")
    parser.add_argument("-a", "--assembly", action="append", help="test assembly to run (repeatable); default: all")
    parser.add_argument("-g", "--group", help="regex over test full names, e.g. 'Ns\\.Class\\.(TestA|TestB)'")
    parser.add_argument("-t", "--timeout", type=float, default=900, help="seconds before giving up (default 900)")
    parser.add_argument("--project", default="uv-wizard", help="Unity project name in the bridge status file")
    args = parser.parse_args(argv)
    utf8_streams()

    deadline = time.monotonic() + args.timeout
    port = bridge.find_port(args.project)
    try:
        if not args.no_refresh:
            refresh(port, deadline)
        if args.refresh_only:
            return 0
        report(run(port, args.assembly or test_assemblies(), args.group, deadline))
        return 0
    except Failed as error:
        log("unity-tests: %s" % error)
        return 1
    except (bridge.BridgeError, RuntimeError, TimeoutError) as error:
        log("unity-tests: couldn't run the tests: %s" % error)
        return 2


if __name__ == "__main__":
    sys.exit(main())

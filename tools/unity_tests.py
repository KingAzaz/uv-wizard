"""Run EditMode tests in the open Unity editor, through the MCP for Unity bridge.

Usage: python tools/unity_tests.py [--refresh-only | --no-refresh] [-a ASSEMBLY]... [-g GROUP_REGEX] [-t SECONDS]

By default it refreshes the asset database, recompiles and waits for the
editor, then runs every test assembly under Packages/com.kingazaz.uv-wizard/Tests
and polls until the run ends. It prints each failing and Inconclusive test and exits:
  0  every selected test passed;
  1  a test failed or was Inconclusive, the scripts didn't compile, or the
     selection ran no tests;
  2  the bridge or the editor couldn't run the tests (unreachable, timed out,
     or already running tests).
"""

import argparse
import glob
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_bridge as bridge  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE_DIR = os.path.join(REPO, "Packages", "com.kingazaz.uv-wizard")
TESTS_DIR = os.path.join(PACKAGE_DIR, "Tests")

# MCP for Unity's TestJobManager auto-fails a job that hasn't started within
# 15 s by default ("failed to initialize"), yet Unity still runs it, so the next
# request collides with it. Jobs right after a recompile often take longer (#209, #218).
INIT_TIMEOUT_MS = 120000


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


def tooling_behind(repo=REPO):
    """The commits on origin/main that change tools/ and that HEAD lacks, one line each.

    A branch cut before a fix to this script runs the old one: the #214 run hit
    four "couldn't run the tests" errors and a mutation batch that proved nothing,
    all fixed by #238, which merged minutes after its branch was cut.
    Reads the last fetch only, and returns [] when git can't answer.
    """
    try:
        out = subprocess.run(
            ["git", "-C", repo, "log", "--oneline", "HEAD..origin/main", "--", "tools"],
            capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired):
        return []
    return out.stdout.splitlines() if out.returncode == 0 else []


def now_ms():
    return int(time.time() * 1000)


def test_assemblies():
    names = []
    for path in sorted(glob.glob(os.path.join(TESTS_DIR, "**", "*.asmdef"), recursive=True)):
        with open(path, encoding="utf-8-sig") as f:
            names.append(json.load(f)["name"])
    return names


def editor_state(port, deadline, timeout=30.0):
    return bridge.result_of(bridge.call(port, "get_editor_state", timeout=timeout, deadline=deadline))


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


def newest_source_ms():
    """When a script or assembly definition in the package last changed, in unix ms."""
    newest = 0
    for pattern in ("*.cs", "*.asmdef", "*.asmref"):
        for path in glob.glob(os.path.join(PACKAGE_DIR, "**", pattern), recursive=True):
            newest = max(newest, int(os.path.getmtime(path) * 1000))
    return newest


def assembly_sources(package_dir=PACKAGE_DIR):
    """Map each assembly in the package to when its own asmdef or scripts last changed, in unix ms.

    A script belongs to the assembly whose asmdef sits in its nearest folder that has one.
    """
    roots = {}
    for path in glob.glob(os.path.join(package_dir, "**", "*.asmdef"), recursive=True):
        with open(path, encoding="utf-8-sig") as f:
            roots[os.path.dirname(path)] = json.load(f)["name"]
    newest = {}
    for pattern in ("*.cs", "*.asmdef"):
        for path in glob.glob(os.path.join(package_dir, "**", pattern), recursive=True):
            folder = os.path.dirname(path)
            while folder not in roots and len(folder) > len(package_dir):
                folder = os.path.dirname(folder)
            name = roots.get(folder)
            if name:
                newest[name] = max(newest.get(name, 0), int(os.path.getmtime(path) * 1000))
    return newest


def compiled_on_disk_ms(assemblies_dir, package_dir=PACKAGE_DIR):
    """When the editor last wrote a package assembly, in unix ms, if each one is newer than its own sources.

    None while any assembly is older than its sources. Unity rewrites only the
    assemblies whose sources changed, so each is compared with its own. An
    assembly with no DLL is one the project doesn't build (its defineConstraints
    fail), and is skipped.
    """
    written = None
    for name, source_ms in assembly_sources(package_dir).items():
        dll = os.path.join(assemblies_dir, name + ".dll")
        if not os.path.exists(dll):
            continue
        dll_ms = int(os.path.getmtime(dll) * 1000)
        if dll_ms < source_ms:
            return None
        written = max(written or 0, dll_ms)
    return written


def script_assemblies_dir(project_name):
    """The project's Library/ScriptAssemblies, from the bridge's status file, or None."""
    status = bridge.find_status(project_name)
    assets = status and status.get("project_path")
    return os.path.join(os.path.dirname(assets.rstrip("/\\")), "Library", "ScriptAssemblies") if assets else None


def refresh(port, deadline, compile_wait=180, poll=1.0, settle=15, source_ms=None,
            assemblies_dir=None, package_dir=PACKAGE_DIR, disk_settle=25, poll_wait=10.0):
    """Refresh, request a compile, and wait until the editor is idle again on the new code.

    A requested compile usually ends in a compile or a domain reload, even with
    nothing changed, but only seconds after the refresh returns (about 8 s on
    the dev machine). So the wait ends once one is seen: an idle editor alone
    may still be on the old code.

    A focused editor compiles each edit by itself, and the requested compile
    then may never come (#229). So the wait also ends once the editor has
    stayed idle for settle seconds after a compile that finished later than
    the newest source file.

    Polls can time out for minutes while the editor's main thread is busy,
    and that held the wait open on code the editor had already compiled
    (#233: 18 exits 2 across five runs). So a poll that times out doesn't
    restart the settle window, and the wait also ends on an idle answer once
    each assembly's DLL in assemblies_dir is newer than its own sources and
    disk_settle seconds have passed since both the refresh and the last DLL
    write.

    A domain reload can take over 200 s, with the bridge unreachable
    throughout (#237). So compile_wait counts only time the editor answers
    idle; a busy or unreachable editor is waited on until the deadline.
    """
    started = now_ms()
    if source_ms is None:
        source_ms = newest_source_ms()
    current_since = None
    try:
        bridge.result_of(bridge.call(
            port, "refresh_unity", {"mode": "force", "scope": "all", "compile": "request", "wait_for_ready": False},
            deadline=deadline))
    except bridge.BridgeError as error:
        raise RuntimeError("refresh failed: %s" % error)
    refreshed = time.monotonic()
    give_up = min(deadline, refreshed + compile_wait)
    while True:
        try:
            state = editor_state(port, min(give_up, time.monotonic() + poll_wait), timeout=poll_wait)
        except bridge.BridgeError:
            state = None
        idle = state is not None and is_idle(state)
        if idle and compile_finished_since(state, started):
            break
        written = compiled_on_disk_ms(assemblies_dir, package_dir) if idle and assemblies_dir else None
        if (written and time.monotonic() - refreshed >= disk_settle
                and now_ms() - written >= disk_settle * 1000):
            log("unity-tests: every assembly on disk is newer than its sources")
            break
        if idle and compile_finished_since(state, source_ms):
            current_since = current_since or time.monotonic()
            if time.monotonic() - current_since >= settle:
                break
        elif state is not None:
            # Only a busy answer restarts the settle window: a poll that timed out says nothing.
            current_since = None
        if not idle:
            give_up = min(deadline, time.monotonic() + compile_wait)
        if time.monotonic() > give_up:
            if compilation_failed(port, deadline):
                raise Failed("scripts failed to compile; the editor still runs the old code (see its console)")
            if not idle:
                raise TimeoutError("the editor stayed busy or unreachable until the deadline (-t)")
            raise TimeoutError("no compile or domain reload seen in %d s of an idle editor after the refresh" % compile_wait)
        time.sleep(poll)
    if compilation_failed(port, deadline):
        raise Failed("scripts failed to compile; the editor still runs the old code (see its console)")
    log("unity-tests: refreshed and compiled")


def start_job(port, params, deadline, adopt_wait=10, poll=1.0, reruns=1, drop_wait=90):
    """Start a test run and return its job id.

    run_tests isn't safe to send twice, so it is never resent blind. If the
    connection drops after the request goes out, or the editor answers that
    tests are running, the job the editor started since this call is ours.

    The editor-state snapshot refreshes at most once a second, and the bridge
    clears the current job id when a job ends. So a job can finish before any
    snapshot names it: a fast one, or any one while the busy editor drops the
    snapshot requests. The snapshot then shows a run that started and finished
    since this call, but no job to follow. Nothing is running then, so
    run_tests is sent again, up to reruns times, and the first run's result is
    lost: a flaky test can fail in it and pass in the rerun.

    After an editor restart a job can take most of a minute to start, and the
    busy editor drops the snapshot requests meanwhile. So after a dropped
    request the wait lasts drop_wait seconds, and it ends saying tests are
    running only if a snapshot showed a job this run didn't start (#229).
    """
    requested = now_ms()
    try:
        reply = bridge.call(port, "run_tests", params, deadline=deadline, resend=False)
        if error_code(reply) != "tests_running":   # RunTests.cs's code when tests are running
            return bridge.result_of(reply)["job_id"]
    except bridge.DroppedAfterSending as error:
        log("unity-tests: %s; looking for the job it started" % error)
        adopt_wait = max(adopt_wait, drop_wait)
    give_up = min(deadline, time.monotonic() + adopt_wait)
    saw_theirs = False
    while True:
        try:
            tests = editor_state(port, give_up).get("tests") or {}
        except bridge.BridgeError:
            tests = {}
        ours = (tests.get("started_unix_ms") or 0) >= requested
        if ours and tests.get("current_job_id"):
            return tests["current_job_id"]
        if not ours and tests.get("current_job_id"):
            saw_theirs = True
        if ours and (tests.get("last_run") or {}).get("finished_unix_ms"):
            if not reruns:
                raise RuntimeError("the tests ran, but the editor's reply was lost: result unknown")
            log("unity-tests: the job finished before its id was seen; running it again (its result is lost)")
            return start_job(port, params, deadline, adopt_wait, poll, reruns - 1)
        if time.monotonic() > give_up:
            if saw_theirs:
                raise RuntimeError("the editor is already running tests that this run didn't start")
            raise RuntimeError("no test job seen within %d s of the request" % adopt_wait)
        time.sleep(poll)


def error_code(reply):
    """The error a tool answered with, such as "tests_running", or None."""
    return (reply.get("result") or {}).get("error")


def run(port, assemblies, group, deadline, poll=2.0):
    params = {"mode": "EditMode", "assembly_names": assemblies, "include_failed_tests": True,
              "init_timeout": INIT_TIMEOUT_MS}
    if group:
        params["group_names"] = [group]
    job_id = start_job(port, params, deadline)
    log("unity-tests: started job %s" % job_id)
    while True:
        if time.monotonic() > deadline:
            raise TimeoutError("tests still running; job %s" % job_id)
        time.sleep(poll)
        try:
            reply = bridge.call(
                port, "get_test_job", {"job_id": job_id, "include_failed_tests": True}, deadline=deadline)
            if error_code(reply) == "Unknown job_id.":
                # GetTestJob.cs: the editor has no such job, so no later poll will find it.
                raise RuntimeError("Unknown job_id %s: the editor lost the job" % job_id)
            job = bridge.result_of(reply)
        except bridge.BridgeError as error:
            # The editor's main thread is busy while a test runs; the next poll gets through.
            log("unity-tests: poll missed (%s)" % error)
            continue
        if job.get("status") != "running":
            return job


def print_result(kind, full_name, message):
    print("%s %s\n  %s" % (kind, full_name, (message or "").strip().replace("\n", "\n  ")))


def inconclusive_results(job):
    """The job's Inconclusive tests, from the result list include_failed_tests asks for.

    The bridge counts a run "succeeded" while no test failed, and its summary
    leaves Inconclusive tests out of every count, total included. CI fails a run
    with any, so they're red here too.
    """
    results = (job.get("result") or {}).get("results") or []
    return [r for r in results if (r.get("state") or "").startswith("Inconclusive")]


def report(job):
    """Print the job's outcome and raise Failed when it isn't all green.

    The bridge marks a job "failed" both when a test fails (error null) and when
    the job itself breaks (error set). A finished job carries its result list;
    a failed one also carries its first 25 failures under progress, which this
    reads for their messages.
    """
    status, error = job.get("status"), job.get("error")
    inconclusive = inconclusive_results(job)
    also = ", %d inconclusive" % len(inconclusive) if inconclusive else ""
    if status == "failed" and not error:
        failures = (job.get("progress") or {}).get("failures_so_far") or []
        for failure in failures:
            print_result("FAIL", failure.get("full_name"), failure.get("message"))
        for result in inconclusive:
            print_result("INCONCLUSIVE", result.get("fullName"), result.get("message"))
        if (job.get("progress") or {}).get("failures_capped"):
            print("(only the first %d failures are listed)" % len(failures))
            raise Failed("%d or more tests failed%s" % (len(failures), also))
        raise Failed("%d test(s) failed%s" % (max(len(failures), 1), also))
    if status != "succeeded":
        raise RuntimeError("test job %s: %s" % (status, error))
    for result in inconclusive:
        print_result("INCONCLUSIVE", result.get("fullName"), result.get("message"))
    summary = (job.get("result") or {}).get("summary") or {}
    total = summary.get("total", 0) + len(inconclusive)
    print("total %s, passed %s, failed %s, inconclusive %d, skipped %s, %.1f s" % (
        total, summary.get("passed"), summary.get("failed"), len(inconclusive), summary.get("skipped"),
        summary.get("durationSeconds") or 0))
    if summary.get("failed"):
        raise Failed("%d test(s) failed" % summary["failed"])
    if inconclusive:
        raise Failed("%d test(s) inconclusive" % len(inconclusive))
    if not total:
        raise Failed("no tests ran: check the assembly and group selection")


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
    behind = tooling_behind()
    if behind:
        log("unity-tests: origin/main has %d newer commit(s) to tools/; merge origin/main before "
            "trusting a \"couldn't run the tests\" result:\n  %s" % (len(behind), "\n  ".join(behind)))

    deadline = time.monotonic() + args.timeout
    port = bridge.find_port(args.project)
    try:
        if not args.no_refresh:
            refresh(port, deadline, assemblies_dir=script_assemblies_dir(args.project))
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

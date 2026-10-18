"""A minimal client for the MCP for Unity editor bridge (v10.3.0, stdio transport).

The bridge listens on localhost (port 6400 unless the status file says
otherwise). After a one-line WELCOME handshake, each message is a frame: an
8-byte big-endian length, then that many bytes of UTF-8 JSON. A request is
{"type": <tool or resource name>, "params": {...}}.

The bridge keeps one client: each new connection closes the others
(StdioBridgeHost.cs). So this client opens a connection per request and
retries when it is dropped, which is also what happens when the editor reloads
its domain. Each of its connections also drops the MCP server an agent holds
open, and an MCP call in flight then loses its reply: make no MCP calls while
unity-tests.sh runs.
"""

import glob
import json
import os
import socket
import struct
import time

HANDSHAKE = b"WELCOME UNITY-MCP 1 FRAMING=1\n"


class BridgeError(Exception):
    """The bridge stayed unreachable, or answered with something unreadable."""


def status_dir():
    return os.environ.get("UNITY_MCP_STATUS_DIR") or os.path.join(os.path.expanduser("~"), ".unity-mcp")


def find_port(project_name=None):
    """The port from the newest status file (for project_name, if given), or 6400."""
    status = find_status(project_name)
    return status.get("unity_port", 6400) if status else 6400


def find_status(project_name=None):
    """The newest status file's contents (for project_name, if given), or None."""
    best = None
    for path in glob.glob(os.path.join(status_dir(), "unity-mcp-status-*.json")):
        try:
            with open(path, encoding="utf-8") as f:
                status = json.load(f)
        except (OSError, ValueError):
            continue
        if project_name and status.get("project_name") != project_name:
            continue
        mtime = os.path.getmtime(path)
        if best is None or mtime > best[0]:
            best = (mtime, status)
    return best[1] if best else None


def read_exact(sock, count):
    data = b""
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            raise ConnectionError("bridge closed the connection")
        data += chunk
    return data


def call_once(port, kind, params, timeout):
    """Send one request on a fresh connection and return the parsed reply."""
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as sock:
        greeting = b""
        while not greeting.endswith(b"\n"):
            chunk = sock.recv(64)
            if not chunk:
                raise ConnectionError("bridge closed the connection during the handshake")
            greeting += chunk
        if greeting != HANDSHAKE:
            raise BridgeError("unexpected handshake: %r" % greeting)
        payload = json.dumps({"type": kind, "params": params}).encode("utf-8")
        sock.sendall(struct.pack(">Q", len(payload)) + payload)
        try:
            (length,) = struct.unpack(">Q", read_exact(sock, 8))
            return json.loads(read_exact(sock, length).decode("utf-8"))
        except OSError as error:
            raise DroppedAfterSending("%s request sent, then: %s" % (kind, error))


class DroppedAfterSending(BridgeError):
    """The request went out but no reply came back: the editor may have acted on it."""


def call(port, kind, params=None, timeout=30.0, deadline=None, resend=True):
    """call_once, retried on dropped or refused connections until deadline (a time.monotonic() value).

    With resend=False, a request that went out before the drop is not sent
    again: DroppedAfterSending is raised for the caller to find out what the
    editor did. Use it for requests that aren't safe to repeat, like run_tests.
    """
    if deadline is None:
        deadline = time.monotonic() + 120
    delay = 0.5
    while True:
        try:
            return call_once(port, kind, params or {}, timeout)
        except DroppedAfterSending as dropped:
            if not resend:
                raise
            error = dropped
        except OSError as refused:
            error = refused
        if time.monotonic() + delay > deadline:
            raise BridgeError("bridge unreachable on port %d: %s" % (port, error))
        time.sleep(delay)
        delay = min(delay * 2, 5.0)


def result_of(reply):
    """The data of a reply, unwrapping the bridge's and the tool's envelopes; raises on an error."""
    if reply.get("status") == "error":
        raise BridgeError(reply.get("error") or json.dumps(reply))
    inner = reply.get("result", reply)
    if isinstance(inner, dict) and inner.get("success") is False:
        raise BridgeError(inner.get("message") or inner.get("error") or json.dumps(inner))
    if isinstance(inner, dict) and "data" in inner:
        return inner["data"]
    return inner

"""Lease on the Unity checkout, so only one scheduled run uses it at a time.

`list_sessions` reports a session waiting on sub-agents as `isRunning: false`, so a second run's
gate can pass while the first still owns the checkout (retro issue #114). A run that switches
branches or runs Unity in C:\\Users\\Azaz\\AI\\uv-wizard holds this lease from its gate to its end.

    python checkout_lease.py take <session-id> <title>   # exit 1: leased by another run
    python checkout_lease.py check [<session-id>]        # exit 1: leased by another run
    python checkout_lease.py release <session-id>

The lease file lives under the checkout's .git directory, so `git status` stays clean. The holder
renews its lease by taking it again, which a run does before each branch switch and Unity run. A
lease not renewed for STALE_AFTER belongs to a run that died without releasing it, and `take`
replaces it.

Every write is atomic: a lease is written to a temp file, then linked into place when the checkout
is free, or renamed over the old lease. Taking over a stale lease first creates a claim file named
after that exact lease, so of two runs that both find it stale, only one replaces it.
"""

import argparse
import datetime
import hashlib
import json
import os
import sys
import tempfile

DEFAULT_LEASE_FILE = r"C:\Users\Azaz\AI\uv-wizard\.git\uv-wizard-checkout.lease"
STALE_AFTER = datetime.timedelta(hours=3)
# A takeover holds its claim for milliseconds. One older than this belongs to a run that died
# mid-takeover, and the next run to find it clears it.
CLAIM_STALE_AFTER = datetime.timedelta(minutes=5)


def now():
    return datetime.datetime.now(datetime.timezone.utc)


def read(path):
    """The lease at `path`, or None. Its token identifies this exact file."""
    try:
        with open(path, "rb") as f:
            stat = os.fstat(f.fileno())
            raw = f.read()
    except FileNotFoundError:
        return None
    try:
        lease = json.loads(raw.decode("utf-8"))
        lease["since_dt"] = datetime.datetime.fromisoformat(lease["since"])
        lease["session"], lease["title"]
    except (ValueError, KeyError, TypeError):
        # Empty (an older version of this script, between creating the file and writing it),
        # half-written or hand-edited. It holds the checkout until the file is STALE_AFTER old.
        mtime = datetime.datetime.fromtimestamp(stat.st_mtime, datetime.timezone.utc)
        lease = {"session": "?", "title": "unreadable lease",
                 "since": mtime.isoformat(timespec="seconds"), "since_dt": mtime}
    lease["token"] = hashlib.sha256(raw + b"|%d" % stat.st_mtime_ns).hexdigest()[:16]
    return lease


def describe(lease):
    return f"checkout leased by {lease['title']} ({lease['session']}) since {lease['since']}"


def blocking(lease, session, at):
    """The lease if it blocks `session`, else None."""
    if lease is None or lease["session"] == session or at - lease["since_dt"] >= STALE_AFTER:
        return None
    return lease


def refusal(lease):
    return False, describe(lease) if lease else "the lease changed while taking it; try again"


def write_temp(path, session, title, at):
    body = json.dumps({"session": session, "title": title, "since": at.isoformat(timespec="seconds")})
    fd, temp = tempfile.mkstemp(dir=os.path.dirname(path) or ".",
                                prefix=os.path.basename(path) + ".", suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(body)
    return temp


def create(path, session, title, at):
    temp = write_temp(path, session, title, at)
    try:
        os.link(temp, path)
    except FileExistsError:
        # Another run took it between our read and our write.
        return refusal(read(path))
    finally:
        os.remove(temp)
    return True, "lease taken"


def clear_abandoned(claim, at):
    try:
        claimed = datetime.datetime.fromtimestamp(os.stat(claim).st_mtime, datetime.timezone.utc)
        if at - claimed >= CLAIM_STALE_AFTER:
            os.remove(claim)
    except FileNotFoundError:
        pass


def take_over(path, stale, session, title, at):
    claim = f"{path}.claim-{stale['token']}"
    try:
        os.close(os.open(claim, os.O_WRONLY | os.O_CREAT | os.O_EXCL))
    except FileExistsError:
        clear_abandoned(claim, at)
        return False, f"another run is taking over the stale lease ({describe(stale)})"
    try:
        current = read(path)
        if current is None or current["token"] != stale["token"]:
            # Another run replaced or released it after our first read.
            return refusal(current)
        os.replace(write_temp(path, session, title, at), path)
    finally:
        os.remove(claim)
    return True, f"stale lease replaced ({describe(stale)})"


def take(path, session, title, at=None):
    """Returns (ok, message)."""
    at = at or now()
    lease = read(path)
    if lease is None:
        return create(path, session, title, at)
    if lease["session"] == session:
        os.replace(write_temp(path, session, title, at), path)
        return True, "lease renewed"
    if blocking(lease, session, at):
        return False, describe(lease)
    return take_over(path, lease, session, title, at)


def check(path, session=None, at=None):
    held = blocking(read(path), session, at or now())
    return (False, describe(held)) if held else (True, "checkout free")


def release(path, session):
    lease = read(path)
    if lease is None:
        return True, "no lease"
    if lease["session"] != session:
        return False, f"not released: {describe(lease)}"
    os.remove(path)
    return True, "lease released"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--lease-file", default=DEFAULT_LEASE_FILE)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("take")
    p.add_argument("session")
    p.add_argument("title")
    p = sub.add_parser("check")
    p.add_argument("session", nargs="?")
    p = sub.add_parser("release")
    p.add_argument("session")
    args = parser.parse_args(argv)

    if args.command == "take":
        ok, message = take(args.lease_file, args.session, args.title)
    elif args.command == "check":
        ok, message = check(args.lease_file, args.session)
    else:
        ok, message = release(args.lease_file, args.session)
    print(message)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

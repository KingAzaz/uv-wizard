"""Lease on the Unity checkout, so only one scheduled run uses it at a time.

`list_sessions` reports a session waiting on sub-agents as `isRunning: false`, so a second run's
gate can pass while the first still owns the checkout (retro issue #114). A run that switches
branches or runs Unity in C:\\Users\\Azaz\\AI\\uv-wizard holds this lease from its gate to its end.

    python checkout_lease.py take <session-id> <title>   # exit 1: leased by another run
    python checkout_lease.py check [<session-id>]        # exit 1: leased by another run
    python checkout_lease.py release <session-id>

The lease file lives under the checkout's .git directory, so `git status` stays clean. A lease
older than STALE_AFTER belongs to a run that died without releasing it, and `take` replaces it.
"""

import argparse
import datetime
import json
import os
import sys

DEFAULT_LEASE_FILE = r"C:\Users\Azaz\AI\uv-wizard\.git\uv-wizard-checkout.lease"
STALE_AFTER = datetime.timedelta(hours=3)


def now():
    return datetime.datetime.now(datetime.timezone.utc)


def read(path):
    try:
        with open(path, encoding="utf-8") as f:
            lease = json.load(f)
        lease["since_dt"] = datetime.datetime.fromisoformat(lease["since"])
        return lease
    except FileNotFoundError:
        return None
    except (ValueError, KeyError):
        # A half-written or hand-edited file: treat it as stale so it can be replaced.
        return {"session": "?", "title": "unreadable lease", "since": "?",
                "since_dt": datetime.datetime.min.replace(tzinfo=datetime.timezone.utc)}


def describe(lease):
    return f"checkout leased by {lease['title']} ({lease['session']}) since {lease['since']}"


def blocking(lease, session, at):
    """The lease if it blocks `session`, else None."""
    if lease is None or lease["session"] == session or at - lease["since_dt"] >= STALE_AFTER:
        return None
    return lease


def write_new(path, session, title, at):
    body = json.dumps({"session": session, "title": title, "since": at.isoformat(timespec="seconds")})
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(body)


def take(path, session, title, at=None):
    """Returns (ok, message)."""
    at = at or now()
    lease = read(path)
    held = blocking(lease, session, at)
    if held:
        return False, describe(held)
    message = "lease taken"
    if lease is not None:
        if lease["session"] == session:
            return True, "lease already held by this session"
        message = f"stale lease replaced ({describe(lease)})"
        os.remove(path)
    try:
        write_new(path, session, title, at)
    except FileExistsError:
        # Another run took it between our read and our write.
        return False, describe(read(path))
    return True, message


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

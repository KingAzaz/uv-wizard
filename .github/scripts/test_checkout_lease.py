import datetime
import json
import os
import tempfile
import unittest
from unittest import mock

import checkout_lease
from checkout_lease import CLAIM_STALE_AFTER, STALE_AFTER, check, main, release, take

T0 = datetime.datetime(2026, 10, 15, 17, 5, tzinfo=datetime.timezone.utc)


class LeaseTest(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.path = os.path.join(self.dir.name, "lease")

    def tearDown(self):
        self.dir.cleanup()

    def holder(self):
        with open(self.path, encoding="utf-8") as f:
            return json.load(f)

    def interrupt_at_read(self, nth, action):
        """Patches `read` so that `action` runs right after the nth read returns its result,
        as if another run were scheduled in between."""
        real = checkout_lease.read
        state = {"calls": 0, "fired": False}

        def hooked(path):
            result = real(path)
            state["calls"] += 1
            if state["calls"] == nth and not state["fired"]:
                state["fired"] = True
                action()
            return result

        return mock.patch.object(checkout_lease, "read", hooked)

    def test_a_free_checkout_can_be_taken(self):
        self.assertEqual(take(self.path, "a", "UV Wizard next slice", T0), (True, "lease taken"))
        self.assertEqual(self.holder()["session"], "a")

    def test_a_second_run_is_refused_while_the_first_waits(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        self.assertEqual(
            take(self.path, "b", "UV Wizard next slice", T0 + datetime.timedelta(minutes=21)),
            (False, "checkout leased by UV Wizard next slice (a) since 2026-10-15T17:05:00+00:00"))

    def test_the_loser_of_a_concurrent_take_is_refused(self):
        results = {}
        with self.interrupt_at_read(1, lambda: results.setdefault(
                "b", take(self.path, "b", "UV Wizard owner", T0))):
            results["a"] = take(self.path, "a", "UV Wizard next slice", T0)
        self.assertEqual(results["b"], (True, "lease taken"))
        self.assertEqual(results["a"],
                         (False, "checkout leased by UV Wizard owner (b) since 2026-10-15T17:05:00+00:00"))
        self.assertEqual(self.holder()["session"], "b")

    def test_the_holder_renews_its_lease_by_taking_it_again(self):
        take(self.path, "a", "UV Wizard owner", T0)
        later = T0 + datetime.timedelta(hours=2)
        self.assertEqual(take(self.path, "a", "UV Wizard owner", later), (True, "lease renewed"))
        self.assertEqual(self.holder()["since"], "2026-10-15T19:05:00+00:00")
        # Renewed at 2 h, the lease still blocks another run at 4 h.
        self.assertFalse(take(self.path, "b", "UV Wizard next slice", T0 + datetime.timedelta(hours=4))[0])

    def test_a_stale_lease_is_replaced(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        self.assertEqual(
            take(self.path, "b", "UV Wizard owner", T0 + STALE_AFTER),
            (True, "stale lease replaced (checkout leased by UV Wizard next slice (a) "
                   "since 2026-10-15T17:05:00+00:00)"))
        self.assertEqual(self.holder()["session"], "b")
        self.assertEqual(os.listdir(self.dir.name), ["lease"])

    def test_two_stale_takeovers_give_the_lease_to_one_run(self):
        # Interrupt the first takeover after its first read (the stale lease) and after its
        # second (the re-read once it holds the claim).
        for nth in (1, 2):
            with self.subTest(interrupted_after_read=nth):
                for name in os.listdir(self.dir.name):
                    os.remove(os.path.join(self.dir.name, name))
                take(self.path, "a", "UV Wizard next slice", T0)
                at = T0 + STALE_AFTER
                results = {}
                with self.interrupt_at_read(nth, lambda: results.setdefault(
                        "c", take(self.path, "c", "UV Wizard owner", at))):
                    results["b"] = take(self.path, "b", "UV Wizard next slice", at)
                winners = [s for s, (ok, _) in results.items() if ok]
                self.assertEqual(len(winners), 1, results)
                self.assertEqual(self.holder()["session"], winners[0])
                loser = ({"b", "c"} - set(winners)).pop()
                self.assertEqual(results[loser][0], False)
                self.assertNotIn("replaced", results[loser][1])

    def test_a_takeover_is_refused_when_the_lease_is_rewritten_with_the_same_contents(self):
        open(self.path, "w").close()
        old = self.mtime() - STALE_AFTER
        os.utime(self.path, (old.timestamp(), old.timestamp()))

        def rewrite():
            # A run on an older version of this script recreates an empty lease, just as empty.
            os.remove(self.path)
            open(self.path, "w").close()

        with self.interrupt_at_read(1, rewrite):
            ok, message = take(self.path, "b", "UV Wizard owner", old + STALE_AFTER)
        self.assertFalse(ok)
        self.assertEqual(os.path.getsize(self.path), 0)

    def test_a_takeover_is_refused_when_the_lease_is_released_meanwhile(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        with self.interrupt_at_read(1, lambda: release(self.path, "a")):
            result = take(self.path, "b", "UV Wizard owner", T0 + STALE_AFTER)
        self.assertEqual(result, (False, "the lease changed while taking it; try again"))
        self.assertFalse(os.path.exists(self.path))

    def test_check_passes_for_the_holder_and_fails_for_others(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        self.assertTrue(check(self.path, "a", T0)[0])
        self.assertFalse(check(self.path, "b", T0)[0])
        self.assertFalse(check(self.path, None, T0)[0])

    def test_only_the_holder_releases_it(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        self.assertEqual(release(self.path, "b"), (
            False, "not released: checkout leased by UV Wizard next slice (a) since 2026-10-15T17:05:00+00:00"))
        self.assertEqual(release(self.path, "a"), (True, "lease released"))
        self.assertTrue(check(self.path, "b", T0)[0])

    def test_releasing_with_no_lease_succeeds(self):
        self.assertEqual(release(self.path, "a"), (True, "no lease"))

    def test_a_just_created_empty_lease_is_held(self):
        open(self.path, "w").close()
        self.assertEqual(take(self.path, "a", "UV Wizard owner", self.mtime() + datetime.timedelta(minutes=1)),
                         (False, f"checkout leased by unreadable lease (?) since {self.mtime().isoformat(timespec="seconds")}"))

    def test_an_unreadable_lease_counts_as_stale_once_its_file_is_old(self):
        with open(self.path, "w") as f:
            f.write("{not json")
        self.assertTrue(take(self.path, "a", "UV Wizard owner", self.mtime() + STALE_AFTER)[0])
        self.assertEqual(self.holder()["session"], "a")

    def test_an_abandoned_claim_is_cleared_for_the_next_run(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        at = T0 + STALE_AFTER
        # Run b dies between claiming and replacing the lease, so nothing removes its claim.
        with mock.patch.object(checkout_lease.os, "replace", side_effect=OSError("died mid-takeover")), \
                mock.patch.object(checkout_lease.os, "remove"):
            with self.assertRaises(OSError):
                take(self.path, "b", "UV Wizard owner", at)
        # Within CLAIM_STALE_AFTER of the claim, another run treats it as a takeover in progress.
        claim_at = self.claim_mtime()
        self.assertFalse(take(self.path, "c", "UV Wizard owner", claim_at + datetime.timedelta(seconds=1))[0])
        # After that, the abandoned claim is cleared, and the next run can take over.
        late = claim_at + CLAIM_STALE_AFTER
        self.assertFalse(take(self.path, "c", "UV Wizard owner", late)[0])
        self.assertTrue(take(self.path, "c", "UV Wizard owner", late)[0])
        self.assertEqual(self.holder()["session"], "c")

    def test_the_cli_exits_1_when_refused(self):
        self.assertEqual(main(["--lease-file", self.path, "take", "a", "UV Wizard next slice"]), 0)
        self.assertEqual(main(["--lease-file", self.path, "take", "b", "UV Wizard next slice"]), 1)
        self.assertEqual(main(["--lease-file", self.path, "release", "a"]), 0)

    def mtime(self):
        return datetime.datetime.fromtimestamp(os.stat(self.path).st_mtime, datetime.timezone.utc)

    def claim_mtime(self):
        claims = [n for n in os.listdir(self.dir.name) if ".claim-" in n]
        self.assertEqual(len(claims), 1, claims)
        return datetime.datetime.fromtimestamp(
            os.stat(os.path.join(self.dir.name, claims[0])).st_mtime, datetime.timezone.utc)


if __name__ == "__main__":
    unittest.main()

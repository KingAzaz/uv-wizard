import datetime
import os
import tempfile
import unittest

from checkout_lease import STALE_AFTER, check, main, release, take

T0 = datetime.datetime(2026, 10, 15, 17, 5, tzinfo=datetime.timezone.utc)


class LeaseTest(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.path = os.path.join(self.dir.name, "lease")

    def tearDown(self):
        self.dir.cleanup()

    def test_a_free_checkout_can_be_taken(self):
        self.assertEqual(take(self.path, "a", "UV Wizard next slice", T0), (True, "lease taken"))
        self.assertTrue(os.path.exists(self.path))

    def test_a_second_run_is_refused_while_the_first_waits(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        ok, message = take(self.path, "b", "UV Wizard next slice", T0 + datetime.timedelta(minutes=21))
        self.assertFalse(ok)
        self.assertIn("UV Wizard next slice (a)", message)

    def test_the_holder_can_take_it_again(self):
        take(self.path, "a", "UV Wizard owner", T0)
        self.assertTrue(take(self.path, "a", "UV Wizard owner", T0)[0])

    def test_a_stale_lease_is_replaced(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        ok, message = take(self.path, "b", "UV Wizard owner", T0 + STALE_AFTER)
        self.assertTrue(ok)
        self.assertIn("stale lease replaced", message)
        self.assertFalse(check(self.path, "a", T0 + STALE_AFTER)[0])

    def test_check_passes_for_the_holder_and_fails_for_others(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        self.assertTrue(check(self.path, "a", T0)[0])
        self.assertFalse(check(self.path, "b", T0)[0])
        self.assertFalse(check(self.path, None, T0)[0])

    def test_only_the_holder_releases_it(self):
        take(self.path, "a", "UV Wizard next slice", T0)
        self.assertFalse(release(self.path, "b")[0])
        self.assertTrue(release(self.path, "a")[0])
        self.assertTrue(check(self.path, "b", T0)[0])

    def test_an_unreadable_lease_counts_as_stale(self):
        with open(self.path, "w") as f:
            f.write("{not json")
        self.assertTrue(take(self.path, "a", "UV Wizard owner", T0)[0])

    def test_the_cli_exits_1_when_refused(self):
        self.assertEqual(main(["--lease-file", self.path, "take", "a", "UV Wizard next slice"]), 0)
        self.assertEqual(main(["--lease-file", self.path, "take", "b", "UV Wizard next slice"]), 1)
        self.assertEqual(main(["--lease-file", self.path, "release", "a"]), 0)


if __name__ == "__main__":
    unittest.main()

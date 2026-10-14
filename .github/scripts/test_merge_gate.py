import unittest

from merge_gate import holds


def review(login, state, association="OWNER"):
    return {"user": {"login": login}, "state": state, "author_association": association}


class HoldsTest(unittest.TestCase):
    def test_nothing_holds_a_plain_pr(self):
        self.assertEqual(holds(["owner-agent", "enhancement"], []), [])

    def test_each_hold_label_holds(self):
        for label in ["needs-unity", "unity-failed", "do-not-merge", "ready-for-human"]:
            self.assertEqual(holds(["owner-agent", label], []), [f"label {label}"])

    def test_changes_requested_holds(self):
        self.assertEqual(holds([], [review("KingAzaz", "CHANGES_REQUESTED")]), ["changes requested by KingAzaz"])

    def test_a_later_approval_releases_it(self):
        reviews = [review("KingAzaz", "CHANGES_REQUESTED"), review("KingAzaz", "APPROVED")]
        self.assertEqual(holds([], reviews), [])

    def test_a_later_comment_does_not_release_it(self):
        reviews = [review("KingAzaz", "CHANGES_REQUESTED"), review("KingAzaz", "COMMENTED")]
        self.assertEqual(holds([], reviews), ["changes requested by KingAzaz"])

    def test_a_dismissal_releases_it(self):
        reviews = [review("KingAzaz", "CHANGES_REQUESTED"), review("KingAzaz", "DISMISSED")]
        self.assertEqual(holds([], reviews), [])

    def test_a_stranger_cannot_hold_it(self):
        reviews = [review("someone", "CHANGES_REQUESTED", association="NONE"),
                   review("contributor", "CHANGES_REQUESTED", association="CONTRIBUTOR")]
        self.assertEqual(holds([], reviews), [])

    def test_labels_and_reviews_both_count(self):
        reviews = [review("KingAzaz", "CHANGES_REQUESTED")]
        self.assertEqual(holds(["needs-unity"], reviews), ["label needs-unity", "changes requested by KingAzaz"])


if __name__ == "__main__":
    unittest.main()

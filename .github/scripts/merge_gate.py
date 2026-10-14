"""The Merge gate: fails while something should hold a pull request's merge.

It reads the PR's labels and reviews live from the API, not from the event that started the run,
so a label swap or a review in flight can't leave a stale snapshot that passes.
"""

import json
import os
import sys
import urllib.request

# The labels that hold a merge. The docs point here rather than repeating the list.
HOLD_LABELS = {"needs-unity", "unity-failed", "do-not-merge", "ready-for-human"}

# Only reviews from people with write access count. Anyone can review a public repo's PR.
TRUSTED = {"OWNER", "MEMBER", "COLLABORATOR"}


def holds(labels, reviews):
    """Reasons the merge must wait, given label names and reviews in the order they were made."""
    reasons = [f"label {name}" for name in sorted(HOLD_LABELS & set(labels))]
    latest = {}
    for review in reviews:
        if review.get("author_association") not in TRUSTED:
            continue
        state = review.get("state")
        if state in ("COMMENTED", "PENDING"):
            continue
        latest[review["user"]["login"]] = state
    reasons += [f"changes requested by {login}" for login, state in sorted(latest.items()) if state == "CHANGES_REQUESTED"]
    return reasons


def get(path):
    """Every item of a paginated GitHub REST list, or the single object at path."""
    items, page = [], 1
    while True:
        separator = "&" if "?" in path else "?"
        request = urllib.request.Request(
            f"https://api.github.com/{path}{separator}per_page=100&page={page}",
            headers={"Authorization": f"Bearer {os.environ['GITHUB_TOKEN']}", "Accept": "application/vnd.github+json"},
        )
        with urllib.request.urlopen(request) as response:
            data = json.load(response)
        if not isinstance(data, list):
            return data
        items += data
        if len(data) < 100:
            return items
        page += 1


def main():
    repo, number = os.environ["REPO"], os.environ["PR"]
    pr = get(f"repos/{repo}/pulls/{number}")
    reviews = get(f"repos/{repo}/pulls/{number}/reviews")
    reasons = holds([label["name"] for label in pr["labels"]], reviews)
    if reasons:
        print(f"::error::Holding the merge: {'; '.join(reasons)}")
        sys.exit(1)
    print("Nothing holds this merge.")


if __name__ == "__main__":
    main()

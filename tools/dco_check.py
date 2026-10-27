"""Checks the Developer Certificate of Origin sign-off on every non-merge commit in a range.

A commit passes when it has a `Signed-off-by: Name <email>` trailer whose email matches the
commit's author or committer (case-insensitive). Committer sign-off covers `git rebase --signoff`,
which is how a maintainer certifies commits they are submitting. Dependabot can't sign off and
is exempt.

Usage: python3 tools/dco_check.py <base-sha> <head-sha>
Exit code 0 when every commit passes, 1 otherwise. Standard library only.
"""

from __future__ import annotations

import re
import subprocess
import sys
from dataclasses import dataclass

EXEMPT_AUTHOR_EMAILS = frozenset({"49699333+dependabot[bot]@users.noreply.github.com"})
SIGN_OFF = re.compile(r"^signed-off-by:\s*(?P<name>.+?)\s*<(?P<email>[^<>\s]+@[^<>\s]+)>\s*$", re.IGNORECASE | re.MULTILINE)
FIELD, RECORD = "\x1f", "\x1e"


@dataclass(frozen=True)
class Commit:
    sha: str
    author_email: str
    committer_email: str
    message: str


def problem(commit: Commit) -> str | None:
    """Returns why the commit fails the DCO check, or None when it passes."""
    if commit.author_email.lower() in EXEMPT_AUTHOR_EMAILS:
        return None
    signers = {m.group("email").lower() for m in SIGN_OFF.finditer(commit.message)}
    if not signers:
        return "no Signed-off-by line"
    if not signers & {commit.author_email.lower(), commit.committer_email.lower()}:
        return "Signed-off-by email matches neither the author nor the committer"
    return None


def commits_between(base: str, head: str) -> list[Commit]:
    log = subprocess.run(
        ["git", "log", "--no-merges", f"--format=%H{FIELD}%ae{FIELD}%ce{FIELD}%B{RECORD}", f"{base}..{head}"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout
    commits = []
    for record in log.split(RECORD):
        if record.strip():
            sha, author, committer, message = record.strip("\n").split(FIELD, 3)
            commits.append(Commit(sha, author, committer, message))
    return commits


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__)
        return 2
    commits = commits_between(*argv)
    failures = [(c, reason) for c in commits if (reason := problem(c))]
    print(f"Checked {len(commits)} commit(s); {len(failures)} without a valid sign-off.")
    for commit, reason in failures:
        print(f"::error title=DCO sign-off missing::{commit.sha[:7]}: {reason}. Fix: git rebase --signoff {argv[0][:7]} && git push --force-with-lease")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

import unittest

from dco_check import Commit, problem

AUTHOR = "author@example.org"


def commit(message: str, author: str = AUTHOR, committer: str = AUTHOR) -> Commit:
    return Commit("0123456789abcdef", author, committer, message)


class DcoCheckTests(unittest.TestCase):
    def test_sign_off_by_author_passes(self):
        self.assertIsNone(problem(commit("Fix\n\nSigned-off-by: A Person <author@example.org>\n")))

    def test_sign_off_is_case_insensitive(self):
        self.assertIsNone(problem(commit("Fix\n\nsigned-off-by: A Person <AUTHOR@example.org>")))

    def test_missing_sign_off_fails(self):
        self.assertEqual(problem(commit("Fix\n\nCo-Authored-By: Someone <x@example.org>")), "no Signed-off-by line")

    def test_sign_off_by_a_stranger_fails(self):
        self.assertEqual(
            problem(commit("Fix\n\nSigned-off-by: Other <other@example.org>")),
            "Signed-off-by email matches neither the author nor the committer",
        )

    def test_committer_sign_off_passes_after_rebase_signoff(self):
        message = "Fix\n\nSigned-off-by: Maintainer <maintainer@example.org>"
        self.assertIsNone(problem(commit(message, committer="maintainer@example.org")))

    def test_sign_off_must_be_its_own_line(self):
        self.assertEqual(
            problem(commit("Fix: mention Signed-off-by: A <author@example.org> inline")), "no Signed-off-by line"
        )

    def test_dependabot_is_exempt(self):
        self.assertIsNone(problem(commit("Bump x", author="49699333+dependabot[bot]@users.noreply.github.com")))


if __name__ == "__main__":
    unittest.main()

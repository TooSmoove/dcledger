import os
import subprocess
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parent / "refuse_r2_credentials.sh"
BASE_ENV = {"PATH": os.environ.get("PATH", "/usr/bin:/bin")}


def run(**env: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["bash", str(SCRIPT)], env={**BASE_ENV, **env}, capture_output=True, text=True, check=False)


class RefuseR2CredentialsTests(unittest.TestCase):
    def test_a_clean_job_passes(self):
        result = run(SECRETS_PRESENT="false", R2_ACCOUNT_ID="abc", CLOUDFLARE_API_TOKEN="pages-token")
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_a_readable_secret_fails(self):
        result = run(SECRETS_PRESENT="true")
        self.assertEqual(result.returncode, 1)
        self.assertIn("R2_PRIVATE_* secret is readable here", result.stdout)

    def test_credential_variables_fail_and_are_named_without_values(self):
        for name in ("R2_PRIVATE_ACCESS_KEY_ID", "R2_PRIVATE_SECRET_ACCESS_KEY", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN"):
            with self.subTest(name=name):
                result = run(SECRETS_PRESENT="false", **{name: "do-not-print-me"})
                self.assertEqual(result.returncode, 1)
                self.assertIn(f"environment variable {name} is set", result.stdout)
                self.assertNotIn("do-not-print-me", result.stdout + result.stderr)

    def test_a_missing_or_odd_flag_fails_closed(self):
        self.assertEqual(run().returncode, 1)
        self.assertEqual(run(SECRETS_PRESENT="yes").returncode, 1)


if __name__ == "__main__":
    unittest.main()

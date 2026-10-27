import json
import tempfile
import unittest
from pathlib import Path

from license_check import Package, license_allowed, npm_packages, nuget_packages

POLICY = {
    "allowed": ["MIT", "Apache-2.0", "BSD-3-Clause"],
    "exceptions": [{"ecosystem": "npm", "package": "@img/sharp-*", "license": "LGPL-3.0-or-later", "reason": "test"}],
}


def allowed(ecosystem: str, name: str, license: str) -> bool:
    return license_allowed(Package(ecosystem, name, "1.0.0", license), POLICY)


class LicenseAllowedTests(unittest.TestCase):
    def test_allowed_single_license_passes(self):
        self.assertTrue(allowed("nuget", "Shouldly", "BSD-3-Clause"))

    def test_fee_bearing_file_license_fails(self):
        self.assertFalse(allowed("nuget", "JsonPath.Net", "file:OSMFEULA.txt"))

    def test_url_or_missing_license_fails(self):
        self.assertFalse(allowed("nuget", "Fare", "url:https://example.org/LICENSE"))
        self.assertFalse(allowed("npm", "mystery", "none"))

    def test_or_needs_one_allowed_alternative(self):
        self.assertTrue(allowed("npm", "dual", "(GPL-3.0-only OR MIT)"))
        self.assertFalse(allowed("npm", "dual", "GPL-3.0-only OR AGPL-3.0-only"))

    def test_and_needs_every_term_allowed(self):
        self.assertFalse(allowed("npm", "combo", "MIT AND LGPL-3.0-or-later"))

    def test_exception_applies_only_to_matching_packages(self):
        self.assertTrue(allowed("npm", "@img/sharp-libvips-linux-x64", "LGPL-3.0-or-later"))
        self.assertTrue(allowed("npm", "@img/sharp-wasm32", "Apache-2.0 AND LGPL-3.0-or-later AND MIT"))
        self.assertFalse(allowed("npm", "other-lgpl", "LGPL-3.0-or-later"))
        self.assertFalse(allowed("nuget", "@img/sharp-x", "LGPL-3.0-or-later"))


class ReaderTests(unittest.TestCase):
    def test_npm_reader_skips_dev_and_root_entries(self):
        lock = {
            "lockfileVersion": 3,
            "packages": {
                "": {"name": "site"},
                "node_modules/astro": {"version": "7.4.2", "license": "MIT"},
                "node_modules/prettier": {"version": "3.9.10", "license": "MIT", "dev": True},
                "node_modules/x/node_modules/y": {"version": "1.0.0"},
            },
        }
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "package-lock.json")
            path.write_text(json.dumps(lock), encoding="utf-8")
            packages = npm_packages(path)
        self.assertEqual([(p.name, p.license) for p in packages], [("astro", "MIT"), ("y", "none")])

    def test_nuget_reader_reads_nuspec_expression_and_skips_projects(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, cache = Path(tmp, "repo"), Path(tmp, "cache")
            lock_dir = repo / "src" / "App"
            lock_dir.mkdir(parents=True)
            (lock_dir / "packages.lock.json").write_text(
                json.dumps(
                    {
                        "version": 2,
                        "dependencies": {
                            "net10.0": {
                                "Shouldly": {"type": "Direct", "resolved": "4.3.0"},
                                "dcledger.domain": {"type": "Project"},
                            }
                        },
                    }
                ),
                encoding="utf-8",
            )
            nuspec_dir = cache / "shouldly" / "4.3.0"
            nuspec_dir.mkdir(parents=True)
            (nuspec_dir / "shouldly.nuspec").write_text(
                '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>'
                '<license type="expression">BSD-3-Clause</license></metadata></package>',
                encoding="utf-8",
            )
            packages = nuget_packages(repo, cache)
        self.assertEqual([(p.name, p.license) for p in packages], [("Shouldly", "BSD-3-Clause")])


if __name__ == "__main__":
    unittest.main()

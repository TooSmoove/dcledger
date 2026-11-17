import datetime as dt
import json
import unittest
from pathlib import Path

from npm_audit_gate import Advisory, Exception_, advisories, gate, load_exceptions

TODAY = dt.date(2026, 11, 17)
PICOMATCH = "GHSA-fc5q-39xc-vjpf"
OTHER = "GHSA-c475-qrg2-pj4r"


def report(*vias: tuple[str, str, str]) -> dict:
    """An `npm audit --json` report: each (package, ghsa, severity), plus a dependent listing it by name."""
    vulnerabilities = {}
    for package, ghsa, severity in vias:
        vulnerabilities.setdefault(package, {"via": []})["via"].append(
            {"source": 1, "name": package, "title": "t", "url": f"https://github.com/advisories/{ghsa}", "severity": severity}
        )
        vulnerabilities[f"depends-on-{package}"] = {"via": [package]}
    return {"auditReportVersion": 2, "vulnerabilities": vulnerabilities}


def exception(ghsa: str = PICOMATCH, package: str = "picomatch", expires: dt.date = dt.date(2026, 12, 31)) -> Exception_:
    return Exception_(ghsa, package, "build time only", expires)


class AdvisoriesTests(unittest.TestCase):
    def test_reads_each_advisory_once_and_skips_name_only_entries(self):
        found = advisories(report(("picomatch", PICOMATCH, "high")))
        self.assertEqual(found, [Advisory(PICOMATCH, "picomatch", "high", "t")])

    def test_a_clean_report_has_none(self):
        self.assertEqual(advisories({"auditReportVersion": 2, "vulnerabilities": {}}), [])

    def test_an_npm_error_is_not_a_clean_report(self):
        with self.assertRaises(ValueError):
            advisories({"error": {"code": "ENOTFOUND"}})
        with self.assertRaises(ValueError):
            advisories({})


class GateTests(unittest.TestCase):
    def test_an_excepted_advisory_passes_until_it_expires(self):
        found = advisories(report(("picomatch", PICOMATCH, "high")))
        failures, notes = gate(found, [exception()], TODAY)
        self.assertEqual(failures, [])
        self.assertIn("excepted until 2026-12-31", notes[0])

    def test_the_expiry_day_still_passes_and_the_next_day_fails(self):
        found = advisories(report(("picomatch", PICOMATCH, "high")))
        self.assertEqual(gate(found, [exception()], dt.date(2026, 12, 31))[0], [])
        self.assertIn("exception expired 2026-12-31", gate(found, [exception()], dt.date(2027, 1, 1))[0][0])

    def test_any_other_advisory_fails(self):
        found = advisories(report(("picomatch", PICOMATCH, "high"), ("basic-ftp", OTHER, "low")))
        failures, _ = gate(found, [exception()], TODAY)
        self.assertEqual(len(failures), 1)
        self.assertIn("basic-ftp: GHSA-c475-qrg2-pj4r (low)", failures[0])

    def test_an_exception_covers_only_its_package(self):
        found = advisories(report(("anymatch", PICOMATCH, "high")))
        self.assertEqual(len(gate(found, [exception()], TODAY)[0]), 1)

    def test_an_unused_exception_is_reported_for_removal(self):
        _, notes = gate([], [exception()], TODAY)
        self.assertEqual(notes, [f"picomatch: {PICOMATCH} no longer reported; remove its exception"])


class PolicyTests(unittest.TestCase):
    def test_the_committed_policy_loads(self):
        policy = json.loads((Path(__file__).parent / "audit-exceptions.json").read_text(encoding="utf-8"))
        self.assertTrue(load_exceptions(policy), "an empty policy file should be deleted along with the gate")

    def test_an_exception_without_a_reason_is_rejected(self):
        with self.assertRaises(ValueError):
            load_exceptions({"exceptions": [{"ghsa": PICOMATCH, "package": "picomatch", "reason": " ", "expires": "2026-12-31"}]})


if __name__ == "__main__":
    unittest.main()

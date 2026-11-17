"""Fails CI on any npm advisory except a reviewed exception that hasn't expired.

`npm audit` has no way to accept one advisory, so the audit job pipes `npm audit --json` here
instead of failing on its exit code. Every advisory fails unless tools/audit-exceptions.json lists
its GHSA id for that package with a reason and an expiry date; an expired exception fails like no
exception. An exception that no longer matches anything is reported, so it gets removed once the
fix ships. A report npm couldn't produce (an `error` key, or no `vulnerabilities`) fails too:
no answer is not a clean answer.

Usage: npm audit --omit=dev --json | python3 tools/npm_audit_gate.py tools/audit-exceptions.json
Exit code 0 when nothing unexcepted is reported, 1 otherwise. Standard library only.
"""

from __future__ import annotations

import datetime as dt
import json
import re
import sys
from dataclasses import dataclass

GHSA = re.compile(r"GHSA(?:-[23456789cfghjmpqrvwx]{4}){3}")


@dataclass(frozen=True)
class Advisory:
    ghsa: str
    package: str
    severity: str
    title: str


@dataclass(frozen=True)
class Exception_:
    ghsa: str
    package: str
    reason: str
    expires: dt.date


def advisories(report: dict) -> list[Advisory]:
    """The advisories in an `npm audit --json` report (lockfile v2+), one per package and id."""
    if "error" in report or "vulnerabilities" not in report:
        raise ValueError("npm audit produced no report: " + json.dumps(report.get("error", report))[:300])
    found: dict[tuple[str, str], Advisory] = {}
    for package, entry in report["vulnerabilities"].items():
        for via in entry.get("via", []):
            if not isinstance(via, dict):
                continue  # a package name: the advisory is listed under that package itself
            match = GHSA.search(str(via.get("url", "")))
            ghsa = match.group(0) if match else str(via.get("source", "unknown"))
            name = str(via.get("name", package))
            found[(name, ghsa)] = Advisory(ghsa, name, str(via.get("severity", "unknown")), str(via.get("title", "")))
    return sorted(found.values(), key=lambda a: (a.package, a.ghsa))


def load_exceptions(policy: dict) -> list[Exception_]:
    exceptions = []
    for item in policy.get("exceptions", []):
        ghsa, package, reason = item.get("ghsa", ""), item.get("package", ""), item.get("reason", "").strip()
        if not GHSA.fullmatch(ghsa) or not package or not reason:
            raise ValueError(f"exception needs a GHSA id, a package and a reason: {item!r}")
        exceptions.append(Exception_(ghsa, package, reason, dt.date.fromisoformat(item["expires"])))
    return exceptions


def gate(found: list[Advisory], exceptions: list[Exception_], today: dt.date) -> tuple[list[str], list[str]]:
    """Returns (failures, notes)."""
    failures, notes, used = [], [], set()
    for advisory in found:
        match = next((e for e in exceptions if (e.ghsa, e.package) == (advisory.ghsa, advisory.package)), None)
        label = f"{advisory.package}: {advisory.ghsa} ({advisory.severity}) {advisory.title}"
        if match is None:
            failures.append(f"{label}: no exception")
        elif today > match.expires:
            failures.append(f"{label}: exception expired {match.expires.isoformat()}; review it again")
        else:
            used.add(match)
            notes.append(f"{label}: excepted until {match.expires.isoformat()} ({match.reason})")
    for unused in (e for e in exceptions if e not in used):
        notes.append(f"{unused.package}: {unused.ghsa} no longer reported; remove its exception")
    return failures, notes


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    with open(argv[1], encoding="utf-8") as policy_file:
        exceptions = load_exceptions(json.load(policy_file))
    try:
        found = advisories(json.load(sys.stdin))
    except (ValueError, json.JSONDecodeError) as error:
        print(f"::error::{error}")
        return 1
    failures, notes = gate(found, exceptions, dt.datetime.now(dt.timezone.utc).date())
    for note in notes:
        print(f"::warning::{note}" if "remove its exception" in note else note)
    for failure in failures:
        print(f"::error::{failure}")
    print(f"{len(found)} advisories, {len(failures)} failing.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

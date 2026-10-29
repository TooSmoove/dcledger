"""Builds ingest-status.json for `dcl export --status` from ingest run history (ADR-0004, issue #23).

"Last checked" for an automated jurisdiction is when its newest successful `ingest (<id>)` job finished, in an
`ingest.yml` run on main of this repository, started by its schedule or a dispatch. Dry runs don't count: they store
nothing and open no issue, so a relevant agenda they saw would never reach review. ingest.yml names them
"ingest (dry run)". Every property of a run is checked here, on the client, as in cli_artifact.py.

History is searched back LOOKBACK_DAYS; a jurisdiction with no successful job in that time is written with
`last_checked_utc: null` and shows as never checked. The search stops as soon as every jurisdiction has been seen, so
a normal run costs a few API calls.

Usage:
  GH_TOKEN=... python3 -I tools/ingest_status.py --repo OWNER/REPO --data build/data --out ingest-status.json
Exit code 0 on success, 1 when the API fails, 2 on bad configuration. Standard library only.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections.abc import Iterable, Mapping
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

# `python3 -I` leaves the script's own directory off sys.path; this is our own tools/ folder, not the working
# directory, so adding it back is safe.
sys.path.insert(0, str(Path(__file__).resolve().parent))

from cli_artifact import Api, CheckFailed, ConfigError, GitHubApi, parse_time  # noqa: E402

SCHEMA_VERSION = 1
WORKFLOW_FILE = "ingest.yml"
WORKFLOW_PATH = f".github/workflows/{WORKFLOW_FILE}"
BRANCH = "main"
EVENTS = frozenset({"schedule", "workflow_dispatch"})
DRY_RUN_TITLE = "ingest (dry run)"
LOOKBACK_DAYS = 30
MAX_PAGES = 5  # 500 runs; at every 6 hours, 30 days is about 120

JOB_NAME = re.compile(r"^ingest \((?P<id>[a-z0-9]+(?:-[a-z0-9]+)*)\)$")
REPOSITORY = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
# Same rule as the plan job in ingest.yml: a jurisdiction is automated when its YAML says `platform: legistar`.
AUTOMATED = re.compile(r'^platform:[ \t]*"?legistar"?[ \t]*(#.*)?$', re.MULTILINE)


def automated_jurisdictions(data_dir: Path) -> list[str]:
    """IDs of the jurisdictions ingest runs for, from data/jurisdictions/*.yaml, sorted."""
    folder = data_dir / "jurisdictions"
    if not folder.is_dir():
        raise ConfigError(f"{folder} is not a directory")
    return sorted(path.stem for path in folder.glob("*.yaml") if AUTOMATED.search(path.read_text(encoding="utf-8")))


def run_counts(run: Mapping[str, Any], repository: str) -> bool:
    """True when the run is an ingest run on main of this repository, started by schedule or dispatch, not dry."""
    head_repository = (run.get("head_repository") or {}).get("full_name") or ""
    return (
        run.get("path") == WORKFLOW_PATH
        and run.get("head_branch") == BRANCH
        and run.get("event") in EVENTS
        and head_repository.lower() == repository.lower()
        and run.get("display_title") != DRY_RUN_TITLE
    )


def successful_checks(jobs: Iterable[Mapping[str, Any]]) -> dict[str, datetime]:
    """Jurisdiction ID -> when its newest successful ingest job in `jobs` finished."""
    found: dict[str, datetime] = {}
    for job in jobs:
        match = JOB_NAME.match(job.get("name") or "")
        finished = parse_time(job.get("completed_at"))
        if match and job.get("conclusion") == "success" and finished is not None:
            jurisdiction = match.group("id")
            if jurisdiction not in found or finished > found[jurisdiction]:
                found[jurisdiction] = finished
    return found


def last_checked(api: Api, repository: str, jurisdictions: list[str], now: datetime) -> dict[str, datetime | None]:
    """Newest successful check per jurisdiction within the lookback; None where there is none."""
    since = now - timedelta(days=LOOKBACK_DAYS)
    wanted = set(jurisdictions)
    found: dict[str, datetime] = {}
    path = f"repos/{repository}/actions/workflows/{WORKFLOW_FILE}/runs"
    for page in range(1, MAX_PAGES + 1):
        params = {
            "branch": BRANCH,
            "status": "completed",
            "created": f">={since:%Y-%m-%d}",
            "per_page": "100",
            "page": str(page),
        }
        runs = api.get(path, params).get("workflow_runs") or []
        for run in sorted(runs, key=lambda r: r.get("created_at") or "", reverse=True):
            created = parse_time(run.get("created_at"))
            if created is None or created < since or not run_counts(run, repository):
                continue
            jobs = api.get(f"repos/{repository}/actions/runs/{int(run['id'])}/jobs", {"filter": "all", "per_page": "100"})
            for jurisdiction, finished in successful_checks(jobs.get("jobs") or []).items():
                if jurisdiction in wanted and (jurisdiction not in found or finished > found[jurisdiction]):
                    found[jurisdiction] = finished
            if wanted <= found.keys():
                return {j: found.get(j) for j in jurisdictions}
        if len(runs) < 100:
            break
    return {j: found.get(j) for j in jurisdictions}


def utc_text(value: datetime) -> str:
    return value.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


def status_document(checks: Mapping[str, datetime | None], generated: datetime) -> dict[str, Any]:
    """The ingest-status.json object (docs/export.md), jurisdictions sorted by ID."""
    return {
        "schema_version": SCHEMA_VERSION,
        "generated_utc": utc_text(generated),
        "jurisdictions": [
            {"id": jurisdiction, "last_checked_utc": None if checked is None else utc_text(checked)}
            for jurisdiction, checked in sorted(checks.items())
        ],
    }


def summary(document: Mapping[str, Any]) -> str:
    rows = "".join(
        f"| `{entry['id']}` | {entry['last_checked_utc'] or 'none in the last ' + str(LOOKBACK_DAYS) + ' days'} |\n"
        for entry in document["jurisdictions"]
    )
    return f"### Ingest status\n\nAs of {document['generated_utc']}.\n\n| Jurisdiction | Last checked (UTC) |\n|---|---|\n{rows}"


def main(argv: list[str], env: Mapping[str, str] | None = None, *, api: Api | None = None, now: datetime | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo", required=True, help="OWNER/REPO")
    parser.add_argument("--data", required=True, help="the ledger data directory")
    parser.add_argument("--out", required=True, help="where to write ingest-status.json")
    args = parser.parse_args(argv)
    env = os.environ if env is None else env
    try:
        if not REPOSITORY.match(args.repo):
            raise ConfigError(f"--repo must be OWNER/REPO, got {args.repo!r}")
        jurisdictions = automated_jurisdictions(Path(args.data))
        client = api or GitHubApi(
            env.get("GH_TOKEN", ""), env.get("GITHUB_API_URL", "https://api.github.com"), user_agent="dc-ledger-publish"
        )
        generated = (now or datetime.now(UTC)).replace(microsecond=0)
        document = status_document(last_checked(client, args.repo, jurisdictions, generated), generated)
    except ConfigError as error:
        print(f"::error title=ingest-status::{error}")
        return 2
    except CheckFailed as error:
        print(f"::error title=ingest-status::{error}")
        return 1

    Path(args.out).write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(document, indent=2))
    summary_file = env.get("GITHUB_STEP_SUMMARY")
    if summary_file:
        with open(summary_file, "a", encoding="utf-8") as stream:
            stream.write(summary(document))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

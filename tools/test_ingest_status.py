import json
import tempfile
import unittest
from datetime import UTC, datetime
from pathlib import Path

from ingest_status import (
    DRY_RUN_TITLE,
    automated_jurisdictions,
    last_checked,
    main,
    run_counts,
    status_document,
    successful_checks,
)

REPO = "TooSmoove/dcledger"
NOW = datetime(2026, 10, 29, 14, 47, 12, tzinfo=UTC)


def run(run_id: int, created: str, **changes) -> dict:
    base = {
        "id": run_id,
        "path": ".github/workflows/ingest.yml",
        "head_branch": "main",
        "event": "schedule",
        "head_repository": {"full_name": REPO},
        "display_title": "ingest",
        "created_at": created,
    }
    base.update(changes)
    return base


def job(name: str, conclusion: str, completed: str | None) -> dict:
    return {"name": name, "conclusion": conclusion, "completed_at": completed}


class FakeApi:
    """Serves workflow runs (one page) and each run's jobs; records every path asked for."""

    def __init__(self, runs: list[dict], jobs: dict[int, list[dict]]) -> None:
        self.runs = runs
        self.jobs = jobs
        self.calls: list[tuple[str, dict]] = []

    def get(self, path, params=None):
        self.calls.append((path, dict(params or {})))
        if path.endswith("/runs"):
            page = int((params or {}).get("page", "1"))
            return {"workflow_runs": self.runs if page == 1 else []}
        run_id = int(path.split("/")[-2])
        return {"jobs": self.jobs.get(run_id, [])}

    def get_all(self, path, key):  # pragma: no cover - not used
        raise AssertionError("not used")

    def job_calls(self) -> list[str]:
        return [path for path, _ in self.calls if path.endswith("/jobs")]


class AutomatedJurisdictionsTests(unittest.TestCase):
    def test_only_legistar_jurisdictions_count(self):
        with tempfile.TemporaryDirectory() as data:
            folder = Path(data, "jurisdictions")
            folder.mkdir()
            Path(folder, "guilford.yaml").write_text("id: guilford\nplatform: legistar\n")
            Path(folder, "winston-salem.yaml").write_text('id: winston-salem\nplatform: "legistar"  # automated\n')
            Path(folder, "greensboro.yaml").write_text("id: greensboro\nplatform: manual\n")
            Path(folder, "notes.txt").write_text("platform: legistar\n")

            self.assertEqual(automated_jurisdictions(Path(data)), ["guilford", "winston-salem"])

    def test_a_missing_folder_is_a_configuration_error(self):
        with tempfile.TemporaryDirectory() as data:
            self.assertEqual(main(["--repo", REPO, "--data", data, "--out", str(Path(data, "s.json"))], {}, api=FakeApi([], {})), 2)


class RunFilterTests(unittest.TestCase):
    def test_a_scheduled_or_dispatched_run_on_main_counts(self):
        self.assertTrue(run_counts(run(1, "2026-10-29T12:17:00Z"), REPO))
        self.assertTrue(run_counts(run(1, "2026-10-29T12:17:00Z", event="workflow_dispatch"), REPO))

    def test_dry_runs_other_branches_events_repositories_and_workflows_do_not(self):
        rejected = [
            {"display_title": DRY_RUN_TITLE},
            {"head_branch": "feature"},
            {"event": "pull_request"},
            {"event": "push"},
            {"head_repository": {"full_name": "someone/dcledger"}},
            {"head_repository": None},
            {"path": ".github/workflows/other.yml"},
        ]
        for changes in rejected:
            with self.subTest(changes=changes):
                self.assertFalse(run_counts(run(1, "2026-10-29T12:17:00Z", **changes), REPO))


class SuccessfulChecksTests(unittest.TestCase):
    def test_newest_successful_job_per_jurisdiction(self):
        jobs = [
            job("plan", "success", "2026-10-29T12:17:30Z"),
            job("ingest (guilford)", "success", "2026-10-29T12:19:00Z"),
            job("ingest (guilford)", "success", "2026-10-29T13:01:00Z"),  # a re-run attempt
            job("ingest (winston-salem)", "failure", "2026-10-29T12:20:00Z"),
            job("ingest (forsyth)", "success", None),
            job("ingest (Bad Name)", "success", "2026-10-29T12:20:00Z"),
        ]

        self.assertEqual(successful_checks(jobs), {"guilford": datetime(2026, 10, 29, 13, 1, tzinfo=UTC)})


class LastCheckedTests(unittest.TestCase):
    def test_stops_once_every_jurisdiction_is_found(self):
        api = FakeApi(
            [run(3, "2026-10-29T12:17:00Z"), run(2, "2026-10-29T06:17:00Z"), run(1, "2026-10-29T00:17:00Z")],
            {
                3: [job("ingest (guilford)", "success", "2026-10-29T12:19:00Z")],
                2: [job("ingest (winston-salem)", "success", "2026-10-29T06:21:00Z")],
                1: [job("ingest (guilford)", "success", "2026-10-29T00:19:00Z")],
            },
        )

        checks = last_checked(api, REPO, ["guilford", "winston-salem"], NOW)

        self.assertEqual(
            checks,
            {
                "guilford": datetime(2026, 10, 29, 12, 19, tzinfo=UTC),
                "winston-salem": datetime(2026, 10, 29, 6, 21, tzinfo=UTC),
            },
        )
        self.assertEqual(len(api.job_calls()), 2)

    def test_runs_are_read_newest_first_whatever_the_api_order(self):
        api = FakeApi(
            [run(1, "2026-10-28T00:17:00Z"), run(2, "2026-10-29T12:17:00Z")],
            {
                1: [job("ingest (guilford)", "success", "2026-10-28T00:19:00Z")],
                2: [job("ingest (guilford)", "success", "2026-10-29T12:19:00Z")],
            },
        )

        self.assertEqual(last_checked(api, REPO, ["guilford"], NOW)["guilford"], datetime(2026, 10, 29, 12, 19, tzinfo=UTC))

    def test_a_failing_jurisdiction_keeps_its_last_success_and_dry_runs_are_skipped(self):
        api = FakeApi(
            [
                run(4, "2026-10-29T13:00:00Z", event="workflow_dispatch", display_title=DRY_RUN_TITLE),
                run(3, "2026-10-29T12:17:00Z"),
                run(2, "2026-10-27T12:17:00Z"),
            ],
            {
                4: [job("ingest (guilford)", "success", "2026-10-29T13:02:00Z")],
                3: [job("ingest (guilford)", "failure", "2026-10-29T12:19:00Z")],
                2: [job("ingest (guilford)", "success", "2026-10-27T12:19:00Z")],
            },
        )

        checks = last_checked(api, REPO, ["guilford"], NOW)

        self.assertEqual(checks["guilford"], datetime(2026, 10, 27, 12, 19, tzinfo=UTC))
        self.assertNotIn("repos/TooSmoove/dcledger/actions/runs/4/jobs", api.job_calls())

    def test_nothing_within_the_lookback_is_none_and_old_runs_are_ignored(self):
        api = FakeApi(
            [run(1, "2026-09-20T12:17:00Z")],
            {1: [job("ingest (guilford)", "success", "2026-09-20T12:19:00Z")]},
        )

        self.assertEqual(last_checked(api, REPO, ["guilford"], NOW), {"guilford": None})
        self.assertEqual(api.job_calls(), [])

    def test_the_runs_query_is_filtered_to_main_completed_and_the_lookback(self):
        api = FakeApi([], {})

        last_checked(api, REPO, ["guilford"], NOW)

        path, params = api.calls[0]
        self.assertEqual(path, "repos/TooSmoove/dcledger/actions/workflows/ingest.yml/runs")
        self.assertEqual(params["branch"], "main")
        self.assertEqual(params["status"], "completed")
        self.assertEqual(params["created"], ">=2026-09-29")


class DocumentTests(unittest.TestCase):
    def test_document_matches_the_export_contract(self):
        document = status_document(
            {"winston-salem": None, "guilford": datetime(2026, 10, 29, 12, 19, 5, 123000, tzinfo=UTC)},
            NOW,
        )

        self.assertEqual(
            document,
            {
                "schema_version": 1,
                "generated_utc": "2026-10-29T14:47:12Z",
                "jurisdictions": [
                    {"id": "guilford", "last_checked_utc": "2026-10-29T12:19:05Z"},
                    {"id": "winston-salem", "last_checked_utc": None},
                ],
            },
        )

    def test_main_writes_the_file_and_the_summary(self):
        with tempfile.TemporaryDirectory() as temp:
            data = Path(temp, "data", "jurisdictions")
            data.mkdir(parents=True)
            Path(data, "guilford.yaml").write_text("platform: legistar\n")
            out = Path(temp, "ingest-status.json")
            summary = Path(temp, "summary.md")
            api = FakeApi(
                [run(1, "2026-10-29T12:17:00Z")],
                {1: [job("ingest (guilford)", "success", "2026-10-29T12:19:00Z")]},
            )

            code = main(
                ["--repo", REPO, "--data", str(data.parent), "--out", str(out)],
                {"GITHUB_STEP_SUMMARY": str(summary)},
                api=api,
                now=NOW.replace(microsecond=456),
            )

            self.assertEqual(code, 0)
            written = json.loads(out.read_text())
            self.assertEqual(written["generated_utc"], "2026-10-29T14:47:12Z")
            self.assertEqual(written["jurisdictions"], [{"id": "guilford", "last_checked_utc": "2026-10-29T12:19:00Z"}])
            self.assertIn("| `guilford` | 2026-10-29T12:19:00Z |", summary.read_text())

    def test_a_bad_repository_is_a_configuration_error(self):
        with tempfile.TemporaryDirectory() as temp:
            self.assertEqual(main(["--repo", "nope", "--data", temp, "--out", str(Path(temp, "s.json"))], {}, api=FakeApi([], {})), 2)


if __name__ == "__main__":
    unittest.main()

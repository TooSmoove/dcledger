import hashlib
import io
import json
import os
import re
import stat
import subprocess
import tarfile
import tempfile
import unittest
import urllib.error
from datetime import UTC, datetime, timedelta
from email.message import Message
from pathlib import Path

from cli_artifact import (
    CheckFailed,
    ConfigError,
    GitHubApi,
    Policy,
    Workflow,
    artifact_name,
    attestation_command,
    attested_run_ids,
    check_digest,
    check_version,
    escape_data,
    main,
    moved_since,
    pack,
    parse_run_id,
    pick_artifact,
    run_command,
    run_rejection,
    select,
    single_download,
    staleness,
    unpack,
    without_secrets,
)

REPO = "TooSmoove/dcledger"
SHA = "a" * 40
OLDER_SHA = "b" * 40
FILE_DIGEST = "c" * 64
NOW = datetime(2026, 10, 28, 12, 0, tzinfo=UTC)
EVENTS = frozenset({"push", "schedule", "workflow_dispatch"})
POLICY = Policy(REPO, "ci.yml", "main", EVENTS, max_ahead_days=7, min_expiry_days=14)


def a_run(**overrides):
    run = {
        "id": 200,
        "status": "completed",
        "conclusion": "success",
        "repository": {"full_name": REPO},
        "head_repository": {"full_name": REPO},
        "head_branch": "main",
        "event": "push",
        "path": ".github/workflows/ci.yml",
        "head_sha": SHA,
        "created_at": "2026-10-28T10:00:00Z",
        "updated_at": "2026-10-28T10:05:00Z",
        "run_attempt": 1,
    }
    run.update(overrides)
    return run


def an_artifact(sha=SHA, attempt=1, **overrides):
    artifact = {
        "id": 9000 + attempt,
        "name": artifact_name(sha, attempt),
        "expired": False,
        "digest": f"sha256:{FILE_DIGEST}",
        "expires_at": "2027-01-26T10:00:00Z",
        "size_in_bytes": 1234,
    }
    artifact.update(overrides)
    return artifact


def a_compare(status="identical", ahead_by=0, commits=()):
    return {"status": status, "ahead_by": ahead_by, "commits": list(commits)}


def a_commit(sha, parent, date, *more_parents):
    parents = [{"sha": parent}] + [{"sha": other} for other in more_parents]
    return {"sha": sha, "parents": parents, "commit": {"committer": {"date": date}}}


def at(year, month, day, hour):
    return datetime(year, month, day, hour, tzinfo=UTC)


class FakeApi:
    """Serves workflow runs, compares and artifacts from dictionaries and records each request."""

    def __init__(self, runs=(), compares=None, artifacts=None):
        self.runs = list(runs)
        self.compares = compares or {}
        self.artifacts = artifacts or {}
        self.calls = []

    def get(self, path, params=None):
        self.calls.append((path, dict(params or {})))
        if path == f"repos/{REPO}/actions/workflows/ci.yml/runs":
            return {"workflow_runs": self.runs}
        if match := re.fullmatch(rf"repos/{REPO}/actions/runs/(\d+)", path):
            return next(run for run in self.runs if run["id"] == int(match[1]))
        if match := re.fullmatch(rf"repos/{REPO}/compare/([0-9a-f]{{40}})\.\.\.main", path):
            return self.compares.get(match[1], a_compare())
        raise AssertionError(f"unexpected GET {path}")

    def get_all(self, path, key):
        self.calls.append((path, {}))
        match = re.fullmatch(rf"repos/{REPO}/actions/runs/(\d+)/artifacts", path)
        assert match and key == "artifacts", path
        return self.artifacts.get(int(match[1]), [])


class RunRejectionTests(unittest.TestCase):
    def test_green_push_on_main_is_eligible(self):
        self.assertIsNone(run_rejection(a_run(), POLICY))

    def test_scheduled_and_dispatched_runs_are_eligible(self):
        for event in ("schedule", "workflow_dispatch"):
            with self.subTest(event=event):
                self.assertIsNone(run_rejection(a_run(event=event), POLICY))

    def test_repository_names_compare_case_insensitively(self):
        self.assertIsNone(run_rejection(a_run(head_repository={"full_name": REPO.lower()}), POLICY))

    def test_fork_branch_named_main_is_rejected(self):
        reason = run_rejection(a_run(head_repository={"full_name": "attacker/dcledger"}), POLICY)
        self.assertIn("attacker/dcledger", reason)

    def test_missing_head_repository_is_rejected(self):
        self.assertIsNotNone(run_rejection(a_run(head_repository=None), POLICY))

    def test_another_repository_is_rejected(self):
        self.assertIsNotNone(run_rejection(a_run(repository={"full_name": "other/repo"}), POLICY))

    def test_pull_request_events_are_rejected(self):
        # A pull request can change its own workflow's trigger, so only allow-listed events count.
        for event in ("pull_request", "pull_request_target", "pull_request_review_comment", "issue_comment"):
            with self.subTest(event=event):
                self.assertIn("triggered by", run_rejection(a_run(event=event), POLICY))

    def test_unsuccessful_runs_are_rejected(self):
        for status, conclusion in (("completed", "failure"), ("completed", "cancelled"), ("in_progress", None)):
            with self.subTest(conclusion=conclusion):
                self.assertIsNotNone(run_rejection(a_run(status=status, conclusion=conclusion), POLICY))

    def test_another_branch_is_rejected(self):
        self.assertIsNotNone(run_rejection(a_run(head_branch="feature"), POLICY))

    def test_another_workflow_is_rejected(self):
        self.assertIsNotNone(run_rejection(a_run(path=".github/workflows/ci.yml.evil"), POLICY))
        self.assertIsNotNone(run_rejection(a_run(path=".github/workflows/other.yml"), POLICY))

    def test_workflow_path_with_a_ref_suffix_is_accepted(self):
        self.assertIsNone(run_rejection(a_run(path=".github/workflows/ci.yml@refs/heads/main"), POLICY))

    def test_short_or_malformed_sha_is_rejected(self):
        self.assertIsNotNone(run_rejection(a_run(head_sha="abc1234"), POLICY))


class PickArtifactTests(unittest.TestCase):
    def test_latest_attempt_wins(self):
        artifacts = [an_artifact(attempt=1), an_artifact(attempt=3), an_artifact(attempt=2)]
        self.assertEqual(pick_artifact(artifacts, SHA)["name"], artifact_name(SHA, 3))

    def test_expired_artifacts_are_ignored(self):
        artifacts = [an_artifact(attempt=1), an_artifact(attempt=2, expired=True)]
        self.assertEqual(pick_artifact(artifacts, SHA)["name"], artifact_name(SHA, 1))

    def test_other_commits_and_other_artifacts_are_ignored(self):
        artifacts = [
            an_artifact(sha=OLDER_SHA),
            {"id": 1, "name": f"fixture-exports-{SHA}", "expired": False},
            {"id": 2, "name": artifact_name(SHA, 1) + ".evil", "expired": False},
            {"id": 3, "name": artifact_name(SHA, 1).replace("-a1", "-a0"), "expired": False},
        ]
        self.assertIsNone(pick_artifact(artifacts, SHA))


class StalenessTests(unittest.TestCase):
    EXPIRES = NOW + timedelta(days=80)

    def assess(self, policy=POLICY, **kwargs):
        args = {"ahead_by": 0, "moved_at": None, "expires_at": self.EXPIRES, "now": NOW, "policy": policy}
        args.update(kwargs)
        return staleness(**args)

    def test_build_at_the_branch_head_is_fine(self):
        self.assertEqual(self.assess(), ([], []))

    def test_branch_ahead_for_a_short_time_only_warns(self):
        problems, warnings = self.assess(ahead_by=2, moved_at=NOW - timedelta(days=2))
        self.assertEqual(problems, [])
        self.assertIn("2 commit(s) ahead", warnings[0])

    def test_branch_ahead_for_too_long_fails(self):
        problems, _ = self.assess(ahead_by=1, moved_at=NOW - timedelta(days=7, hours=1))
        self.assertIn("7.0 days", problems[0])

    def test_unknown_move_time_fails(self):
        problems, _ = self.assess(ahead_by=1, moved_at=None)
        self.assertIn("can't tell", problems[0])

    def test_artifact_close_to_expiry_fails(self):
        problems, _ = self.assess(expires_at=NOW + timedelta(days=13))
        self.assertIn("expires in 13.0 days", problems[0])

    def test_missing_expiry_fails(self):
        problems, _ = self.assess(expires_at=None)
        self.assertEqual(len(problems), 1)

    def test_allow_stale_turns_problems_into_warnings(self):
        relaxed = Policy(REPO, "ci.yml", "main", EVENTS, 7, 14, allow_stale=True)
        problems, warnings = self.assess(policy=relaxed, ahead_by=1, moved_at=NOW - timedelta(days=30))
        self.assertEqual(problems, [])
        self.assertTrue(any(warning.startswith("allowed by allow-stale") for warning in warnings))

    def test_move_time_is_when_the_pull_request_merged_not_when_its_commit_was_written(self):
        # The shape GitHub returned for #52: the PR commit and the merge both have the base as first parent.
        pr_commit = a_commit("p" * 40, SHA, "2026-10-20T09:00:00Z")
        merge = a_commit("m" * 40, SHA, "2026-10-28T09:00:00Z", "p" * 40)
        self.assertEqual(moved_since(a_compare("ahead", 2, [pr_commit, merge]), SHA), at(2026, 10, 28, 9))

    def test_move_time_follows_the_first_parent_line_across_later_merges(self):
        first_pr = a_commit("1" * 40, SHA, "2026-10-21T09:00:00Z")
        first_merge = a_commit("2" * 40, SHA, "2026-10-22T09:00:00Z", "1" * 40)
        second_pr = a_commit("3" * 40, SHA, "2026-10-25T09:00:00Z")  # also branched from the build's commit
        second_merge = a_commit("4" * 40, "2" * 40, "2026-10-26T09:00:00Z", "3" * 40)
        compare = a_compare("ahead", 4, [first_pr, first_merge, second_pr, second_merge])
        self.assertEqual(moved_since(compare, SHA), at(2026, 10, 22, 9))

    def test_move_time_of_direct_pushes_is_the_first_commit(self):
        first, second = (
            a_commit("1" * 40, SHA, "2026-10-21T09:00:00Z"),
            a_commit("2" * 40, "1" * 40, "2026-10-27T09:00:00Z"),
        )
        self.assertEqual(moved_since(a_compare("ahead", 2, [second, first]), SHA), at(2026, 10, 21, 9))

    def test_move_time_is_unknown_when_the_compare_is_truncated_or_empty(self):
        truncated = a_compare("ahead", 300, [a_commit("1" * 40, SHA, "2026-10-21T09:00:00Z")])
        truncated["total_commits"] = 300
        self.assertIsNone(moved_since(truncated, SHA))
        self.assertIsNone(moved_since(a_compare("ahead", 300, []), SHA))


class SelectTests(unittest.TestCase):
    def test_newest_eligible_run_wins_and_forks_are_ignored(self):
        fork = a_run(id=300, created_at="2026-10-28T11:00:00Z", head_repository={"full_name": "x/dcledger"})
        older = a_run(id=100, head_sha=OLDER_SHA, created_at="2026-10-27T10:00:00Z")
        api = FakeApi(runs=[older, fork, a_run()], artifacts={200: [an_artifact()], 300: [an_artifact()]})

        selection = select(api, POLICY, NOW)

        self.assertEqual((selection.run_id, selection.head_sha), (200, SHA))
        self.assertEqual(selection.digest, f"sha256:{FILE_DIGEST}")
        self.assertEqual(selection.warnings, ())

    def test_server_side_filters_are_requested(self):
        api = FakeApi(runs=[a_run()], artifacts={200: [an_artifact()]})
        select(api, POLICY, NOW)
        self.assertEqual(
            api.calls[0][1], {"branch": "main", "status": "success", "exclude_pull_requests": "true", "per_page": "100"}
        )

    def test_run_whose_commit_left_the_branch_is_skipped(self):
        rewritten = a_run(id=300, head_sha="d" * 40, created_at="2026-10-28T11:00:00Z")
        api = FakeApi(
            runs=[rewritten, a_run()],
            compares={"d" * 40: a_compare("diverged")},
            artifacts={200: [an_artifact()], 300: [an_artifact(sha="d" * 40)]},
        )
        selection = select(api, POLICY, NOW)
        self.assertEqual(selection.run_id, 200)
        self.assertIn("not on main (diverged)", selection.warnings[0])

    def test_newest_run_without_an_artifact_fails_instead_of_falling_back(self):
        older = a_run(id=100, head_sha=OLDER_SHA, created_at="2026-10-27T10:00:00Z")
        api = FakeApi(runs=[older, a_run()], artifacts={100: [an_artifact(sha=OLDER_SHA)], 200: []})
        with self.assertRaisesRegex(CheckFailed, "refusing to fall back"):
            select(api, POLICY, NOW)

    def test_no_eligible_run_fails_and_says_why(self):
        api = FakeApi(runs=[a_run(event="pull_request")])
        with self.assertRaisesRegex(
            CheckFailed, r"no successful ci\.yml run.*run 200: it was triggered by 'pull_request'"
        ):
            select(api, POLICY, NOW)

    def test_artifact_without_a_digest_fails(self):
        api = FakeApi(runs=[a_run()], artifacts={200: [an_artifact(digest=None)]})
        with self.assertRaisesRegex(CheckFailed, "no SHA-256 digest"):
            select(api, POLICY, NOW)

    def test_stale_build_fails(self):
        child = a_commit("m" * 40, SHA, "2026-10-18T12:00:00Z")
        api = FakeApi(runs=[a_run()], compares={SHA: a_compare("ahead", 1, [child])}, artifacts={200: [an_artifact()]})
        with self.assertRaisesRegex(CheckFailed, "10.0 days"):
            select(api, POLICY, NOW)

    def test_fresh_build_behind_the_branch_warns(self):
        child = a_commit("m" * 40, SHA, "2026-10-28T11:00:00Z")
        api = FakeApi(runs=[a_run()], compares={SHA: a_compare("ahead", 1, [child])}, artifacts={200: [an_artifact()]})
        selection = select(api, POLICY, NOW)
        self.assertEqual(selection.ahead_by, 1)
        self.assertIn("1 commit(s) ahead", selection.warnings[0])

    def test_pinned_pull_request_run_is_rejected(self):
        api = FakeApi(runs=[a_run(id=555, event="pull_request", head_branch="spike/12")])
        with self.assertRaisesRegex(CheckFailed, "run 555 can't be used"):
            select(api, POLICY, NOW, pinned_run_id=555)

    def test_pinned_eligible_run_is_used(self):
        api = FakeApi(runs=[a_run(id=100, head_sha=OLDER_SHA)], artifacts={100: [an_artifact(sha=OLDER_SHA)]})
        self.assertEqual(select(api, POLICY, NOW, pinned_run_id=100).head_sha, OLDER_SHA)


class SettingsTests(unittest.TestCase):
    def test_defaults(self):
        policy = Policy.from_env({"GITHUB_REPOSITORY": REPO, "FETCH_CLI_EVENTS": " "})
        self.assertEqual((policy.workflow, policy.branch, policy.events), ("ci.yml", "main", EVENTS))
        self.assertEqual((policy.max_ahead_days, policy.min_expiry_days, policy.allow_stale), (7, 14, False))
        self.assertEqual((policy.workflow_path, policy.source_ref), (".github/workflows/ci.yml", "refs/heads/main"))

    def test_overrides_for_a_throwaway_branch(self):
        env = {
            "GITHUB_REPOSITORY": REPO,
            "FETCH_CLI_BRANCH": "ci/tmp-12",
            "FETCH_CLI_EVENTS": "push",
            "FETCH_CLI_ALLOW_STALE": "TRUE",
        }
        policy = Policy.from_env(env)
        self.assertEqual(
            (policy.source_ref, policy.events, policy.allow_stale), ("refs/heads/ci/tmp-12", {"push"}, True)
        )

    def test_bad_settings_are_rejected(self):
        for name, value in (
            ("GITHUB_REPOSITORY", "no-slash"),
            ("FETCH_CLI_WORKFLOW", "../ci.yml"),
            ("FETCH_CLI_BRANCH", "main..x"),
            ("FETCH_CLI_BRANCH", "-main"),
            ("FETCH_CLI_EVENTS", "push; rm"),
            ("FETCH_CLI_EVENTS", "push pull_request"),
            ("FETCH_CLI_EVENTS", "pull_request_target"),
            ("FETCH_CLI_MAX_AHEAD_DAYS", "-1"),
            ("FETCH_CLI_MIN_EXPIRY_DAYS", "nan"),
            ("FETCH_CLI_MIN_EXPIRY_DAYS", "inf"),
            ("FETCH_CLI_ALLOW_STALE", "yes"),
        ):
            with self.subTest(name=name, value=value):
                env = {"GITHUB_REPOSITORY": REPO, name: value}
                with self.assertRaises(ConfigError):
                    Policy.from_env(env)

    def test_run_ids(self):
        self.assertIsNone(parse_run_id(" "))
        self.assertEqual(parse_run_id("40689016564"), 40689016564)
        for bad in ("0", "-5", "12a", "1e3"):
            with self.subTest(bad=bad), self.assertRaises(ConfigError):
                parse_run_id(bad)


def write_tarball(path, members):
    """members: (name, kind, payload) with kind 'file', 'dir', 'symlink' or 'hardlink'."""
    with tarfile.open(path, "w:gz") as archive:
        for name, kind, payload in members:
            info = tarfile.TarInfo(name)
            if kind == "dir":
                info.type, info.mode = tarfile.DIRTYPE, 0o755
                archive.addfile(info)
            elif kind in ("symlink", "hardlink"):
                info.type = tarfile.SYMTYPE if kind == "symlink" else tarfile.LNKTYPE
                info.linkname = payload
                archive.addfile(info)
            else:
                data = payload.encode()
                info.size, info.mode = len(data), 0o755 if name.endswith("dcl") else 0o644
                archive.addfile(info, io.BytesIO(data))


def fake_dcl(version):
    return f"#!/bin/sh\necho '{version}'\n"


class PackTests(unittest.TestCase):
    EPOCH = 1_793_000_000

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.source = self.tmp / "publish"
        (self.source / "de").mkdir(parents=True)
        (self.source / "dcl").write_text(fake_dcl(f"0.1.0+{SHA}"))
        (self.source / "dcl").chmod(0o750)
        (self.source / "dcl.dll").write_bytes(b"managed")
        (self.source / "libduckdb.so").write_bytes(b"native")
        (self.source / "libduckdb.so").chmod(0o700)
        (self.source / "de" / "System.CommandLine.resources.dll").write_bytes(b"resources")

    def tearDown(self):
        self._tmp.cleanup()

    def test_tarball_is_named_for_the_commit_and_attempt(self):
        target = pack(self.source, self.tmp / "out", SHA, 2, self.EPOCH)
        self.assertEqual(target.name, f"dcl-linux-x64-{SHA}-a2.tar.gz")

    def test_entries_are_sorted_and_normalized(self):
        target = pack(self.source, self.tmp / "out", SHA, 1, self.EPOCH)
        with tarfile.open(target) as archive:
            members = archive.getmembers()
        self.assertEqual(
            [member.name for member in members],
            ["dcl", "dcl.dll", "de", "de/System.CommandLine.resources.dll", "libduckdb.so"],
        )
        modes = {member.name: member.mode for member in members}
        expected = {"dcl": 0o755, "dcl.dll": 0o644, "de": 0o755, "libduckdb.so": 0o755}
        self.assertEqual(modes, expected | {"de/System.CommandLine.resources.dll": 0o644})
        for member in members:
            self.assertEqual(
                (member.uid, member.gid, member.uname, member.gname, member.mtime), (0, 0, "", "", self.EPOCH)
            )

    def test_same_input_gives_the_same_bytes(self):
        first = pack(self.source, self.tmp / "one", SHA, 1, self.EPOCH).read_bytes()
        os.utime(self.source / "dcl.dll", (0, 0))  # file times on disk don't matter
        second = pack(self.source, self.tmp / "two", SHA, 1, self.EPOCH).read_bytes()
        self.assertEqual(hashlib.sha256(first).hexdigest(), hashlib.sha256(second).hexdigest())
        self.assertEqual(first[4:8], b"\0\0\0\0", "gzip header time must be zero")

    def test_round_trip_through_unpack_keeps_the_executable(self):
        target = pack(self.source, self.tmp / "out", SHA, 1, self.EPOCH)
        cli = unpack(target, self.tmp / "unpacked")
        self.assertTrue(cli.stat().st_mode & stat.S_IXUSR)
        self.assertEqual(run_command([str(cli)], {}).stdout.strip(), f"0.1.0+{SHA}")
        self.assertEqual((self.tmp / "unpacked" / "de" / "System.CommandLine.resources.dll").read_bytes(), b"resources")

    def test_refuses_a_missing_or_non_executable_cli(self):
        (self.source / "dcl").chmod(0o644)
        with self.assertRaisesRegex(CheckFailed, "no executable dcl"):
            pack(self.source, self.tmp / "out", SHA, 1, self.EPOCH)
        (self.source / "dcl").unlink()
        with self.assertRaisesRegex(CheckFailed, "no executable dcl"):
            pack(self.source, self.tmp / "out", SHA, 1, self.EPOCH)

    def test_refuses_links(self):
        (self.source / "link.so").symlink_to(self.source / "libduckdb.so")
        with self.assertRaisesRegex(CheckFailed, "link.so is a link"):
            pack(self.source, self.tmp / "out", SHA, 1, self.EPOCH)

    def test_refuses_bad_arguments(self):
        for sha, attempt, epoch in (("abc", 1, 0), (SHA, 0, 0), (SHA, 1, -1)):
            with self.subTest(sha=sha, attempt=attempt, epoch=epoch), self.assertRaises(ConfigError):
                pack(self.source, self.tmp / "out", sha, attempt, epoch)


class VerifyFileTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def test_single_download_needs_exactly_one_regular_file(self):
        download = self.tmp / "download"
        download.mkdir()
        with self.assertRaisesRegex(CheckFailed, "found nothing"):
            single_download(download)
        (download / "artifact").write_bytes(b"x")
        self.assertEqual(single_download(download).name, "artifact")
        (download / "second").write_bytes(b"y")
        with self.assertRaisesRegex(CheckFailed, "artifact, second"):
            single_download(download)

    def test_single_download_refuses_a_symlink(self):
        download = self.tmp / "download"
        download.mkdir()
        (download / "link").symlink_to("/etc/passwd")
        with self.assertRaises(CheckFailed):
            single_download(download)

    def test_digest_comparison(self):
        check_digest(FILE_DIGEST, f"sha256:{FILE_DIGEST}")
        check_digest(FILE_DIGEST.upper(), FILE_DIGEST)
        with self.assertRaisesRegex(CheckFailed, "doesn't match"):
            check_digest("d" * 64, f"sha256:{FILE_DIGEST}")

    def test_unpack_keeps_the_executable_bit(self):
        tarball = self.tmp / "dcl.tar.gz"
        write_tarball(tarball, [(".", "dir", ""), ("./dcl", "file", fake_dcl("1")), ("./lib/x.dll", "file", "x")])
        cli = unpack(tarball, self.tmp / "out")
        self.assertEqual(cli, self.tmp / "out" / "dcl")
        self.assertTrue(cli.stat().st_mode & stat.S_IXUSR)
        self.assertTrue((self.tmp / "out" / "lib" / "x.dll").is_file())

    def test_unpack_refuses_dangerous_entries(self):
        for members, message in (
            ([("dcl", "file", "x"), ("evil", "symlink", "/etc/passwd")], "link or special file"),
            ([("dcl", "file", "x"), ("evil", "hardlink", "dcl")], "link or special file"),
            ([("dcl", "file", "x"), ("../evil", "file", "x")], "not a plain relative path"),
            ([("dcl", "file", "x"), ("a/../../evil", "file", "x")], "not a plain relative path"),
            ([("/tmp/evil", "file", "x")], "not a plain relative path"),
            ([("bin/dcl", "file", "x")], "no dcl executable at its root"),
        ):
            with self.subTest(message=message, members=[m[0] for m in members]):
                tarball = self.tmp / "bad.tar.gz"
                write_tarball(tarball, members)
                dest = Path(tempfile.mkdtemp(dir=self.tmp))
                with self.assertRaisesRegex(CheckFailed, message):
                    unpack(tarball, dest)
                self.assertFalse((self.tmp / "evil").exists())

    def test_unpack_refuses_a_corrupt_file_and_a_used_destination(self):
        corrupt = self.tmp / "corrupt.tar.gz"
        corrupt.write_bytes(b"not a tarball")
        with self.assertRaisesRegex(CheckFailed, "can't unpack"):
            unpack(corrupt, self.tmp / "out1")
        good = self.tmp / "good.tar.gz"
        write_tarball(good, [("dcl", "file", "x")])
        used = self.tmp / "used"
        used.mkdir()
        (used / "leftover").write_text("x")
        with self.assertRaisesRegex(CheckFailed, "not empty"):
            unpack(good, used)

    def test_version_must_name_the_commit(self):
        self.assertEqual(check_version(f"0.1.0+{SHA}\n", SHA), f"0.1.0+{SHA}")
        for output in (f"0.1.0+{OLDER_SHA}", "0.1.0", ""):
            with self.subTest(output=output), self.assertRaises(CheckFailed):
                check_version(output, SHA)

    def test_attestation_command_pins_workflow_ref_and_commit(self):
        command = attestation_command(Path("f.tar.gz"), POLICY, SHA)
        self.assertEqual(command[:4], ["gh", "attestation", "verify", "f.tar.gz"])
        pairs = dict(zip(command[4:12:2], command[5:12:2], strict=True))
        self.assertEqual(pairs["--repo"], REPO)
        self.assertEqual(pairs["--signer-workflow"], f"{REPO}/.github/workflows/ci.yml")
        self.assertEqual(pairs["--source-ref"], "refs/heads/main")
        self.assertEqual(pairs["--source-digest"], SHA)
        self.assertIn("--deny-self-hosted-runners", command)

    def test_attested_run_ids_come_from_the_provenance_invocation(self):
        verified = [
            attestation_result(f"https://github.com/{REPO}/actions/runs/123/attempts/2"),
            attestation_result(f"https://github.com/{REPO}/actions/runs/456"),
            {"verificationResult": {"statement": {"predicate": {}}}},
            None,
        ]
        self.assertEqual(attested_run_ids(verified), {123, 456})
        self.assertEqual(attested_run_ids({"not": "a list"}), set())

    def test_secrets_are_removed_from_the_binary_environment(self):
        env = {name: "x" for name in ("GH_TOKEN", "GITHUB_TOKEN", "ACTIONS_ID_TOKEN_REQUEST_TOKEN", "PATH")}
        self.assertEqual(without_secrets(env), {"PATH": "x"})


def attestation_result(invocation_id):
    return {
        "verificationResult": {
            "statement": {"predicate": {"runDetails": {"metadata": {"invocationId": invocation_id}}}}
        }
    }


class MainTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.outputs = self.tmp / "outputs"
        self.summary = self.tmp / "summary"
        self.env = {
            "GITHUB_REPOSITORY": REPO,
            "GITHUB_OUTPUT": str(self.outputs),
            "GITHUB_STEP_SUMMARY": str(self.summary),
            "RUNNER_TEMP": str(self.tmp),
            "GH_TOKEN": "token-value",
            "PATH": os.environ.get("PATH", ""),
        }
        self.stdout = io.StringIO()

    def tearDown(self):
        self._tmp.cleanup()

    def read_outputs(self):
        return dict(line.split("=", 1) for line in self.outputs.read_text().splitlines())

    def test_pack_writes_outputs_and_a_summary(self):
        source = self.tmp / "publish"
        source.mkdir()
        (source / "dcl").write_text(fake_dcl("1"))
        (source / "dcl").chmod(0o755)
        argv = ["pack", "--source", str(source), "--out-dir", str(self.tmp / "out"), "--head-sha", SHA]
        self.assertEqual(main(argv + ["--attempt", "1", "--epoch", "0"], self.env, stdout=self.stdout), 0)
        outputs = self.read_outputs()
        self.assertEqual(outputs["file-name"], artifact_name(SHA, 1))
        self.assertEqual(outputs["sha256"], hashlib.sha256(Path(outputs["path"]).read_bytes()).hexdigest())
        self.assertIn(outputs["sha256"], self.summary.read_text())

    def test_pack_without_a_cli_exits_1(self):
        argv = ["pack", "--source", str(self.tmp), "--out-dir", str(self.tmp / "out"), "--head-sha", SHA]
        self.assertEqual(main(argv + ["--attempt", "1", "--epoch", "0"], self.env, stdout=self.stdout), 1)
        self.assertTrue(self.stdout.getvalue().startswith("::error title=cli-artifact::"))

    def test_select_writes_outputs_and_a_summary(self):
        api = FakeApi(runs=[a_run()], artifacts={200: [an_artifact()]})
        self.assertEqual(main(["select"], self.env, stdout=self.stdout, api=api, now=NOW), 0)
        outputs = self.read_outputs()
        self.assertEqual(outputs["run-id"], "200")
        self.assertEqual(outputs["head-sha"], SHA)
        self.assertEqual(outputs["artifact-id"], "9001")
        self.assertEqual(outputs["digest"], f"sha256:{FILE_DIGEST}")
        self.assertTrue(Path(outputs["download-dir"]).is_dir())
        self.assertIn(f"/{REPO}/actions/runs/200", self.summary.read_text())
        self.assertNotIn("token-value", self.stdout.getvalue() + self.summary.read_text())

    def test_select_failure_is_an_error_annotation_and_exit_1(self):
        self.assertEqual(main(["select"], self.env, stdout=self.stdout, api=FakeApi(), now=NOW), 1)
        self.assertTrue(self.stdout.getvalue().startswith("::error title=cli-artifact::no successful"))
        self.assertFalse(self.outputs.exists())

    def test_bad_configuration_exits_2(self):
        env = dict(self.env, FETCH_CLI_RUN_ID="abc")
        self.assertEqual(main(["select"], env, stdout=self.stdout, api=FakeApi(), now=NOW), 2)

    def verify(self, *, version=f"0.1.0+{SHA}", attested_run=200, gh_exit=0, digest=None, run_id="200", dest=True):
        download = self.tmp / "download"
        download.mkdir()
        tarball = download / "artifact"
        write_tarball(tarball, [(".", "dir", ""), ("./dcl", "file", fake_dcl(version))])
        actual = hashlib.sha256(tarball.read_bytes()).hexdigest()
        calls = []

        def runner(command, env):
            calls.append((command, dict(env)))
            if command[0] == "gh":
                body = json.dumps(
                    [attestation_result(f"https://github.com/{REPO}/actions/runs/{attested_run}/attempts/1")]
                )
                return subprocess.CompletedProcess(command, gh_exit, body if gh_exit == 0 else "", "no attestations")
            return run_command(command, env)

        argv = ["verify", "--run-id", run_id, "--head-sha", SHA, "--digest", digest or f"sha256:{actual}"]
        argv += ["--download-dir", str(download)] + (["--dest", str(self.tmp / "dcl")] if dest else [])
        return main(argv, self.env, stdout=self.stdout, runner=runner), calls

    def test_verify_unpacks_and_reports_the_cli(self):
        code, calls = self.verify()
        self.assertEqual(code, 0, self.stdout.getvalue())
        outputs = self.read_outputs()
        self.assertEqual(outputs["cli-path"], str(self.tmp / "dcl" / "dcl"))
        self.assertEqual(outputs["version"], f"0.1.0+{SHA}")
        self.assertEqual(calls[0][0][:3], ["gh", "attestation", "verify"])
        self.assertEqual(calls[0][1]["GH_TOKEN"], "token-value")
        self.assertNotIn("GH_TOKEN", calls[1][1], "the downloaded binary must not see the token")

    def test_verify_unpacks_into_a_new_directory_by_default(self):
        code, _ = self.verify(dest=False)
        self.assertEqual(code, 0, self.stdout.getvalue())
        cli = Path(self.read_outputs()["cli-path"])
        self.assertEqual((cli.name, cli.parent.parent), ("dcl", self.tmp))

    def test_verify_fails_on_a_digest_mismatch_before_running_anything(self):
        code, calls = self.verify(digest=f"sha256:{'0' * 64}")
        self.assertEqual(code, 1)
        self.assertIn("doesn't match", self.stdout.getvalue())
        self.assertEqual(calls, [])

    def test_verify_fails_when_the_attestation_does_not_verify(self):
        code, calls = self.verify(gh_exit=1)
        self.assertEqual(code, 1)
        self.assertIn("attestation verification failed: no attestations", self.stdout.getvalue())
        self.assertEqual(len(calls), 1, "dcl must not run")

    def test_verify_fails_when_the_attestation_names_another_run(self):
        code, _ = self.verify(attested_run=999)
        self.assertEqual(code, 1)
        self.assertIn("not the selected run 200", self.stdout.getvalue())

    def test_verify_fails_when_the_binary_names_another_commit(self):
        code, _ = self.verify(version=f"0.1.0+{OLDER_SHA}")
        self.assertEqual(code, 1)
        self.assertIn("doesn't name commit", self.stdout.getvalue())

    def test_verify_rejects_incomplete_arguments(self):
        code, _ = self.verify(run_id="")
        self.assertEqual(code, 2)


class FakeResponse(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


class GitHubApiTests(unittest.TestCase):
    def http_error(self, code, retry_after=None):
        headers = Message()
        if retry_after is not None:
            headers["Retry-After"] = retry_after
        return urllib.error.HTTPError("https://api.github.com/x", code, "Error", headers, None)

    def test_server_errors_are_retried_and_auth_is_sent(self):
        requests, sleeps = [], []
        replies = [self.http_error(503), self.http_error(429, "3"), FakeResponse(b'{"ok": true}')]

        def opener(request, timeout):
            requests.append(request)
            reply = replies.pop(0)
            if isinstance(reply, Exception):
                raise reply
            return reply

        api = GitHubApi("secret", opener=opener, sleep=sleeps.append)
        self.assertEqual(api.get("repos/o/r", {"a": "b c"}), {"ok": True})
        self.assertEqual(requests[0].full_url, "https://api.github.com/repos/o/r?a=b+c")
        self.assertEqual(requests[0].get_header("Authorization"), "Bearer secret")
        self.assertEqual(sleeps, [2.0, 3.0])

    def test_client_errors_fail_without_retrying_or_leaking_the_token(self):
        calls = []

        def opener(request, timeout):
            calls.append(request)
            raise self.http_error(404)

        with self.assertRaises(CheckFailed) as caught:
            GitHubApi("secret", opener=opener, sleep=lambda _: None).get("repos/o/r")
        self.assertEqual(len(calls), 1)
        self.assertNotIn("secret", str(caught.exception))

    def test_pagination_stops_at_a_short_page(self):
        pages = [{"artifacts": [{"id": n} for n in range(100)]}, {"artifacts": [{"id": 100}]}]
        api = GitHubApi("t", opener=lambda request, timeout: FakeResponse(json.dumps(pages.pop(0)).encode()))
        self.assertEqual(len(api.get_all("repos/o/r/actions/runs/1/artifacts", "artifacts")), 101)

    def test_missing_token_is_a_configuration_error(self):
        with self.assertRaises(ConfigError):
            GitHubApi("")


class WorkflowTests(unittest.TestCase):
    def test_outputs_must_be_single_lines(self):
        with self.assertRaises(ValueError):
            Workflow({}).output("x", "a\nb")

    def test_annotation_data_is_escaped(self):
        self.assertEqual(escape_data("50%\r\nnext"), "50%25%0D%0Anext")
        stdout = io.StringIO()
        Workflow({}, stdout).warning("line1\n::error::injected")
        self.assertEqual(stdout.getvalue(), "::warning title=cli-artifact::line1%0A::error::injected\n")


if __name__ == "__main__":
    unittest.main()

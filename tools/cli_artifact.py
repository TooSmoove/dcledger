"""Packs the dcl CLI in ci, and selects, verifies and unpacks it in scheduled workflows (ADR-0007, spike S17).

The CLI artifact is one gzipped tarball, `dcl-linux-x64-<commit>-a<attempt>.tar.gz`, uploaded unzipped by ci and
signed with an artifact attestation when ci passes on main. This module owns that contract on both sides:

  pack    (ci) Packs the `dotnet publish` output deterministically: sorted entries, owner 0:0, the commit time
          on every entry, only the executable bit kept. Writes file-name, path and sha256 to $GITHUB_OUTPUT.
  select  (consumers) Finds the newest successful ci run that belongs to this repository, was built from it (not
          a fork), ran on main, was triggered by an allowed event and whose commit is on main. Picks that run's
          CLI artifact and applies the staleness policy. Never falls back to an older build when the newest
          eligible run has no artifact. Writes run-id, head-sha, artifact-id, digest and download-dir.
  verify  (consumers) Checks the downloaded tarball against the API digest, verifies its attestation (ci.yml on
          refs/heads/main, that commit, that run), unpacks it safely and checks that `dcl --version` names the
          commit. Writes cli-dir, cli-path and version.

Every property of a run is checked here, on the client: the API's `branch` filter also matches a fork's branch
named main, and a pull request can change the event its own workflow runs on.

Usage (consumers normally go through .github/actions/fetch-cli):
  python3 -I tools/cli_artifact.py pack --source DIR --out-dir DIR --head-sha SHA --attempt N --epoch SECONDS
  python3 -I tools/cli_artifact.py select
  python3 -I tools/cli_artifact.py verify --run-id ID --head-sha SHA --digest sha256:HEX --download-dir DIR
Consumer settings come from the environment (see Policy.from_env). Exit code 0 on success, 1 when a check fails,
2 on bad configuration. Standard library only; Python 3.12 or later (tarfile extraction filters).
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path, PurePosixPath
from typing import Any, Protocol, TextIO

ARTIFACT_PREFIX = "dcl-linux-x64-"
EXECUTABLE = "dcl"
SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")
DIGEST_PATTERN = re.compile(r"^(sha256:)?[0-9a-f]{64}$")
REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
WORKFLOW_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+\.ya?ml$")
BRANCH_PATTERN = re.compile(r"^[A-Za-z0-9_./-]+$")
EVENT_PATTERN = re.compile(r"^[a-z_]+$")
INVOCATION_PATTERN = re.compile(r"/actions/runs/(?P<run>[0-9]+)(?:/attempts/[0-9]+)?$")

DEFAULT_EVENTS = "push schedule workflow_dispatch"
MAX_RUNS_TO_INSPECT = 20  # runs whose commit left the branch are skipped; stop looking after this many
MAX_TAR_MEMBERS = 5_000
MAX_UNPACKED_BYTES = 768 * 1024 * 1024
COMMAND_TIMEOUT_SECONDS = 120
SECRET_VARIABLES = frozenset(
    {
        "GH_TOKEN",
        "GITHUB_TOKEN",
        "ACTIONS_RUNTIME_TOKEN",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN",
        "ACTIONS_ID_TOKEN_REQUEST_URL",
    }
)


class ConfigError(Exception):
    """The settings or arguments are missing or malformed (exit code 2)."""


class CheckFailed(Exception):
    """A packing, selection or verification check failed (exit code 1)."""


def artifact_name(head_sha: str, attempt: int) -> str:
    """The CLI artifact's file name, which is also its artifact name (upload-artifact `archive: false`)."""
    return f"{ARTIFACT_PREFIX}{head_sha}-a{attempt}.tar.gz"


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


# --- Pack (ci) -----------------------------------------------------------------------------------------------


def pack(source: Path, out_dir: Path, head_sha: str, attempt: int, epoch: int) -> Path:
    """Packs a published CLI directory into the tarball ci uploads and returns its path.

    The same directory contents always give the same bytes: entries are sorted, owned by 0:0 with no user or group
    names, stamped with `epoch` (the commit time) and keep only the executable bit; the gzip header has no file name
    or time. Links and special files are refused, because unpack refuses them too.
    """
    if not SHA_PATTERN.match(head_sha):
        raise ConfigError(f"--head-sha must be a full commit SHA, got {head_sha!r}")
    if attempt < 1 or epoch < 0:
        raise ConfigError("--attempt must be 1 or more and --epoch zero or more")
    cli = source / EXECUTABLE
    if not cli.is_file() or cli.is_symlink() or not os.access(cli, os.X_OK):
        raise CheckFailed(f"{source} has no executable {EXECUTABLE} at its root; run dotnet publish first")

    entries = sorted(source.rglob("*"), key=lambda path: path.relative_to(source).as_posix())
    out_dir.mkdir(parents=True, exist_ok=True)
    target = out_dir / artifact_name(head_sha, attempt)
    with (
        target.open("wb") as raw,
        gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as compressed,
        tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive,
    ):
        for path in entries:
            name = path.relative_to(source).as_posix()
            if path.is_symlink() or not (path.is_file() or path.is_dir()):
                raise CheckFailed(f"{name} is a link or special file; the CLI tarball holds only files and directories")
            info = tarfile.TarInfo(name)
            info.mtime, info.uid, info.gid, info.uname, info.gname = epoch, 0, 0, "", ""
            if path.is_dir():
                info.type, info.mode = tarfile.DIRTYPE, 0o755
                archive.addfile(info)
                continue
            status = path.stat()
            info.size, info.mode = status.st_size, 0o755 if status.st_mode & 0o111 else 0o644
            with path.open("rb") as stream:
                archive.addfile(info, stream)
    return target


# --- Consumer settings ---------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Policy:
    """Which ci runs are eligible and how stale a build may be."""

    repository: str  # owner/name, from GITHUB_REPOSITORY
    workflow: str  # file name under .github/workflows
    branch: str
    events: frozenset[str]
    max_ahead_days: float
    min_expiry_days: float
    allow_stale: bool = False

    @property
    def workflow_path(self) -> str:
        return f".github/workflows/{self.workflow}"

    @property
    def source_ref(self) -> str:
        return f"refs/heads/{self.branch}"

    @staticmethod
    def from_env(env: Mapping[str, str]) -> Policy:
        """Reads the policy. FETCH_CLI_WORKFLOW, FETCH_CLI_BRANCH and FETCH_CLI_EVENTS exist for tests on a
        throwaway branch; production consumers keep the defaults (ci.yml, main, push/schedule/workflow_dispatch).
        """
        repository = env.get("GITHUB_REPOSITORY", "")
        workflow = env.get("FETCH_CLI_WORKFLOW", "").strip() or "ci.yml"
        branch = env.get("FETCH_CLI_BRANCH", "").strip() or "main"
        events = frozenset((env.get("FETCH_CLI_EVENTS", "").strip() or DEFAULT_EVENTS).split())
        if not REPOSITORY_PATTERN.match(repository):
            raise ConfigError(f"GITHUB_REPOSITORY must be owner/name, got {repository!r}")
        if not WORKFLOW_PATTERN.match(workflow):
            raise ConfigError(f"the workflow must be a file name such as ci.yml, got {workflow!r}")
        if not BRANCH_PATTERN.match(branch) or ".." in branch or branch.startswith(("/", "-")):
            raise ConfigError(f"the branch is not a plain branch name: {branch!r}")
        if not all(EVENT_PATTERN.match(event) for event in events):
            raise ConfigError(f"events must be space-separated event names, got {sorted(events)!r}")
        if events & {"pull_request", "pull_request_target", "pull_request_review", "pull_request_review_comment"}:
            raise ConfigError("pull request events can never be trusted to have produced the CLI")
        return Policy(
            repository=repository,
            workflow=workflow,
            branch=branch,
            events=events,
            max_ahead_days=_non_negative(env, "FETCH_CLI_MAX_AHEAD_DAYS", 7),
            min_expiry_days=_non_negative(env, "FETCH_CLI_MIN_EXPIRY_DAYS", 14),
            allow_stale=_boolean(env, "FETCH_CLI_ALLOW_STALE"),
        )


def _non_negative(env: Mapping[str, str], name: str, default: float) -> float:
    raw = env.get(name, "").strip()
    if not raw:
        return default
    try:
        value = float(raw)
    except ValueError as error:
        raise ConfigError(f"{name} must be a number, got {raw!r}") from error
    if value < 0 or not math.isfinite(value):
        raise ConfigError(f"{name} must be a finite number, zero or more, got {raw!r}")
    return value


def _boolean(env: Mapping[str, str], name: str) -> bool:
    raw = env.get(name, "").strip().lower()
    if raw in ("", "false"):
        return False
    if raw == "true":
        return True
    raise ConfigError(f"{name} must be true or false, got {raw!r}")


def parse_run_id(raw: str) -> int | None:
    """Parses an optional run ID ('' means none)."""
    raw = raw.strip()
    if not raw:
        return None
    if not raw.isdigit() or int(raw) == 0:
        raise ConfigError(f"a run ID must be a positive integer, got {raw!r}")
    return int(raw)


# --- Selection (pure) ----------------------------------------------------------------------------------------


def run_rejection(run: Mapping[str, Any], policy: Policy) -> str | None:
    """Returns why a workflow run must not be used, or None when it is eligible."""
    if run.get("status") != "completed" or run.get("conclusion") != "success":
        return f"it is not a completed, successful run (status {run.get('status')}, conclusion {run.get('conclusion')})"
    if not _same_repository((run.get("repository") or {}).get("full_name"), policy.repository):
        return "it belongs to another repository"
    head_repository = (run.get("head_repository") or {}).get("full_name")
    if not _same_repository(head_repository, policy.repository):
        return f"it was built from {head_repository or 'an unknown repository'}, not {policy.repository}"
    if run.get("head_branch") != policy.branch:
        return f"its head branch is {run.get('head_branch')!r}, not {policy.branch!r}"
    if run.get("event") not in policy.events:
        return f"it was triggered by {run.get('event')!r}, not one of {', '.join(sorted(policy.events))}"
    path = run.get("path") or ""
    if path != policy.workflow_path and not path.startswith(policy.workflow_path + "@"):
        return f"it ran {path or 'an unknown workflow'}, not {policy.workflow_path}"
    if not SHA_PATTERN.match(run.get("head_sha") or ""):
        return "its head_sha is not a full commit SHA"
    return None


def _same_repository(actual: str | None, expected: str) -> bool:
    # GitHub owner and repository names are case-insensitive.
    return actual is not None and actual.casefold() == expected.casefold()


def newest_first(runs: list[Mapping[str, Any]]) -> list[Mapping[str, Any]]:
    return sorted(runs, key=lambda run: (run.get("created_at") or "", run.get("id") or 0), reverse=True)


def pick_artifact(artifacts: list[Mapping[str, Any]], head_sha: str) -> Mapping[str, Any] | None:
    """The unexpired CLI artifact for the commit from the latest job attempt, or None."""
    pattern = re.compile(rf"^{re.escape(ARTIFACT_PREFIX)}{head_sha}-a(?P<attempt>[1-9][0-9]*)\.tar\.gz$")
    matches = [
        (int(match["attempt"]), artifact.get("id") or 0, artifact)
        for artifact in artifacts
        if not artifact.get("expired") and (match := pattern.match(artifact.get("name") or ""))
    ]
    return max(matches, key=lambda match: match[:2])[2] if matches else None


def moved_since(compare: Mapping[str, Any], base_sha: str) -> datetime | None:
    """When the branch moved past base_sha, from a compare of base_sha...branch; None when that can't be told.

    That is the commit date of the first commit after base_sha on the branch's first-parent line: the merge,
    squash or pushed commit. A merged pull request's own commits can also have base_sha as their first parent,
    but their dates say when the pull request was written or rebased, not when it landed, so the line is walked
    back from the branch head. A truncated compare (over 250 commits) gives None.
    """
    listed = compare.get("commits") or []
    commits = {commit["sha"]: commit for commit in listed if commit.get("sha")}
    if len(commits) != len(listed) or int(compare.get("total_commits") or len(commits)) != len(commits):
        return None
    parents = {sha: [parent.get("sha") for parent in commit.get("parents") or []] for sha, commit in commits.items()}
    tips = set(commits) - {parent for shas in parents.values() for parent in shas}
    if len(tips) != 1:
        return None
    sha = tips.pop()
    for _ in range(len(commits)):  # bounded walk
        if sha not in commits:
            return None
        first_parent = parents[sha][0] if parents[sha] else None
        if first_parent == base_sha:
            return parse_time(((commits[sha].get("commit") or {}).get("committer") or {}).get("date"))
        sha = first_parent
    return None


def staleness(
    *,
    ahead_by: int,
    moved_at: datetime | None,
    expires_at: datetime | None,
    now: datetime,
    policy: Policy,
) -> tuple[list[str], list[str]]:
    """Returns (problems, warnings) for a build the branch may have moved past."""
    problems: list[str] = []
    warnings: list[str] = []
    if ahead_by > 0:
        warnings.append(
            f"{policy.branch} is {ahead_by} commit(s) ahead of this build; its {policy.workflow} run may be in "
            "progress or failing"
        )
        if moved_at is None:
            problems.append(f"can't tell when {policy.branch} moved past this build")
        elif now - moved_at > timedelta(days=policy.max_ahead_days):
            days = (now - moved_at) / timedelta(days=1)
            problems.append(
                f"{policy.branch} has been ahead of the newest green build for {days:.1f} days (limit "
                f"{policy.max_ahead_days:g}); fix {policy.workflow} on {policy.branch}"
            )
    if expires_at is None:
        problems.append("the CLI artifact has no expiry time")
    elif expires_at - now < timedelta(days=policy.min_expiry_days):
        days = (expires_at - now) / timedelta(days=1)
        problems.append(
            f"the CLI artifact expires in {days:.1f} days (limit {policy.min_expiry_days:g}); run "
            f"{policy.workflow} on {policy.branch} to build a fresh one"
        )
    if policy.allow_stale and problems:
        warnings.extend(f"allowed by allow-stale: {problem}" for problem in problems)
        problems = []
    return problems, warnings


def parse_time(value: str | None) -> datetime | None:
    if not value:
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=UTC)


# --- Selection (with the API) --------------------------------------------------------------------------------


class Api(Protocol):
    def get(self, path: str, params: Mapping[str, str] | None = None) -> Any: ...

    def get_all(self, path: str, key: str) -> list[Any]: ...


@dataclass(frozen=True)
class Selection:
    run: Mapping[str, Any]
    artifact: Mapping[str, Any]
    ahead_by: int
    warnings: tuple[str, ...]

    @property
    def run_id(self) -> int:
        return int(self.run["id"])

    @property
    def head_sha(self) -> str:
        return str(self.run["head_sha"])

    @property
    def digest(self) -> str:
        return str(self.artifact.get("digest") or "")


def select(api: Api, policy: Policy, now: datetime, pinned_run_id: int | None = None) -> Selection:
    """Chooses the run and artifact to use, or raises CheckFailed saying why none can be used."""
    repo = policy.repository
    if pinned_run_id is not None:
        run = api.get(f"repos/{repo}/actions/runs/{pinned_run_id}")
        if reason := run_rejection(run, policy):
            raise CheckFailed(f"run {pinned_run_id} can't be used: {reason}")
        candidates = [run]
    else:
        # The query filters are a convenience only: `branch` also matches a fork's branch of the same name.
        listing = api.get(
            f"repos/{repo}/actions/workflows/{policy.workflow}/runs",
            {"branch": policy.branch, "status": "success", "exclude_pull_requests": "true", "per_page": "100"},
        )
        runs = listing.get("workflow_runs") or []
        candidates = newest_first([run for run in runs if not run_rejection(run, policy)])
        if not candidates:
            rejected = [f"run {run.get('id')}: {run_rejection(run, policy)}" for run in newest_first(runs)[:3]]
            raise CheckFailed(
                f"no successful {policy.workflow} run on {policy.branch} from {repo} (triggered by "
                f"{', '.join(sorted(policy.events))}) is available; none has run yet, or all have expired"
                + (f". Rejected: {'; '.join(rejected)}" if rejected else "")
            )

    warnings: list[str] = []
    branch = urllib.parse.quote(policy.branch, safe="")
    for run in candidates[:MAX_RUNS_TO_INSPECT]:
        run_id, head_sha = int(run["id"]), str(run["head_sha"])
        compare = api.get(f"repos/{repo}/compare/{head_sha}...{branch}")
        if compare.get("status") not in ("identical", "ahead"):
            warnings.append(
                f"skipped run {run_id}: commit {head_sha[:7]} is not on {policy.branch} ({compare.get('status')})"
            )
            continue
        artifact = pick_artifact(api.get_all(f"repos/{repo}/actions/runs/{run_id}/artifacts", "artifacts"), head_sha)
        if artifact is None:
            raise CheckFailed(
                f"run {run_id} ({head_sha[:7]}) succeeded but has no unexpired {artifact_name(head_sha, 1)} "
                "(or a later attempt); refusing to fall back to an older build"
            )
        if not DIGEST_PATTERN.match(str(artifact.get("digest") or "")):
            raise CheckFailed(f"artifact {artifact.get('id')} has no SHA-256 digest in the API")
        ahead_by = int(compare.get("ahead_by") or 0)
        problems, stale_warnings = staleness(
            ahead_by=ahead_by,
            moved_at=moved_since(compare, head_sha) if ahead_by else None,
            expires_at=parse_time(artifact.get("expires_at")),
            now=now,
            policy=policy,
        )
        if problems:
            raise CheckFailed("; ".join(problems))
        return Selection(run, artifact, ahead_by, tuple(warnings + stale_warnings))
    raise CheckFailed(f"none of the newest successful runs has a commit on {policy.branch}")


# --- Verification (pure or local files only) -----------------------------------------------------------------


def single_download(download_dir: Path) -> Path:
    """The one regular file download-artifact wrote; its name comes from the server, so it isn't trusted."""
    entries = list(download_dir.iterdir())
    if len(entries) != 1 or not entries[0].is_file() or entries[0].is_symlink():
        names = ", ".join(sorted(entry.name for entry in entries)) or "nothing"
        raise CheckFailed(f"expected exactly one downloaded file in {download_dir}, found {names}")
    return entries[0]


def check_digest(actual_hex: str, expected: str) -> None:
    if actual_hex.lower() != expected.lower().removeprefix("sha256:"):
        raise CheckFailed(f"the downloaded file's SHA-256 {actual_hex} doesn't match the artifact digest {expected}")


def attestation_command(path: Path, policy: Policy, head_sha: str) -> list[str]:
    """gh attestation verify, pinned to the signing workflow, the ref and the commit."""
    return [
        "gh", "attestation", "verify", str(path),
        "--repo", policy.repository,
        "--signer-workflow", f"{policy.repository}/{policy.workflow_path}",
        "--source-ref", policy.source_ref,
        "--source-digest", head_sha,
        "--deny-self-hosted-runners",
        "--format", "json",
    ]  # fmt: skip


def attested_run_ids(verified: Any) -> set[int]:
    """The run IDs named by the verified SLSA provenance statements (runDetails.metadata.invocationId)."""
    run_ids: set[int] = set()
    for result in verified if isinstance(verified, list) else []:
        statement = ((result or {}).get("verificationResult") or {}).get("statement") or {}
        metadata = ((statement.get("predicate") or {}).get("runDetails") or {}).get("metadata") or {}
        if match := INVOCATION_PATTERN.search(str(metadata.get("invocationId") or "")):
            run_ids.add(int(match["run"]))
    return run_ids


def unpack(tarball: Path, dest: Path) -> Path:
    """Unpacks the CLI tarball into an empty directory and returns the dcl executable.

    Only regular files and directories with relative paths are accepted; links, devices and anything that would
    land outside dest are refused before extraction, and the 'data' filter enforces the same again.
    """
    if dest.exists() and any(dest.iterdir()):
        raise CheckFailed(f"{dest} is not empty")
    dest.mkdir(parents=True, exist_ok=True)
    try:
        with tarfile.open(tarball, mode="r:gz") as archive:
            members = archive.getmembers()
            if len(members) > MAX_TAR_MEMBERS:
                raise CheckFailed(f"the tarball has {len(members)} entries (limit {MAX_TAR_MEMBERS})")
            for member in members:
                if not (member.isfile() or member.isdir()):
                    raise CheckFailed(f"tarball entry {member.name!r} is a link or special file")
                if member.name.startswith("/") or ".." in PurePosixPath(member.name).parts:
                    raise CheckFailed(f"tarball entry {member.name!r} is not a plain relative path")
            unpacked = sum(member.size for member in members)
            if unpacked > MAX_UNPACKED_BYTES:
                raise CheckFailed(f"the tarball unpacks to {unpacked} bytes (limit {MAX_UNPACKED_BYTES})")
            archive.extractall(dest, filter="data")
    except (tarfile.TarError, OSError, EOFError) as error:
        raise CheckFailed(f"can't unpack {tarball.name}: {error}") from error
    cli = dest / EXECUTABLE
    if not cli.is_file() or cli.is_symlink():
        raise CheckFailed(f"the tarball has no {EXECUTABLE} executable at its root")
    cli.chmod(0o755)  # the tarball carries +x; this only guards against an unusual umask
    return cli


def check_version(output: str, head_sha: str) -> str:
    """`dcl --version` prints <version>+<commit> (SourceLink); the commit must be the one that was selected."""
    lines = output.strip().splitlines()
    version = lines[0].strip() if lines else ""
    if not version.endswith(f"+{head_sha}"):
        raise CheckFailed(f"dcl --version printed {version!r}, which doesn't name commit {head_sha}")
    return version


# --- Process and workflow I/O --------------------------------------------------------------------------------


Runner = Callable[[list[str], Mapping[str, str]], "subprocess.CompletedProcess[str]"]


def without_secrets(env: Mapping[str, str]) -> dict[str, str]:
    """The environment minus tokens, for running the downloaded binary."""
    return {name: value for name, value in env.items() if name not in SECRET_VARIABLES}


def run_command(command: list[str], env: Mapping[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command, env=dict(env), capture_output=True, text=True, timeout=COMMAND_TIMEOUT_SECONDS, check=False
    )


class GitHubApi:
    """Read-only GitHub REST client with retries for rate limits and server errors."""

    def __init__(
        self,
        token: str,
        base_url: str = "https://api.github.com",
        opener: Callable[..., Any] = urllib.request.urlopen,
        sleep: Callable[[float], None] = time.sleep,
        attempts: int = 3,
    ) -> None:
        if not token:
            raise ConfigError("GH_TOKEN is empty; pass a token with actions: read")
        self._token = token
        self._base_url = base_url.rstrip("/")
        self._opener = opener
        self._sleep = sleep
        self._attempts = attempts

    def get(self, path: str, params: Mapping[str, str] | None = None) -> Any:
        url = f"{self._base_url}/{path}" + (f"?{urllib.parse.urlencode(params)}" if params else "")
        request = urllib.request.Request(
            url,
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {self._token}",
                "User-Agent": "dc-ledger-cli-artifact",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        for attempt in range(1, self._attempts + 1):
            try:
                with self._opener(request, timeout=30) as response:
                    return json.load(response)
            except urllib.error.HTTPError as error:
                retryable = error.code in (429, 500, 502, 503, 504)
                if not retryable or attempt == self._attempts:
                    raise CheckFailed(f"GET /{path}: HTTP {error.code} {error.reason}") from None
                self._sleep(_retry_after(error.headers.get("Retry-After"), attempt))
            except (urllib.error.URLError, TimeoutError) as error:
                if attempt == self._attempts:
                    raise CheckFailed(f"GET /{path}: {error}") from None
                self._sleep(2.0**attempt)
        raise AssertionError("unreachable")

    def get_all(self, path: str, key: str) -> list[Any]:
        items: list[Any] = []
        for page in range(1, 11):
            batch = self.get(path, {"per_page": "100", "page": str(page)}).get(key) or []
            items.extend(batch)
            if len(batch) < 100:
                return items
        raise CheckFailed(f"/{path} has more than 1000 {key}")


def _retry_after(header: str | None, attempt: int) -> float:
    try:
        return min(max(float(header or ""), 1.0), 60.0)
    except ValueError:
        return 2.0**attempt


class Workflow:
    """Workflow commands, step outputs and the job summary (https://docs.github.com/actions)."""

    def __init__(self, env: Mapping[str, str], stdout: TextIO = sys.stdout) -> None:
        self._output_file = env.get("GITHUB_OUTPUT")
        self._summary_file = env.get("GITHUB_STEP_SUMMARY")
        self._stdout = stdout

    def output(self, name: str, value: str) -> None:
        if "\n" in value or "\r" in value:
            raise ValueError(f"output {name} must be a single line")
        self._append(self._output_file, f"{name}={value}\n")

    def summary(self, markdown: str) -> None:
        self._append(self._summary_file, markdown)

    def notice(self, message: str) -> None:
        self._command("notice", message)

    def warning(self, message: str) -> None:
        self._command("warning", message)

    def error(self, message: str) -> None:
        self._command("error", message)

    def _command(self, kind: str, message: str) -> None:
        print(f"::{kind} title=cli-artifact::{escape_data(message)}", file=self._stdout)

    @staticmethod
    def _append(path: str | None, text: str) -> None:
        if path:
            with open(path, "a", encoding="utf-8") as stream:
                stream.write(text)


def escape_data(value: str) -> str:
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


# --- Commands ------------------------------------------------------------------------------------------------


def command_pack(args: argparse.Namespace, workflow: Workflow) -> None:
    target = pack(Path(args.source), Path(args.out_dir), args.head_sha, args.attempt, args.epoch)
    digest = sha256_of(target)
    for name, value in (("file-name", target.name), ("path", str(target)), ("sha256", digest)):
        workflow.output(name, value)
    workflow.notice(f"packed {target.name} ({target.stat().st_size} bytes, sha256 {digest})")
    workflow.summary(
        "### dcl CLI package\n\n| | |\n|---|---|\n"
        f"| file | `{target.name}` |\n| size | {target.stat().st_size} bytes |\n| SHA-256 | `{digest}` |\n"
    )


def command_select(env: Mapping[str, str], workflow: Workflow, api: Api, now: datetime) -> None:
    policy = Policy.from_env(env)
    selection = select(api, policy, now, parse_run_id(env.get("FETCH_CLI_RUN_ID", "")))
    for warning in selection.warnings:
        workflow.warning(warning)

    download_dir = tempfile.mkdtemp(prefix="cli-artifact-download-", dir=env.get("RUNNER_TEMP") or None)
    run, artifact = selection.run, selection.artifact
    for name, value in (
        ("run-id", str(selection.run_id)),
        ("head-sha", selection.head_sha),
        ("artifact-id", str(artifact["id"])),
        ("digest", selection.digest),
        ("download-dir", download_dir),
    ):
        workflow.output(name, value)

    server = env.get("GITHUB_SERVER_URL", "https://github.com")
    run_url = f"{server}/{policy.repository}/actions/runs/{selection.run_id}"
    workflow.notice(
        f"using {artifact.get('name')} from {policy.workflow} run {selection.run_id} ({selection.head_sha[:7]})"
    )
    workflow.summary(
        "### dcl CLI provenance\n\n| | |\n|---|---|\n"
        f"| {policy.workflow} run | [{selection.run_id}]({run_url}), {run.get('event')}, attempt {run.get('run_attempt')}, "
        f"finished {run.get('updated_at')} |\n"
        f"| commit | `{selection.head_sha}` ({policy.branch} is {selection.ahead_by} commit(s) ahead) |\n"
        f"| artifact | `{artifact.get('name')}` (id {artifact['id']}, {artifact.get('size_in_bytes')} bytes, "
        f"expires {artifact.get('expires_at')}) |\n"
        f"| API digest | `{selection.digest}` |\n"
    )


def command_verify(args: argparse.Namespace, env: Mapping[str, str], workflow: Workflow, runner: Runner) -> None:
    policy = Policy.from_env(env)
    run_id = parse_run_id(args.run_id)
    head_sha, digest = args.head_sha, args.digest
    if run_id is None or not SHA_PATTERN.match(head_sha) or not DIGEST_PATTERN.match(digest):
        raise ConfigError("verify needs --run-id, a full --head-sha and a sha256 --digest from select")

    tarball = single_download(Path(args.download_dir))
    file_sha = sha256_of(tarball)
    check_digest(file_sha, digest)

    attested = runner(attestation_command(tarball, policy, head_sha), env)
    if attested.returncode != 0:
        detail = (attested.stderr or attested.stdout).strip().splitlines()
        raise CheckFailed(f"attestation verification failed: {detail[-1] if detail else 'no output'}")
    try:
        run_ids = attested_run_ids(json.loads(attested.stdout))
    except json.JSONDecodeError as error:
        raise CheckFailed(f"gh attestation verify printed invalid JSON: {error}") from error
    if run_id not in run_ids:
        raise CheckFailed(f"the attestation names run(s) {sorted(run_ids) or 'none'}, not the selected run {run_id}")

    dest = Path(args.dest) if args.dest else Path(tempfile.mkdtemp(prefix="dcl-", dir=env.get("RUNNER_TEMP") or None))
    cli = unpack(tarball, dest)
    version_run = runner([str(cli), "--version"], without_secrets(env))
    if version_run.returncode != 0:
        raise CheckFailed(f"dcl --version exited with {version_run.returncode}: {version_run.stderr.strip()[:300]}")
    version = check_version(version_run.stdout, head_sha)

    for name, value in (("cli-dir", str(cli.parent)), ("cli-path", str(cli)), ("version", version)):
        workflow.output(name, value)
    workflow.notice(f"verified dcl {version} (sha256 {file_sha[:12]}…, attested by {policy.workflow} run {run_id})")
    workflow.summary(
        f"| file SHA-256 | `{file_sha}` |\n"
        f"| attestation | verified: {policy.workflow_path} on {policy.source_ref} at `{head_sha[:7]}`, run {run_id} |\n"
        f"| dcl --version | `{version}` |\n"
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    packer = commands.add_parser("pack", help="pack the published CLI into the tarball ci uploads")
    packer.add_argument("--source", required=True, help="the dotnet publish output directory")
    packer.add_argument("--out-dir", required=True)
    packer.add_argument("--head-sha", required=True, help="the commit that was built (GITHUB_SHA)")
    packer.add_argument("--attempt", required=True, type=int, help="GITHUB_RUN_ATTEMPT")
    packer.add_argument("--epoch", required=True, type=int, help="the commit time, in seconds since 1970")
    commands.add_parser("select", help="choose the ci run and CLI artifact")
    verify = commands.add_parser("verify", help="verify, unpack and smoke-test the downloaded CLI")
    verify.add_argument("--run-id", required=True)
    verify.add_argument("--head-sha", required=True)
    verify.add_argument("--digest", required=True)
    verify.add_argument("--download-dir", required=True)
    verify.add_argument("--dest", default="", help="an empty directory to unpack into (default: a new temp dir)")
    return parser


def main(
    argv: list[str],
    env: Mapping[str, str] | None = None,
    *,
    stdout: TextIO = sys.stdout,
    api: Api | None = None,
    runner: Runner = run_command,
    now: datetime | None = None,
) -> int:
    args = build_parser().parse_args(argv)
    env = os.environ if env is None else env
    workflow = Workflow(env, stdout)
    try:
        if not hasattr(tarfile, "data_filter"):
            raise ConfigError("Python 3.12 or later is required (tarfile extraction filters)")
        if args.command == "pack":
            command_pack(args, workflow)
        elif args.command == "select":
            client = api or GitHubApi(env.get("GH_TOKEN", ""), env.get("GITHUB_API_URL", "https://api.github.com"))
            command_select(env, workflow, client, now or datetime.now(UTC))
        else:
            if runner is run_command and shutil.which("gh", path=env.get("PATH")) is None:
                raise ConfigError("the GitHub CLI (gh) is required to verify the attestation")
            command_verify(args, env, workflow, runner)
    except ConfigError as error:
        workflow.error(str(error))
        return 2
    except CheckFailed as error:
        workflow.error(str(error))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

# ADR-0007: CLI artifact handoff from ci to scheduled workflows

**Status:** Accepted (spike S17, #12, Oct 28, 2026)

**Context:** `ingest` and `publish` must run the `dcl` that ci built and tested on both operating systems (WS-AC10, S17), not rebuild it. Cross-run artifacts are a known attack path. The runs API's `branch=main` filter also matches a fork's branch named main, and a pull request can change the event its own workflow runs on. A zipped artifact also loses the executable bit. And the first `package` job never ran on main: `dco` is skipped on pushes, and a skipped job upstream skips every job without an explicit status check.

**Options considered:**
- (A) Rebuild from source in each scheduled job. Simple, but the binary isn't the one ci tested, every run needs the SDK, and WS-AC10 isn't met.
- (B) Download "the latest `dcl-linux-x64` on main" with `gh run download` or a third-party action. One step, but it trusts filters that forks can spoof, and a third-party action would hold a token.
- (C) Select the run on the client, download by artifact ID, and verify a GitHub artifact attestation pinned to `ci.yml`, `refs/heads/main` and the commit.

**Decision:** C.
- **Package:** ci publishes `dcl` self-contained for linux-x64 (not single-file, not trimmed) on every event, smoke-tests it (`--version` names the commit; `validate` on the fixture ledger) and packs a deterministic `dcl-linux-x64-<commit>-a<attempt>.tar.gz` (`tools/cli_artifact.py pack`).
- **Upload:** except on pull requests, the tarball is uploaded unzipped (`archive: false`) for 90 days.
- **Sign:** on main, once **CI passed** succeeds, the `attest` job signs it. Only that job holds `id-token: write` and `attestations: write`.
- **Freshness:** ci also runs weekly and on dispatch.
- **Consume:** consumers use `.github/actions/fetch-cli` only. It picks the newest successful `ci.yml` run on main that belongs to this repository, was built from it, was triggered by `push`, `schedule` or `workflow_dispatch`, and whose commit is on main (compare API). It then takes that run's newest-attempt artifact; if there is none, it fails rather than falling back. It downloads by ID with the digest checked, verifies the attestation (`--signer-workflow …/ci.yml --source-ref refs/heads/main --source-digest <commit> --deny-self-hosted-runners`) and that it names the selected run, and checks that `dcl --version` names the commit.
- **Staleness:** fail when main has been ahead of the newest green build for more than 7 days, or the artifact expires within 14 days. The `allow-stale` input is for incidents.
- **Same commit:** consumers check out the selected commit for anything the CLI reads, so code and data match (AC8).

**Consequences:**
- Consumer jobs need `actions: read`, `contents: read` and `attestations: read`.
- Each main build adds one public Sigstore (Rekor) record.
- A red main stops ingest and publish after 7 days, which is intended. Fix ci or dispatch with `allow-stale`.
- Only first-party actions are used, pinned by SHA; Dependabot also watches the composite action.
- Scheduled workflows in a public repository still auto-disable after 60 days without activity, so the weekly ci doesn't replace activity on main.
- Data artifacts on the ci run (WS-AC10) follow once `dcl export` exists (#20).

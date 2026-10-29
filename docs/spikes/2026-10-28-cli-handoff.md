# Spike S17: CLI artifact handoff (#12)

Run Wed Oct 28, 2026.

**Question (#12):** can a scheduled or dispatched workflow download `dcl-linux-x64` from the latest successful `ci` run on main (with `actions: read`) and run it?

## Answer

**Yes, now that ci actually builds it.** Before this change it couldn't: no CLI artifact had ever been produced.

With the new `package` and `attest` jobs in `ci.yml` and `.github/actions/fetch-cli` (ADR-0007), a separate workflow run selected, downloaded, verified and unpacked the CLI in 8 seconds, then ran it. This was proved end to end on the throwaway branch `ci/tmp-12`, with ci and the consumer pointed at that branch instead of main. The dispatched run on main that meets the exit criterion can only happen after merge.

## Findings

| # | Finding | Evidence |
|---|---|---|
| 1 | **The first `package` job never ran.** `dco` is skipped on pushes, and a skipped job upstream skips any job whose `if:` has no status function, even through `CI passed` (`if: always()`). | ci runs 40774454997, 40689016564, 40683893816 and 40621595112 on main: `package` skipped, 0 artifacts |
| 2 | `dotnet publish -r linux-x64` with a locked restore fails with NU1004 in every referenced library. The runtime identifier reaches their restore, and their lock files have no runtime sections. **Fixed** by setting the runtime identifier and `SelfContained` inside `DcLedger.Cli` only (`-p:DclPublishRid=linux-x64`). | PR #53's first runs; `package` green after the fix |
| 3 | Self-contained linux-x64: publish takes 13 s and the tarball is **37.4 MB** (37,374,510 bytes). `dcl --version` prints `0.1.0+<full commit SHA>`. `dcl validate`, which applies the Eastern-time rules, runs with the runner's ICU and tzdata. | ci run 40791289363 (`ci/tmp-12`) |
| 4 | With `archive: false`, the artifact's REST `digest` (`sha256:…`) and upload-artifact's `artifact-digest` both equal the file's SHA-256. | Same run; the package job checks this on every upload |
| 5 | The attestation (actions/attest v4.2.2, SLSA provenance) takes 10 s. `gh attestation verify` on ubuntu-24.04: (a) accepts `--signer-workflow <owner>/<repo>/.github/workflows/ci.yml` without a ref; (b) enforces `--source-ref` and `--source-digest`. Its `invocationId` names the run (`…/actions/runs/<id>/attempts/<n>`). The consumer job needs only `actions: read`, `contents: read` and `attestations: read`. | tmp-12 run 40791601102 |
| 6 | The compare API lists a merged pull request's own commit with the same first parent as the merge commit. In #52, `31e6deb` (written 21:40) and the merge `2eb6567` (21:44) both have `2cc6c63` as first parent. So the staleness clock walks main's first-parent line back from the head instead of taking the first match. | `compare/2cc6c63...main` |

## Negative tests

From tmp-12 run 40791601102:

| Case | Result |
|---|---|
| Main as it is today: the newest green run (40774454997) has no CLI artifact | Refused. No fallback to an older build |
| PR #53's ci run, pinned with `run-id` | Refused: not a successful run. It failed on DCO, so the event check never ran here; unit tests cover that check |
| The genuine tarball, checked against `refs/heads/main` | Refused: "expected SourceRepositoryRef to be refs/heads/main, got refs/heads/ci/tmp-12" |
| The genuine tarball with another run ID | Refused: "the attestation names run(s) [40791289363]" |
| A modified tarball with its own digest | Refused: no attestation for that digest (HTTP 404) |
| A modified tarball with the API digest | Refused: digest mismatch |
| Fork runs, `pull_request*` events, other workflows, expired artifacts, path traversal and links in the tarball | Unit tests in `tools/test_cli_artifact.py` |

## Decision

ADR-0007.

## Still to do

- After merge, the first push to main builds and signs the CLI. Then run `gh workflow run spike-cli-handoff.yml --ref main` and close #12 when it prints `dcl --version`.
- #19 (ingest) and #23 (publish) call `.github/actions/fetch-cli`. WS-AC10's data artifacts follow `dcl export` (#20).
- Not measured: the unpacked size, because job logs aren't readable from the session that ran this.
- `ci/tmp-12` left one public attestation, for commit `6404268`, in the repository and the Sigstore log.

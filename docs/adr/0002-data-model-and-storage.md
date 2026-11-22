# ADR-0002: Data model and storage

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** Gate 0A decided that reviewed YAML in git is canonical, DuckDB is rebuilt in CI and never committed, and raw bytes live in a private R2 bucket. Open points were file layout and which records v0.1 needs.

**Options considered:** (A) one YAML file per record — clean PR diffs, no merge conflicts, filename is the immutable ID; (B) one file per record type — fewer files, noisy diffs and conflicts; (C) JSON instead of YAML — stricter parsing, harder human review.

**Decision:** A. Records under `data/`: `jurisdictions/`, `documents/` (ID = SHA-256 of the bytes), `events/`, `actions/` (local actions such as moratorium votes). `fact`, `project`, `entity` and `correction` are deferred because no v0.1 acceptance criterion needs them. Ingest state (`first_seen_utc`, last hash, issue number), raw documents and run logs live in the private R2 bucket, never in git.

**Consequences:** YAML is parsed into static types with unknown keys rejected (which is how forbidden fields are caught) and a 64 KB cap per file. Event times are stored as ISO 8601 with offset and validated against America/New_York. DuckDB exists only to produce Parquet and CSV; there is no SQL access layer or migrations framework.

**Amendment (Nov 15, 2026, issue #117):** `corrections/` joins the record folders: one file per corrections log entry (`id`, `reported_on`, `corrected_on`, optional `record_ids` and `issue`, `summary`). One file per entry, not one log file, so a published entry can't be removed without failing `SLUG_CHANGED`, the same rule that keeps record IDs permanent. This is the log shown on `/methodology/`, not the deferred `correction` record type: it isn't in the public downloads (SPEC §D).

**Amendment (Nov 21, 2026, ADR-0010):** actions gain an optional `next_on`, the date an archived document in `document_ids` gives for the body's next step on the matter. Allowed on any outcome, after `decided_on`, and a published field (raises `sequence`).

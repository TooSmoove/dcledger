# ADR-0002: Data model and storage

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** Gate 0A decided that reviewed YAML in git is canonical, DuckDB is rebuilt in CI and never committed, and raw bytes live in a private R2 bucket. Open points were file layout and which records v0.1 needs.

**Options considered:** (A) one YAML file per record — clean PR diffs, no merge conflicts, filename is the immutable ID; (B) one file per record type — fewer files, noisy diffs and conflicts; (C) JSON instead of YAML — stricter parsing, harder human review.

**Decision:** A. Records under `data/`: `jurisdictions/`, `documents/` (ID = SHA-256 of the bytes), `events/`, `actions/` (local actions such as moratorium votes). `fact`, `project`, `entity` and `correction` are deferred because no v0.1 acceptance criterion needs them. Ingest state (`first_seen_utc`, last hash, issue number), raw documents and run logs live in the private R2 bucket, never in git.

**Consequences:** YAML is parsed into static types with unknown keys rejected (which is how forbidden fields are caught) and a 64 KB cap per file. Event times are stored as ISO 8601 with offset and validated against America/New_York. DuckDB exists only to produce Parquet and CSV; there is no SQL access layer or migrations framework.

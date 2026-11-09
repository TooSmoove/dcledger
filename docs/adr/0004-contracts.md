# ADR-0004: Contracts between modules, CLI and site

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** C# produces the data; Astro/TypeScript renders it. Scheduled workflows call the CLI. Both boundaries must fail loudly when they drift.

**Options considered:** (A) hand-mirrored types plus a shared golden `events.json` fixture tested on both sides; (B) generate TypeScript types from C# — adds a code generator dependency; (C) the site reads `data/` YAML directly — bypasses the quality gate, rejected.

**Decision:** A. Exported `events.json` carries `schema_version: 1`; any breaking change bumps it and the minor version. Ports are defined in Application (`ISourceAdapter`, `IFetcher`, `IDocumentStore`, `IIngestState`, `IReviewQueue`, `IRunLog`, `ILedgerReader`). CLI commands: `dcl ingest --jurisdiction <id> [--dry-run]`, `dcl validate --data <dir>`, `dcl export --data <dir> --status <file> --out <dir>`, and (amended Nov 3, 2026, issue #25) `dcl archive-document --url <https url> --source-type official|news [--file <path>] [--dry-run]`, which keeps one hand-picked document in the private bucket and prints its ledger record (`--file`, amended Nov 9, 2026, issue #101: a copy a person saved in a browser, recorded as `retrieval: manual`). Exit codes: 0 OK, 1 data invalid, 2 usage or configuration error, 3 external failure after retries, 4 bug.

**Amendment (Nov 3, 2026, issue #25):** `events.json` gained a top-level `actions` array within schema version 1. Adding a key that an older reader can ignore isn't a breaking change; renaming, removing or retyping one is.

**Consequences:** A drift fails one contract test in .NET or Node instead of breaking the site silently. Publish feeds the CLI an `ingest-status.json` built from ingest run history, so the CLI never calls the GitHub API to learn freshness.

# ADR-0004: Contracts between modules, CLI and site

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** C# produces the data; Astro/TypeScript renders it. Scheduled workflows call the CLI. Both boundaries must fail loudly when they drift.

**Options considered:** (A) hand-mirrored types plus a shared golden `events.json` fixture tested on both sides; (B) generate TypeScript types from C# — adds a code generator dependency; (C) the site reads `data/` YAML directly — bypasses the quality gate, rejected.

**Decision:** A. Exported `events.json` carries `schema_version: 1`; any breaking change bumps it and the minor version. Ports are defined in Application (`ISourceAdapter`, `IFetcher`, `IDocumentStore`, `IIngestState`, `IReviewQueue`, `IRunLog`, `ILedgerReader`). CLI commands: `dcl ingest --jurisdiction <id> [--dry-run]`, `dcl validate --data <dir>`, `dcl export --data <dir> --status <file> --out <dir>`, and (amended Nov 3, 2026, issue #25) `dcl archive-document --url <https url> --source-type official|news [--file <path>] [--dry-run]`, which keeps one hand-picked document in the private bucket and prints its ledger record (`--file`, amended Nov 9, 2026, issue #101: a copy a person saved in a browser, recorded as `retrieval: manual`). Exit codes: 0 OK, 1 data invalid, 2 usage or configuration error, 3 external failure after retries, 4 bug.

**Amendment (Nov 3, 2026, issue #25):** `events.json` gained a top-level `actions` array within schema version 1. Adding a key that an older reader can ignore isn't a breaking change; renaming, removing or retyping one is.

**Amendment (Nov 15, 2026, issue #113):** every document in `events.json` (under events and actions) gained `retrieval`, `fetched` or `manual`, as its last key, within schema version 1. The site defaults a missing key to `fetched`. The wording shown for `manual` is one constant on each side of the boundary (`RetrievalNote.Manual` in Publishing, `MANUAL_RETRIEVAL_TEXT` in `site/src/lib/radar.js`), each pinned by a test to the same sentence.

**Amendment (Nov 15, 2026, issue #115):** within schema version 1, each jurisdiction in `events.json` gained `coverage_tier` (`watched`, `decisions` or `registered`) as its last key, and each action gained nine keys after `documents`: `instrument_number`, `threshold`, `scope`, `effective_on`, `expires_on`, `early_termination`, `exemptions`, `successor_id` and `status`, each always written and null when not stated. The site defaults a missing tier to `watched` and missing terms to null. A jurisdiction that isn't watched is written with `last_checked_utc: null` and `stale: false`. The status wording is one table on each side (`RssFeedWriter.StatusText`, `actionStatusText` in `site/src/lib/radar.js`), each pinned by a test.

**Amendment (Nov 15, 2026, issue #117):** `dcl export` also writes `site-data/corrections.json` (`schema_version` 1, `opened_on`, `corrections`), which the methodology page reads with `site/src/lib/corrections.js`. It is a second site-data file rather than a key in `events.json` because it isn't part of the public data contract: it is never served or downloaded. The shapes are pinned on both sides against `site/tests/fixtures/corrections.json` (`CorrectionsExporterTests`, `site/tests/corrections.test.mjs`); the site rejects any other schema version and any malformed entry, so a bad log fails the build.

**Amendment (Nov 16, 2026, issue #132):** within schema version 1, every document in `events.json` gained two keys after `retrieval`: `kind` (`minutes`, `legislation`, `agenda`, `agenda_packet`, `notice`, `news_release`, `news_article`, `other`, or null) and `thumbnail` (`{"path": "/thumbnails/{sha256}.webp", "width", "height"}`, or null), each always written. `dcl export` copies each listed document's reviewed picture to `public/thumbnails/{sha256}.webp`, a new path. The site defaults both keys to null and, whatever the data says, shows a picture only for `minutes` and `legislation` (`site/src/lib/thumbnails.js`, mirroring `Thumbnail.MayShow`). The CSV and Parquet downloads are unchanged.

**Amendment (Nov 21, 2026, ADR-0010):** within schema version 1, each action in `events.json` gained `next_on` (`YYYY-MM-DD`, or null) after `status`, always written; `actions.csv` and `actions.parquet` gained it as their last column. The site defaults a missing key to null.

**Consequences:** A drift fails one contract test in .NET or Node instead of breaking the site silently. Publish feeds the CLI an `ingest-status.json` built from ingest run history, so the CLI never calls the GitHub API to learn freshness.

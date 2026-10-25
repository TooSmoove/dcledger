# ADR-0001: Module boundaries and dependency direction

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** Clean Architecture is required. The walking skeleton crosses fetch, storage, review, validation, export and the site. A solo maintainer needs few moving parts.

**Options considered:** (A) three projects: Core, Infrastructure, Cli — fastest, but adapters and storage blur and Infrastructure becomes a god assembly; (B) six projects; (C) nine or more, one per component — maximal isolation, heavy ceremony for v0.1.

**Decision:** B. Projects: `DcLedger.Domain`, `DcLedger.Application`, `DcLedger.Adapters.Legistar`, `DcLedger.Infrastructure`, `DcLedger.Publishing`, `DcLedger.Cli`. Dependencies point inward only: Domain ← Application ← {Adapters.Legistar, Infrastructure, Publishing} ← Cli. Domain uses the BCL only. Adapters never reference Infrastructure or each other. Cli is the only composition root. `site/` reads only exported files under `out/site-data/`, never `data/`.

**Consequences:** One adapter project per agenda platform; a new Legistar jurisdiction is YAML, not code. Heavy packages (DuckDB, Ical.Net, AWSSDK) stay out of Domain and Application. `DcLedger.Architecture.Tests` enforces these rules by reading assembly references with reflection, so a forbidden reference fails CI.

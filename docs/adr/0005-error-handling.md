# ADR-0005: Error handling

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** Expected failures (bad data, a source changing format, a transient outage) must be reported precisely; bugs must crash loudly. A failed scheduled run is also the freshness signal (AC10).

**Options considered:** (A) exceptions everywhere, mapped to exit codes at the top; (B) returned errors for expected failures, typed exceptions for I/O after retries, crashes for bugs; (C) result types everywhere, possibly via a library — heavy and adds a dependency.

**Decision:** B. The validator returns a list of errors (file, record ID, rule) and never throws. Adapters throw `SourceUnavailable`, `SourceRejected` or `SourceFormatChanged`. HTTP uses the standard resilience handler: 3 retries with jitter on 408, 429 and 5xx, honoring Retry-After, 30 s timeout; never retries 401, 403, 404 or a robots.txt disallow. The CLI maps outcomes to exit codes 0/1/2/3/4 (ADR-0004).

**Consequences:** A red ingest job means "not checked" for that jurisdiction and opens or updates a `scraper-broken` issue. No custom retry policies beyond the standard handler. Run logs in R2 record errors; a failed run-log write is logged but doesn't fail the run.

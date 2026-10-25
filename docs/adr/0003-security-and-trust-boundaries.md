# ADR-0003: Security and trust boundaries

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** There is no inter-process communication; every boundary is a network call or a file. Untrusted inputs: fetched web content (Legistar JSON, agenda bytes) and YAML in pull requests from forks. Credentials: an R2 key for the private document bucket, an R2 key for the public basemap bucket, and a Cloudflare Pages deploy token.

**Options considered:** (A) a GitHub environment per credential plus per-job `permissions` — proven in spike S2/S3; (B) repository secrets plus per-job `permissions` — simpler, but every workflow on main can read every secret; (C) a separate private repository for ingest — strongest isolation, double the maintenance.

**Decision:** A. Environments restricted to main: `r2-docs` (ingest only), `r2-basemap` (basemap only), `cloudflare-pages` (publish only). Default workflow permissions `contents: read`, raised per job. No `pull_request_target`; fork PRs get no secrets; every action pinned to a full commit SHA. Fetched bytes are hashed and stored; only Legistar JSON is parsed in v0.1, into static types, with a 50 MB cap and 30 s timeout. Review issues never contain original titles; case numbers are filtered to `[A-Za-z0-9-]` and @-mentions are neutralized.

**Consequences:** Account ID and bucket names are repository variables, not secrets. Publish holds no R2 document credentials and fails if any are present (WS-AC9). Personal information that slips into git requires the history-rewrite runbook.

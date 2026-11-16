# ADR-0003: Security and trust boundaries

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** There is no inter-process communication; every boundary is a network call or a file. Untrusted inputs: fetched web content (Legistar JSON, agenda bytes) and YAML in pull requests from forks. Credentials: an R2 key for the private document bucket, an R2 key for the public basemap bucket, and a Cloudflare Pages deploy token.

**Options considered:** (A) a GitHub environment per credential plus per-job `permissions` — proven in spike S2/S3; (B) repository secrets plus per-job `permissions` — simpler, but every workflow on main can read every secret; (C) a separate private repository for ingest — strongest isolation, double the maintenance.

**Decision:** A. Environments restricted to main: `r2-docs` (ingest only), `r2-basemap` (basemap only), `cloudflare-pages` (publish only). Default workflow permissions `contents: read`, raised per job. No `pull_request_target`; fork PRs get no secrets; every action pinned to a full commit SHA. Fetched bytes are hashed and stored; only Legistar JSON is parsed in v0.1, into static types, with a 50 MB cap and 30 s timeout. Review issues never contain original titles; case numbers are filtered to `[A-Za-z0-9-]` and @-mentions are neutralized.

**Consequences:** Account ID and bucket names are repository variables, not secrets. Publish holds no R2 document credentials and fails if any are present (WS-AC9). Personal information that slips into git requires the history-rewrite runbook.

**Amendment (Nov 16, 2026, issue #132):** a third dispatch-only workflow uses `r2-docs`: `thumbnail` reads one archived document by its SHA-256 (it never writes the bucket) and renders its first page for review, so publish still holds no document credentials (WS-AC9) and builds pictures only from what is committed. The picture is an Actions artifact for 3 days, rendered only for records already marked as official minutes or legislation; it reaches the site only after a person checks it for private individuals' names and merges it. Saved HTML is drawn by headless Chrome with a no-script, no-request Content-Security-Policy written into the copy and every request sent to a closed proxy port (`tools/render_thumbnail.py`).

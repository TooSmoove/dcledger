# ADR-0006: Testing strategy

**Status:** Accepted (Gate 0B, Oct 25, 2026)

**Context:** A solo maintainer needs fast, offline, deterministic tests that still prove the walking skeleton end to end. The project is FOSS, so contributors must be able to run everything without credentials.

**Options considered:** (A) a test pyramid — unit, golden-file, adapter fixture tests and one offline end-to-end test; (B) emulator-heavy — MinIO for storage, mock servers in containers; (C) mostly live tests against real services.

**Decision:** A, with xunit.v3, Shouldly, Verify.XunitV3 and WireMock.Net.Minimal for .NET and node:test for the site. Regression tests are required for every bug fix that doesn't need a complicated harness; every parser bug gets a fixture first. One permanent end-to-end smoke test runs recorded Legistar responses through ingest, validate, export and the Astro build. Real Legistar, R2 and GitHub are exercised only by the scheduled ingest, which doubles as the live smoke test.

**Consequences:** The whole suite runs offline in minutes. Fixtures are government sources only and scrubbed of personal information. There is no emulator to maintain; R2 behavior is pinned by the spike evidence and the live ingest.

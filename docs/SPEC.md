# dc-ledger — Project Brief (v0.1)

**Status:** Gate 0A approved Oct 25, 2026 · Owner: Keith (@TooSmoove).
Verified by two research passes. Items still unverified were turned into Week-1 spikes (§I); results are recorded in `docs/spikes/`.

## A. Problem & users

**Primary users: Triad journalists.** Today they learn about data-center hearings, moratoria and rezonings by checking each body's agenda portal by hand. Guilford County and Winston-Salem use Legistar; Greensboro uses eSCRIBE. They miss items, and they can't prove what a document said at the time they read it.

dc-ledger gives them one 30-day Hearing Radar, published as a web page, iCal, RSS and CSV. Every item is reviewed by a person before publication and links to its official source, retrieval time and SHA-256 hash. Planning staff are secondary users.

## B. v0.1 success criteria

Due live and unlisted by **Sun Nov 22, 2026**.

- **AC1 Coverage:** Given date D, when /radar is built, it lists every reviewed item in [D, D+30 days], sorted by start time, for:
  - Guilford County BOC and Winston-Salem City Council (automated ingest)
  - Greensboro City Council (manual YAML entry)
- **AC2 Provenance:** Every public item shows the source URL, the retrieval time (stored in UTC, shown in Eastern with offset) and the SHA-256 of the retrieved bytes. The gate fails any record missing one of these.
- **AC3 iCal across DST:**
  - An Oct 15, 2026 5:30 p.m. meeting serializes as `DTSTART;TZID=America/New_York:20261015T173000` and resolves to 21:30Z.
  - A Nov 5, 2026 5:30 p.m. meeting resolves to 22:30Z.
  - Feeds embed the pinned, hand-written VTIMEZONE (2007+ rules), checked byte for byte by a golden test.
  - UID = hash(jurisdiction, platform, platform meeting ID), stable across rebuilds.
- **AC4 Cancellations:** A cancelled meeting keeps its UID and gets `STATUS:CANCELLED` and SEQUENCE+1, an RSS entry, and visible "Cancelled" text on /radar.
- **AC5 RSS:** Valid RSS 2.0, one item per reviewed change (new, updated or cancelled), with stable GUIDs.
- **AC6 Labels:** Each item carries exactly one label: "reported (news)" or "confirmed (official record)". "Confirmed" requires a document row from an official platform.
- **AC7 CSV safety:** UTF-8 with BOM, RFC 4180 quoting. Text cells starting with `=` `+` `-` `@`, tab or CR get a leading apostrophe (text columns only; documented in the data dictionary). A fixture containing all six triggers round-trips.
- **AC8 Nothing unreviewed is public:** The site builds only from merged YAML on main, and a test proves it needs nothing else.
- **AC9 Review SLA:** Given an agenda posted at time T, a review issue exists by T+12h. T is the platform's agenda-published timestamp (`EventAgendaLastPublishedUTC` when the Legistar API is used). If the fallback path exposes no such timestamp, AC9 is verified by cadence: ingest starts at least every 6 hours, with start skew logged.
- **AC10 Freshness:** Each jurisdiction shows "last checked" in Eastern time; a text-plus-icon stale flag appears after 48 hours without a successful check. For Greensboro, "last checked" is the reviewer's last manual check.
- **AC11 Unlisted:** `noindex` meta tag plus `X-Robots-Tag: noindex` header; no sitemap.
- **AC12 Quality:** axe reports zero critical or serious issues; Lighthouse mobile ≥ 90 on /radar and the map page; non-map pages ship no `<script>` (asserted in CI).
- **AC13 Basemap host:** PMTiles load from the custom domain. CI asserts no `r2.dev` reference in the built output, and that a ranged GET with an `Origin` header returns 206 plus `Access-Control-Allow-Origin`.

## C. Walking skeleton (Guilford only, by Sun Nov 8, 2026)

| Component | Role |
|---|---|
| DcLedger.Domain | Records, UID hashing, DST-safe instant conversion, status and label enums. No I/O. |
| DcLedger.Application | Ingest, classify, propose-review and publish use cases, written over ports |
| DcLedger.Cli | System.CommandLine 2.0.x: `ingest`, `validate`, `build-data`, `export`. Published as linux-x64. |
| Fetcher | IHttpClientFactory, resilience and rate limiter; honors robots.txt; identifying user agent |
| DcLedger.Adapters.Legistar | Web API events/agenda items; if token-gated, per-meeting iCal + meeting-detail HTML (where robots.txt allows). EventDate + EventTime → America/New_York → UTC. |
| DcLedger.Storage.R2 | AWSSDK.S3 4.x, both checksum modes WHEN_REQUIRED, DisablePayloadSigning. Private bucket: raw bytes, hash manifest, run logs. |
| Classifier | Deterministic keyword rules. No AI. |
| Review queue | GitHub Issue with neutral placeholder title, case number, hash, retrieval time, ready-to-paste YAML (incl. document row). Never the original title. |
| `data/` YAML | Canonical records |
| DcLedger.Validation | Hand-written C# quality gate |
| DcLedger.Export | DuckDB (DuckDB.NET.Data.Full 1.5.6.1, CI only, never committed); JSON, CSV, ICS (Ical.Net + pinned VTIMEZONE), RSS (System.ServiceModel.Syndication), Parquet |
| `site/` (Astro 7.x) | /radar, item pages, map page (MapLibre GL JS 6.x + pmtiles 4.x) |
| Public R2 basemap | Protomaps NC extract. r2.dev allowed for the skeleton only. |
| Cloudflare Pages | Static deploy |
| Workflows | `ci` (ubuntu + windows matrix), `ingest` (cron 6h), `publish` (push to main), `basemap` (manual; only job holding the public-bucket token) |

- **WS-AC1:** `dotnet build`/`test` green on ubuntu-latest and windows-latest; Astro build green on Linux.
- **WS-AC2:** Ingesting the same fixtures twice gives byte-identical output and hashes, and no new issue.
- **WS-AC3:** A live Guilford ingest writes raw bytes and the manifest to the private bucket and opens one sanitized issue (no original title; @-mentions neutralized).
- **WS-AC4:** Merging the YAML PR triggers publish; the item appears on /radar.
- **WS-AC5:** The gate rejects a missing document row, an offset-less datetime, and forbidden fields (`original_title`, petitioner names), one failing fixture each.
- **WS-AC6:** `.ics` validates; DST golden test passes on both operating systems.
- **WS-AC7:** Map shows OpenStreetMap attribution; table fallback works without JavaScript.
- **WS-AC8:** Guilford's Nov 5, 2026 5:30 p.m. moratorium decision is live, entered manually, "confirmed" only once an official record exists.
- **WS-AC9:** Anonymous GET on the private bucket is denied; publish fails if R2 document credentials are in its environment.
- **WS-AC10:** Ingest runs the linux-x64 CLI artifact from `ci`; data artifacts attached to the CI run.

## D. Out of scope for v0.1

- **Coverage:** jurisdictions beyond the three; eSCRIBE adapter (plan week 6); automated NC DAQ, Forsyth EAP, NCUC, NCGA ingest (manual entries only); parcels and GIS layers.
- **Features:** project/entity pages; search and filter UI; DuckDB-WASM; PDF extraction (PdfPig deferred); email, accounts, notifications, analytics; any LLM or AI.
- **Publishing:** public mirror of agenda packets; versioned data releases, Frictionless, Zenodo; SEO and indexing.
- **Process:** bot-authored PRs, a GitHub App, required approvals.
- **Deferred migrations:** Workers, DuckDB 2.0, AOT publishing.

## E. Non-negotiable constraints

- **Platforms:** .NET 10 LTS (supported to Nov 2028). Dev on Windows; CI on Linux plus a Windows matrix leg. Node pinned in `.nvmrc` to Astro 7's engines requirement. Last two versions of evergreen browsers plus iOS Safari.
- **Privacy:** Public: neutral titles, case numbers, meeting metadata, source URLs, hashes, retrieval times, basemap. Private R2 only: agenda packets, original titles, manifest, run logs. No private individuals' names in git, site, feeds, downloads or issues. Fixtures scrubbed of PII. No cookies, analytics, third-party scripts or AI services. Git history is permanent, so PII is caught before merge; a runbook covers history rewrites.
- **Security & trust boundaries:** All fetched content is untrusted (AngleSharp parsing; never `set:html`; RFC 5545 escaping; CSV guard; size caps and parse timeouts; sanitized issue bodies). `ingest`: `contents: read`, `issues: write`, private-bucket token. `publish`: `contents: read`, Pages deploy token only. No `pull_request_target`; fork PRs get no secrets; actions pinned by SHA; Dependabot and CodeQL on. No CAPTCHA or bot-wall circumvention.
- **Performance:** Lighthouse mobile ≥ 90; no JS off the map page; map bundle measured and budgeted in CI; PMTiles served by range requests from R2 (Pages 25 MiB per-file limit); publish ≤ 10 minutes.
- **Accessibility:** WCAG 2.2 AA; map has an equivalent table; status in text; times show their zone.
- **Offline/online:** full fixture pipeline runs offline; only `ingest` touches the network; no runtime extension downloads; event table works without JavaScript.
- **Neutrality:** reviewer-written neutral titles; labels applied mechanically; no editorial adjectives.
- **FOSS readiness:** MIT, CC BY 4.0, DCO; README, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, AGENTS.md; license report in CI; no runtime dependency with fee-bearing binary terms.

## F. License

| | Fit with goals (maximum reuse by newsrooms and governments) | Verdict |
|---|---|---|
| **MIT** | Compatible with every verified dependency (BSD-3: MapLibre, pmtiles, Shouldly; MIT: Syndication, DuckDB.NET, WireMock.Net, Astro) | **Chosen** |
| Apache-2.0 | Patent grant; heavier NOTICE handling | Viable, not needed |
| MPL-2.0 | File-level copyleft; friction for agency forks | No |
| GPL-3.0 | Deters government and newsroom reuse | No |

Data: CC BY 4.0. Basemap: ODbL Produced Work in a separate bucket with OSM attribution. JsonSchema.Net removed (fee-bearing binary EULA). WireMock.Net's json-everything dependency is test-only, never redistributed, flagged in the license report. Parcels out of v0.1 (G.S. 132-10 resale concern; statute wording to confirm in S12b).

## G. Stack check (pins)

| Component | Choice | Concern | Spike |
|---|---|---|---|
| Runtime | .NET 10 LTS | None | — |
| CLI | System.CommandLine 2.0.13 | API changed from betas (SetAction/ParseResult) | — |
| YAML | YamlDotNet 18.x (MIT) | No AOT publishing, so AOT caveats don't apply | — |
| DB | DuckDB.NET.Data.Full 1.5.6.1 | Native RIDs and offline Parquet to confirm; AccessViolation under debugger | S7 |
| Object storage | AWSSDK.S3 4.x → R2 | WHEN_REQUIRED checksums + DisablePayloadSigning on every request incl. multipart; ForcePathStyle unknown | S2/S3 |
| iCal | Ical.Net 5.2.x + pinned VTIMEZONE | Never use `FromSystemTimeZone` | S6 |
| RSS | System.ServiceModel.Syndication 10.0.x (MIT) | None | — |
| Validation | Hand-written C# | JsonSchema.Net dropped | — |
| Tests | xunit.v3 4.0.2, Verify.XunitV3 31.25.0, Shouldly 4.3.0, WireMock.Net 2.19.0 | Verify declares xunit ≥ 3.2.2; 4.x compatibility to confirm | S10 |
| Site | Astro 7.x | Stricter compiler; Node requirement | S14 |
| Map | MapLibre GL JS 6.x + pmtiles 4.5 | ESM namespace import; bundle size | CI budget |
| Basemap | Protomaps NC extract on R2 behind custom domain | Self-host (daily builds not CORS-open); size unknown; custom-domain CORS reported flaky | S3/S4 |
| Hosting | Cloudflare Pages | Workers recommended for new projects; migrate after domain exists | — |
| CI | GitHub Actions | Cron delays; 60-day schedule disable on public repos | S5/S12 |
| Ingest source | Legistar Web API | Token status unknown; 1000-row cap; stale timestamps | S1 |
| Greensboro | Manual YAML | eSCRIBE since July 2021 | S9 |

## H. Name check

Repo `dc-ledger` (GitHub repo created as `TooSmoove/dcledger`), namespace `DcLedger.*`, domain best guess ncdcledger.org (alternatives: ncdatacenterledger.org, triaddatacenters.org, triadhearingradar.org). "Ledger" is a Ledger SAS trademark in crypto; "DC" commonly reads as District of Columbia. Register the domain on Cloudflare by Nov 15. Findings: `docs/spikes/`.

## I. Risks & Week-1 spikes (riskiest first)

1. **S1 Legistar access** — events endpoint for `guilford` and `winston-salem`, robots.txt on all hosts. JSON → use API; 403/"Token is required" → email clerks + iCal/HTML fallback; robots disallow → manual entry.
2. **Schedule** — ~40 h across 16 components; build in risk order: Legistar, gate, publish, map last.
3. **S2/S3 R2 round-trip in CI** — single + multipart with v4 flags, ForcePathStyle on/off, anonymous denied, per-bucket tokens, ranged GET + CORS on custom domain.
4. **S13 Lead time** — agenda-published → meeting start over 6 months. G.S. 143-318.12 requires 48-hour notice for special meetings but sets no agenda lead time.
5. **S5 Cron skew** — 14 days of start skew; move to 4h if p95 skew + run time threatens the 12h SLA.
6. **S6 VTIMEZONE** — in-model vs post-serialization injection; golden test on both OSes.
7. **S7 DuckDB** — Parquet `COPY` with autoinstall off on both OSes; win-x64 and linux-x64 runtimes present.
8. **S4 Basemap** — dry-run extract sizes z14/z15; Windows go-pmtiles; font/sprite licenses.
9. **S10 Tests and licenses** — xunit.v3 4.x + Verify; transitive license report; fail on EULA-bearing runtime dependency.
10. **S9 eSCRIBE** — robots.txt and iCal/RSS/JSON feeds.
11. **S12 GitHub** — 60-day "activity" definition; rulesets with zero approvals; DCO app; S12b G.S. 132-10 text.
12. **S14 Astro 7** — escaping golden test; `set:html` ban; Node requirement.
13. **S11 Name/domain** — checks in §H; register by Nov 15.

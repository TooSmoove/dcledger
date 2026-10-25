# dc-ledger — Project Brief (v0.1)

**Status:** Gate 0A approved Oct 25, 2026; amended Oct 25, 2026 after the Week-1 spikes and Gate 0B (see §J). Owner: Keith (@TooSmoove).
Architecture decisions live in `docs/adr/`; spike evidence in `docs/spikes/`.

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
- **AC9 Review SLA (amended):** T is the time an ingest run first sees a published agenda (recorded as `first_seen_utc`). The review issue for that agenda opens in the same ingest run. Ingest starts at least every 6 hours (every 4 hours if spike S5 shows the 12-hour target is at risk), with start delay logged, so the worst case from posting to issue is one cadence interval plus GitHub's scheduling delay, within 12 hours. Legistar's `EventAgendaLastPublishedUTC` is not used: it records the *last* republication, not the first (spike S13).
- **AC10 Freshness:** Each jurisdiction shows "last checked" in Eastern time; a text-plus-icon stale flag appears after 48 hours without a successful check. For Greensboro, "last checked" is the reviewer's last manual check.
- **AC11 Unlisted:** `noindex` meta tag plus `X-Robots-Tag: noindex` header; no sitemap.
- **AC12 Quality:** axe reports zero critical or serious issues; Lighthouse mobile ≥ 90 on /radar and the map page; non-map pages ship no `<script>` (asserted in CI).
- **AC13 Basemap host:** PMTiles load from the custom domain. CI asserts no `r2.dev` reference in the built output, and that a ranged GET with an `Origin` header returns 206 plus `Access-Control-Allow-Origin`.

## C. Walking skeleton (Guilford only, by Sun Nov 8, 2026)

Modules follow ADR-0001; contracts follow ADR-0004.

| Component | Role |
|---|---|
| DcLedger.Domain | Records (jurisdiction, document, event, local action), UID hashing, Eastern-time rules (DST gap and overlap rejected), status and label enums, keyword classifier. No I/O. |
| DcLedger.Application | Use cases (ingest, validate, export) and ports; the hand-written validator (quality gate) |
| DcLedger.Adapters.Legistar | Web API events and event items (API verified open, no token). EventDate + EventTime → America/New_York → UTC. |
| DcLedger.Infrastructure | Fetcher (IHttpClientFactory, standard resilience, rate limit, robots.txt, identifying user agent, size caps); R2 store and ingest state (AWSSDK.S3 4.0.104.6, both checksum modes WHEN_REQUIRED, DisablePayloadSigning); GitHub review queue; YAML reader |
| DcLedger.Publishing | DuckDB (DuckDB.NET.Data.Full 1.5.6.1, built in CI, never committed); JSON, CSV, ICS (Ical.Net 5.2.3 + pinned VTIMEZONE), RSS (System.ServiceModel.Syndication), Parquet |
| DcLedger.Cli | System.CommandLine 2.0.13: `ingest`, `validate`, `export`. Published as linux-x64. Only composition root. |
| Review queue | GitHub Issue with neutral placeholder title, case number, hash, retrieval and first-seen times, ready-to-paste YAML (incl. document row). Never the original title. |
| `data/` YAML | Canonical records, one file each |
| `site/` (Astro 7.4.2) | /radar, item pages, map page (MapLibre GL JS 6.16 + pmtiles 4.5, loaded on demand) |
| Public R2 basemap | Protomaps NC extract at z14 (370 MB). r2.dev allowed for the skeleton only. |
| Cloudflare Pages | Static deploy (project `dcledger`) |
| Workflows | `ci` (ubuntu-24.04 + windows-2025), `ingest` (cron 6 h), `publish` (green CI on main + every 6 h), `basemap` (manual; only job holding the public-bucket token) |

- **WS-AC1:** `dotnet build`/`test` green on ubuntu-24.04 and windows-2025; Astro build green on Linux.
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
- **Records:** `fact`, `project`, `entity`, `correction` (no AC needs them).
- **Features:** project/entity pages; search and filter UI; DuckDB-WASM; PDF extraction (PdfPig deferred); HTML or iCal fallback adapter and AngleSharp (the Legistar API is open); email, accounts, notifications, analytics; any LLM or AI.
- **Publishing:** public mirror of agenda packets; versioned data releases, Frictionless, Zenodo; SEO and indexing.
- **Process:** bot-authored PRs, a GitHub App, required approvals.
- **Deferred migrations:** Workers, DuckDB 2.0, AOT publishing.

## E. Non-negotiable constraints

- **Platforms:** .NET 10 LTS (supported to Nov 2028), exact SDK pinned in `global.json` and CI (10.0.402). Dev on Windows; CI on pinned runner images (ubuntu-24.04, windows-2025). Node ≥ 22.23.4 pinned in `.nvmrc` (Astro 7.4.2 engines). Last two versions of evergreen browsers plus iOS Safari.
- **Privacy:** Public: neutral titles, case numbers, meeting metadata, source URLs, hashes, retrieval times, basemap. Private R2 only: agenda packets, original titles, ingest state, run logs. No private individuals' names in git, site, feeds, downloads or issues. Fixtures scrubbed of PII. No cookies, analytics, third-party scripts or AI services. Git history is permanent, so PII is caught before merge; a runbook covers history rewrites.
- **Security & trust boundaries (ADR-0003):** All fetched content is untrusted (static-typed JSON parsing; never `set:html`; RFC 5545 escaping; CSV guard; size caps and timeouts; sanitized issue bodies). Credentials live in GitHub environments restricted to main: `r2-docs` (ingest), `r2-basemap` (basemap), `cloudflare-pages` (publish). Default workflow permissions `contents: read`, raised per job. No `pull_request_target`; fork PRs get no secrets; actions pinned by full SHA; Dependabot and CodeQL on. No CAPTCHA or bot-wall circumvention.
- **Performance:** Lighthouse mobile ≥ 90; no JS off the map page; on the map page MapLibre loads only when the user opens the map (bundle 289 KB gzip, measured); PMTiles served by range requests from R2 (Pages 25 MiB per-file limit); publish ≤ 10 minutes.
- **Accessibility:** WCAG 2.2 AA; map has an equivalent table; status in text; times show their zone.
- **Offline/online:** full fixture pipeline runs offline; only `ingest` touches the network; no runtime extension downloads (verified: parquet, json, icu statically linked); event table works without JavaScript.
- **Neutrality:** reviewer-written neutral titles; labels applied mechanically; no editorial adjectives.
- **FOSS readiness:** MIT, CC BY 4.0, DCO (enforced by a CI check); README, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, AGENTS.md; license report in CI; no runtime dependency with fee-bearing binary terms.

## F. License

| | Fit with goals (maximum reuse by newsrooms and governments) | Verdict |
|---|---|---|
| **MIT** | Compatible with every verified dependency (BSD-3: MapLibre, pmtiles, Shouldly; MIT: Syndication, DuckDB.NET, Ical.Net, YamlDotNet, Astro; Apache-2.0: AWSSDK, xunit.v3, WireMock.Net.Minimal, NodaTime) | **Chosen** |
| Apache-2.0 | Patent grant; heavier NOTICE handling | Viable, not needed |
| MPL-2.0 | File-level copyleft; friction for agency forks | No |
| GPL-3.0 | Deters government and newsroom reuse | No |

Data: CC BY 4.0. Basemap: ODbL Produced Work in a separate bucket with OSM attribution. JsonSchema.Net removed (fee-bearing binary EULA). Full WireMock.Net replaced by WireMock.Net.Minimal, which drops its OSMF-EULA dependencies (spike S10). Parcels out of v0.1 (G.S. 132-10 resale concern; statute wording to confirm in S12b).

## G. Stack (verified pins)

| Component | Pin | Evidence |
|---|---|---|
| Runtime | .NET 10 LTS, SDK 10.0.402 exact | S6/S7/S10 on Linux + Windows; 10.0.403 feed entry was undownloadable on Oct 25 |
| CLI | System.CommandLine 2.0.13 | NuGet (pass 1) |
| YAML | YamlDotNet 18.1.0 | NuGet, MIT |
| DB | DuckDB.NET.Data.Full 1.5.6.1 (engine 1.5.6) | S7: natives for linux-x64/win-x64; Parquet offline |
| Object storage | AWSSDK.S3 4.0.104.6; no ForcePathStyle | S2/S3 against live R2 |
| iCal | Ical.Net 5.2.3 + pinned VTIMEZONE injected before the first VEVENT; never `FromSystemTimeZone` | S6 |
| RSS | System.ServiceModel.Syndication 10.0.x | NuGet, MIT |
| Validation | Hand-written C# | JsonSchema.Net dropped |
| Tests | xunit.v3 4.0.2, Verify.XunitV3 31.25.0, Shouldly 4.3.0, WireMock.Net.Minimal 2.19.0 | S10 (`dotnet run` and `dotnet test` in MTP mode) |
| Site | Astro 7.4.2 | S14: escaping verified, zero script off-map |
| Map | MapLibre GL JS 6.16.0 + pmtiles 4.5.0; go-pmtiles 1.31.2 | S14, S4 |
| Basemap | Protomaps NC extract, z14, `basemap/nc.pmtiles` | S4, S3: 206 + CORS on r2.dev |
| Hosting | Cloudflare Pages (Workers after the domain exists) | — |
| CI | GitHub Actions, pinned images and SHAs | Cron delay under measurement (S5) |
| Ingest source | Legistar Web API, open, no robots.txt | S1 |
| Greensboro | Manual YAML; eSCRIBE robots.txt blocks only PetalBot | S9 |

## H. Name check

Repo `TooSmoove/dcledger`, namespace `DcLedger.*`, Pages project `dcledger`. npm and PyPI names free (S11). Domain best guess ncdcledger.org (alternatives: ncdatacenterledger.org, triaddatacenters.org, triadhearingradar.org); register on Cloudflare by Nov 15. "Ledger" is a Ledger SAS trademark in crypto; "DC" commonly reads as District of Columbia.

## I. Remaining risks & spikes (riskiest first)

1. **Schedule:** the skeleton spans ~20 roadmap items by Nov 8; Claude implements, Keith reviews and merges.
2. **S15 Legistar event items** (#2): classifier precision, the cancellation field, PII rate in titles.
3. **S16 Pages** (#11): `_headers` noindex on direct upload; Pages: Edit-only token.
4. **S17 artifact handoff** (#12): scheduled workflows run the CLI from the latest green `ci`.
5. **S18 map Lighthouse** (#3): ≥ 90 with on-demand MapLibre.
6. **S5 cron delay** (#4): p95 by Nov 8; decides 6 h vs 4 h ingest.
7. **S11 domain / S12 settings / S12b statute** (#10): owner tasks.

Closed with evidence in `docs/spikes/2026-10-25-week1.md`: S1, S2/S3, S4, S6, S7, S9, S10, S13, S14.

## J. Amendments log

- **Oct 25, 2026 — Week-1 spikes and Gate 0B:**
  - AC9 redefined around first-seen time (S13).
  - Module names aligned with ADR-0001 (Validation → Application, Export → Publishing, Storage.R2 → Infrastructure).
  - CLI commands reduced to `ingest`, `validate`, `export` (`build-data` merged into `export`).
  - v0.1 records limited to jurisdiction, document, event, local_action.
  - Publish runs on green CI on main and every 6 h; "last checked" comes from ingest run history.
  - DCO enforced by a CI check; Pages project `dcledger`.
  - Pins and constraints updated from spikes: WireMock.Net.Minimal, basemap z14, on-demand map, DST gap/overlap rejection, Node ≥ 22.23.4, AWSSDK.S3 4.0.104.6, exact SDK pin, pinned runner images, no HTML/iCal fallback.

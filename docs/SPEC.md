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
- **AC9 Review SLA (amended):** T is the time an ingest run first sees a published agenda (recorded as `first_seen_utc`). The review issue for that agenda opens in the same ingest run. Ingest is scheduled hourly, with start delay logged. GitHub skips most scheduled runs (spike S5), so the worst case from posting to issue is the longest gap between ingest starts: 10.8 hours over 14 days of hourly measurement, within 12 hours. Legistar's `EventAgendaLastPublishedUTC` is not used: it records the *last* republication, not the first (spike S13).
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
| DcLedger.Cli | System.CommandLine 2.0.13: `ingest`, `validate`, `export`, `archive-document`. Published as linux-x64. Only composition root. |
| Review queue | GitHub Issue with neutral placeholder title, case number, hash, retrieval and first-seen times, ready-to-paste YAML (incl. document row). Never the original title. |
| `data/` YAML | Canonical records, one file each |
| `site/` (Astro 7.4.2) | /radar, item pages, map page (MapLibre GL JS 6.16 + pmtiles 4.5, loaded on demand) |
| Public R2 basemap | Protomaps NC extract at z14 (370 MB), served from `tiles.ncdcledger.org`; r2.dev is off (AC13). |
| Cloudflare Pages | Static deploy (project `dcledger`) at `ncdcledger.org`; `www` redirects to the apex |
| Workflows | `ci` (ubuntu-24.04 + windows-2025), `ingest` (cron hourly), `publish` (green CI on main + every 6 h), `basemap` (manual; only job holding the public-bucket token) |

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
| Basemap | Protomaps NC extract, z14, `basemap/nc.pmtiles` on `tiles.ncdcledger.org` | S4, S3; AC13 checked by every publish |
| Hosting | Cloudflare Pages, custom domain `ncdcledger.org` (Workers deferred, §D) | AC11 headers verified on the domain |
| CI | GitHub Actions, pinned images and SHAs | Cron delay under measurement (S5) |
| Ingest source | Legistar Web API, open, no robots.txt | S1 |
| Greensboro | Manual YAML; eSCRIBE robots.txt blocks only PetalBot | S9 |

## H. Name check

Repo `TooSmoove/dcledger`, namespace `DcLedger.*`, Pages project `dcledger`. npm and PyPI names free (S11). Domain **ncdcledger.org**, registered on Cloudflare (Oct 27) and live Nov 3: the site at the apex, `www` 301-redirects to it, the basemap at `tiles.ncdcledger.org`. The domain sends no mail (null MX, SPF `-all`, DMARC `p=reject`). "Ledger" is a Ledger SAS trademark in crypto; "DC" commonly reads as District of Columbia.

## I. Remaining risks & spikes (riskiest first)

1. **Schedule:** the skeleton spans ~20 roadmap items by Nov 8; Claude implements, Keith reviews and merges.
2. **S15 Legistar event items** (#2): classifier precision, the cancellation field, PII rate in titles.
3. **S16 Pages** (#11): `_headers` noindex on direct upload; Pages: Edit-only token.
4. **S17 artifact handoff** (#12): scheduled workflows run the CLI from the latest green `ci`.
5. **S18 map Lighthouse** (#3): ≥ 90 with on-demand MapLibre.
6. **S12 settings / S12b statute** (#10): owner tasks. S11 domain done (§H).

Closed with evidence in `docs/spikes/2026-10-25-week1.md`: S1, S2/S3, S4, S6, S7, S9, S10, S13, S14. S5 in `docs/spikes/2026-11-08-cron-skew.md`.

## J. Amendments log

- **Oct 25, 2026 — Week-1 spikes and Gate 0B:**
  - AC9 redefined around first-seen time (S13).
  - Module names aligned with ADR-0001 (Validation → Application, Export → Publishing, Storage.R2 → Infrastructure).
  - CLI commands reduced to `ingest`, `validate`, `export` (`build-data` merged into `export`).
  - v0.1 records limited to jurisdiction, document, event, local_action.
  - Publish runs on green CI on main and every 6 h; "last checked" comes from ingest run history.
  - DCO enforced by a CI check; Pages project `dcledger`.
  - Pins and constraints updated from spikes: WireMock.Net.Minimal, basemap z14, on-demand map, DST gap/overlap rejection, Node ≥ 22.23.4, AWSSDK.S3 4.0.104.6, exact SDK pin, pinned runner images, no HTML/iCal fallback.
- **Nov 3, 2026 — Recording decisions (issue #25, WS-AC8):**
  - Local actions reach the site: `events.json` gains an `actions` array (schema version 1, additive; ADR-0004 amended) and decisions render on the meeting and jurisdiction pages.
  - Actions gain an optional `event_id`; `action_type` and `outcome` become fixed vocabularies; `votes` is ayes-noes.
  - "Confirmed (official record)" for a decision means the platform's action-detail page is archived; approved minutes may be added later as a second document. Every recorded vote is an action, including continuances and tablings.
  - New `dcl archive-document` command and `archive-document` workflow (r2-docs, dispatch only) keep hand-picked sources in the private bucket.
- **Nov 3, 2026 — Domain ncdcledger.org (S11, AC13):**
  - The site is served at `https://ncdcledger.org` (Pages custom domain; `www` 301-redirects to the apex by a zone Redirect Rule). `dcledger.pages.dev` keeps serving the same build with noindex. Publish checks the live site on the domain.
  - The basemap moved from r2.dev to `tiles.ncdcledger.org` (R2 custom domain; CORS allows `https://ncdcledger.org` and `https://dcledger.pages.dev`, GET and HEAD, `Range`). Publish fails before deploying if the build or `BASEMAP_URL` mentions r2.dev, or a ranged GET with `Origin` doesn't return 206 plus `Access-Control-Allow-Origin` (`tools/check_basemap.py`, PR #80). r2.dev access on the bucket is off.
  - Cloudflare's edge-injected analytics beacon (zone Real User Monitoring) is off, per §E privacy and AC12; Pages Web Analytics stays disabled.
  - The domain is locked against spoofing: null MX, SPF `v=spf1 -all`, DMARC `v=DMARC1; p=reject`.
- **Nov 5, 2026 — Outcome watching (ADR-0008, spike S19):**
  - Ingest watches every meeting that has a review issue for 90 days after it and comments on (and reopens) that issue whenever anything non-personal changes: recorded action or result, case status or final-action date, versions, minutes. The reviewer still writes `actions/` records and documents by hand; nothing is published automatically (AC8).
  - Sources differ by jurisdiction (S19): Winston-Salem records outcomes and minutes in the Legistar Web API; Guilford records no outcomes in Legistar and posts minutes only on its public meeting pages, which ingest now reads for the minutes link alongside the agenda link.
  - Minutes of watched meetings are archived to the private bucket. A missing platform tally means no `votes`.
  - Outcome suggestions come from an exact-match, per-jurisdiction `outcome_map` keyed by action name (option 6c); unmapped names are listed for the reviewer. `data/jurisdictions/` gains that optional key when it lands (ADR-0004 amendment then).
- **Nov 6, 2026 — Unanimous votes without a tally (issue #25):**
  - Actions gain an optional `unanimous: true` for a vote reported unanimous before the platform posts a count. `votes` stays a pure ayes-noes count and is never inferred from the board's size; alongside a tally, `unanimous` needs no noes.
  - `events.json` actions gain a boolean `unanimous` (schema version 1, additive). The site shows "Unanimous (tally not yet recorded)", or the tally marked "(unanimous)".
- **Nov 6, 2026 — Reschedules and cancellations (issue #29, AC4):**
  - Ingest watches each meeting with a review issue and comments on (and reopens) the issue once per change: a new date or start time, a cancellation mention added or removed, the agenda withdrawn or republished, or the meeting no longer listed before its date (spike S15 decision 3) and listed again. Nothing is marked cancelled automatically; the reviewer edits `events/` (`status: cancelled` or `starts_at`, `sequence` + 1) and the existing ICS and /radar output does the rest.
  - Absence is read narrowly (ADR-0008): a meeting missing after its date, or an empty meeting list, is never reported. Issues opened before this change take a silent baseline.
  - Ingest state gains each tracked meeting's last sighting and a per-jurisdiction list of tracked meetings (`state/tracked/{jurisdiction}.json`); both are additive (schema version 1).
  - The quality gate compares events with the base branch (`dcl validate --previous-data`, run by ci): a changed published field needs a higher `sequence` and `sequence` never goes down (`SEQUENCE_NOT_RAISED`); `jurisdiction_id`, `platform` and `platform_meeting_id` are frozen once published because the UID is built from them (`UID_CHANGED`). This is what makes "SEQUENCE+1, same UID" hold in subscribers' calendars.
- **Nov 8, 2026 — Hourly ingest (spike S5, issue #4, AC9):**
  - GitHub skipped 83% of an hourly probe's slots and 37% of the 6-hour ingest's; the 6-hour schedule left 4 of 25 gaps over 12 hours (up to 13.4 h). Ingest is now scheduled hourly, whose probe's longest gap was 10.8 h. Publish stays every 6 hours.
  - An external trigger (for example a Cloudflare Worker calling `workflow_dispatch`) is the fallback if hourly gaps reach 12 hours; it needs an ADR.
  - `spikes/`, `spikes.yml` and the `cron-skew` probe are removed.

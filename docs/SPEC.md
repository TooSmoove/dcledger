# dc-ledger — Project Brief (v0.1)

**Status:** Gate 0A approved Oct 25, 2026; amended Oct 25, 2026 after the Week-1 spikes and Gate 0B (see §J). Owner: Keith (@TooSmoove).
Architecture decisions live in `docs/adr/`; spike evidence in `docs/spikes/`.

## A. Problem & users

**Primary users: Triad journalists.** Today they learn about data-center hearings, moratoria and rezonings by checking each body's agenda portal by hand. Guilford County and Winston-Salem use Legistar; Greensboro uses eSCRIBE. They miss items, and they can't prove what a document said at the time they read it.

dc-ledger gives them one 30-day Hearing Radar, published as a web page, iCal, RSS and CSV. Every item is reviewed by a person before publication and links to its official source, retrieval time and SHA-256 hash. Planning staff are secondary users.

## B. v0.1 success criteria

Due live and unlisted by **Sun Nov 22, 2026**.

- **AC1 Coverage:** Given date D, when /radar is built, it lists every reviewed item in [D, D+30 days], sorted by start time, for:
  - Guilford County BOC, Winston-Salem City Council and Chatham County BOC (automated ingest)
  - Greensboro City Council, Stokes County BOC and Vance County BOC (manual YAML entry; Chatham, Stokes and Vance amended in, issue #94)
  - Only `watched` jurisdictions count here. Forsyth County and Davidson County are `decisions` (decisions recorded, hearings not watched; amended Nov 15, 2026, issue #118), so their meetings are never on /radar.
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
- **AC10 Freshness:** Each watched jurisdiction (`coverage_tier: watched`, amended Nov 15, 2026, issue #115) shows "last checked" in Eastern time; a text-plus-icon stale flag appears after 48 hours without a successful check. For Greensboro, "last checked" is the reviewer's last manual check, and a manual jurisdiction's stale flag appears after 7 days instead (amended Nov 8, 2026, issue #27). While fresh, the line also prints when the flag would appear, so a build that stops being replaced still tells the reader when to stop trusting it (amended Nov 15, 2026, issue #103).
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
| DcLedger.Publishing | DuckDB (DuckDB.NET.Data.Full 1.5.6.1, built in CI, never committed); JSON, CSV, ICS (Ical.Net 5.2.3 + pinned VTIMEZONE), RSS (System.Xml `XmlWriter`), Parquet |
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

- **Coverage:** watched jurisdictions beyond the six in AC1 (the Halifax and Person stretch candidates of #94 included). Others may join as `decisions` or `registered` (§J, Nov 15, 2026), with no hearing watch: Forsyth and Davidson have joined as `decisions` (#118), and watching either waits for #96 and #114; eSCRIBE adapter (plan week 6); automated NC DAQ, Forsyth EAP, NCUC, NCGA ingest (manual entries only); parcels and GIS layers.
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
| RSS | System.Xml `XmlWriter` (in the runtime) | Replaced System.ServiceModel.Syndication (#30): no extra package, and exact control of the bytes |
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
- **Nov 8, 2026 — RSS feed and Parquet export (issue #30, AC5):**
  - `public/feed.xml`: one RSS 2.0 item per event revision. Export keeps no history, so the GUID is the calendar UID plus `sequence`, which every published change raises (#29); a change or cancellation arrives as a new item. Every date comes from the ledger (`pubDate` the newest document retrieval, `lastBuildDate` the newest `pubDate`), so the same ledger gives the same bytes. `dcl export --site-url` sets the absolute links.
  - Written with the runtime's `XmlWriter` instead of System.ServiceModel.Syndication: one dependency fewer, and the output is fully controlled.
  - `public/events.parquet`: the CSV's rows with native types and documents as a list of structs, via DuckDB `COPY` (snappy, one thread, byte-identical across runs).
  - Local actions (decisions) are not feed items yet (added by #90 below).
- **Nov 8, 2026 — CSV safety and data dictionary (issue #31, AC7):**
  - `events.csv` is UTF-8 with a BOM. Every text cell starting with `=`, `+`, `-`, `@`, tab or CR gets a leading apostrophe, and so does one already starting with an apostrophe, so the guard reverses exactly (drop one leading apostrophe). Number columns are never guarded. `events.parquet` stays unguarded.
  - `docs/data-dictionary.md` documents the encoding, the guard and how to reverse it, and every column of both downloads.
- **Nov 8, 2026 — Decisions in the feed and downloads (issue #90, AC5):**
  - Actions gain an optional `sequence` (default 0, so existing records stay valid), raised by every published change; the revision check (`--previous-data`) enforces it with `SEQUENCE_NOT_RAISED`, and an action's `jurisdiction_id` is frozen (`JURISDICTION_CHANGED`). A hash of the published fields was rejected as the change signal: it would turn typo fixes into feed items and work differently from events.
  - Each decision revision is a feed item (GUID `action:{id}/{sequence}`), merged with event items newest first.
  - `public/actions.csv` (same BOM, RFC 4180 and formula guard as `events.csv`) and `public/actions.parquet` (typed; `decided_on` a DATE). `events.json` actions gain `sequence` (schema version 1, additive).
- **Nov 8, 2026 — Quality gates (issue #33, AC12):**
  - ci runs axe-core 4.14.0 in Chrome (playwright-core 1.64.1, no browser download) on every built page, in light and dark color schemes at phone and desktop widths, and fails on any critical or serious violation; before auditing `dist/` it must see the violations planted in a canary page.
  - Raw HTML is banned in `src/` (`set:html`, `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`), and the no-script rule and the Lighthouse thresholds each have a canary or guard test (`site/README.md`).
- **Nov 8, 2026 — Greensboro and manual jurisdictions (issue #27, AC1, AC10):**
  - Greensboro joins as `platform: manual`, with its July 21 (failed 4-5) and Aug 18 (180-day moratorium, 8-0) votes recorded as `reported_news` decisions pending the eSCRIBE minutes.
  - AC10: a manual jurisdiction is stale after 7 days instead of 48 hours, since each check is a reviewed commit; `events.json` jurisdictions gain `stale_after_hours` (schema version 1, additive) so the site states the right threshold. #96 tracks automating these checks to bring it back down.
  - The gate rejects an event whose platform differs from its jurisdiction's (`PLATFORM_MISMATCH`) and `last_manual_check_utc` on a non-manual jurisdiction; export shows a manual check dated after the export as stale, with a warning. The how-to is in `data/README.md`.
- **Nov 9, 2026 — Archiving browser-saved files (issue #101, AC2, AC6):**
  - Greensboro's eSCRIBE puts every document download (`FileStream.ashx`) behind a Cloudflare managed challenge. Programs get HTTP 403 whatever their user agent, cookies or network; the home page and robots.txt don't block us. We don't defeat the challenge.
  - `dcl archive-document --file <path> --url <source URL>` keeps a copy a person saved in a browser: same hashing, bucket and record, with `retrieved_at` from the file's last-modified time. It runs locally; the workflow can't take a file.
  - Documents gain an optional `retrieval: manual` (absent means `fetched`, so existing records are unchanged). The publisher, not who saved the file, decides the label: a manual official copy can back `confirmed_official`. Exports don't carry `retrieval` yet.
- **Nov 14, 2026 — Rural jurisdictions (issue #94, AC1, §D):**
  - Chatham County joins on Legistar (client `chathamnc`, existing adapter); Stokes and Vance join as `platform: manual`, since both post PDF agendas on their own sites. The top three of the Nov 3 rural research doc; new hyperscale siting is concentrated outside the Triad trio.
  - Backfilled as `reported_news` until minutes are archived: Stokes's Sept 14 Project Delta rezoning (3-2) and July 13 rejection of a six-month moratorium; Vance's April 6 continuance and April 20 approval of the Natelli rezoning on U.S. 158.
- **Nov 15, 2026 — Showing how a document was retrieved (issue #113, AC2):**
  - Exports now carry a document's `retrieval`: `events.csv` and `actions.csv` gain a last column `document_retrieval` (space-separated `fetched` or `manual`, in the record's order); the Parquet `documents` struct gains `retrieval` as its last field; every document in `events.json` gains `retrieval` (schema version 1, additive; ADR-0004 amended).
  - Beside a source a person saved, item and jurisdiction pages, the feed and the calendars say "Saved by a person in a browser (the source does not allow automated downloads)." in text. A fetched copy gets no note.
  - The wording differs from the issue's "the source blocks automated downloads": Vance's and Stokes's copies were saved by hand because each site's robots.txt disallows robots and the fetcher honors it, which is a refusal, not a technical block.
  - No record changed, so no `sequence` was raised: feed items for decisions with a hand-saved source keep their GUIDs and gain the sentence in place.
- **Nov 15, 2026 — Decision terms and coverage tiers (issue #115, AC6, AC7, AC10):**
  - Jurisdictions gain a required `coverage_tier`: `watched` (hearings on the radar plus decisions), `decisions` (decisions recorded after the fact; no hearing watch) or `registered` (listed only). The six AC1 jurisdictions are `watched`. Tiers replace the single "beyond the six" line in §D.
  - Only `watched` carries AC10: the others export `last_checked_utc: null` and `stale: false`, have no calendar, are never ingested and show no freshness line. Each jurisdiction page states its tier in a sentence; /radar's freshness list is watched-only, with the others listed below it.
  - The gate rejects an event in a jurisdiction that isn't watched, and any decision in a `registered` one (`TIER_MISMATCH`).
  - Actions gain nine optional terms: `instrument_number`, `threshold`, `scope`, `effective_on`, `expires_on`, `early_termination`, `exemptions`, `successor_id` and `status` (`in_effect`, `expired`, `superseded`, `repealed`). Text terms are copied as written, on one line of at most 500 characters. The gate (`TERMS_INVALID`) allows terms only on `adopted` or `adopted_amended` decisions, rejects `expires_on` before `effective_on` and `superseded` without `successor_id`, and requires the successor to exist (`FK_MISSING`), be another action in the same jurisdiction and not be decided earlier.
  - `status` is set by a reviewer, never by the clock: the gate is deterministic, so nothing turns `in_effect` into `expired`. The expiry page and feed (next issue) flag a passed `expires_on`.
  - Every term is a published field: changing one raises `sequence`, so each change is a feed item.
  - Exports: `actions.csv` and `actions.parquet` gain the nine columns at the end; `events.json` gains `coverage_tier` and the terms within schema version 1 (ADR-0004 amended). The feed and the decision cards show the terms a record states.
  - Data: Greensboro's Aug 18 moratorium gets its threshold, scope, expiry (Feb 14, 2027) and status from the archived minutes and city releases. No archived document states its start date or ordinance number. Guilford's Nov 5 terms wait for the adopted ordinance or minutes (#112): the archived case page states only the requested terms.
- **Nov 15, 2026 — The public data contract (issue #116, AC3, AC5, AC7, ADR-0004):**
  - `docs/export.md` "Stability" states what never changes: record IDs, page addresses and `#decision-` anchors, calendar UIDs, feed GUIDs, file paths, column names, order, types and meaning, and vocabulary values. Columns, keys, files, vocabulary values and records may be added at any time.
  - A breaking change is announced in `CHANGELOG.md` (Keep a Changelog; new at the repository root, with an `[Unreleased]` section that becomes 0.1.0 when #35 tags) at least 90 days before it ships, and `events.json` bumps `schema_version`.
  - Held by tests: `PublicContractTests` (frozen export paths; `docs/data-dictionary.md` against the CSV headers and Parquet columns in name and order; the dictionary's new "Vocabularies" table against the code's tokens) and the site's frozen page and file list.
  - The about page links the promise, the dictionary and the CHANGELOG. A minimal root `README.md` is added and links the CHANGELOG; LICENSE and the other community files (§E) are not part of this change.
- **Nov 15, 2026 — Methodology, disclosure and corrections log (issue #117, AC6, AC8, AC11, AC12, §E):**
  - New page `/methodology/`, linked from every footer and from /about: how items get in, what the labels mean, provenance, coverage tiers, who publishes this, how to cite (with a worked example built from a real item), and corrections. No script; noindex like every page; Lighthouse mobile now also runs on it.
  - The page states the cadence as it is (AC9 amended): automated sources checked about hourly with the longest measured gap just under 11 hours, manual sources checked by a person with a 7-day stale flag, and the site rebuilt after every merge and at least every 6 hours.
  - Corrections: reported as a GitHub issue (form `.github/ISSUE_TEMPLATE/correction.yml`) until a contact address exists (§H; replaced by the site form on Nov 15, below). Within 48 hours the record is corrected, or the report gets a reply saying why it stands or what the fix is waiting for.
  - The log opens Nov 22, 2026. Entries live one per file in `data/corrections/` (ADR-0002 amended), pass the quality gate (`CORRECTION_INVALID`, `FK_MISSING`) and are permanent (`SLUG_CHANGED`). `dcl export` writes them to `site-data/corrections.json` (ADR-0004 amended); they aren't in the public downloads, so the `correction` record type in §D stays deferred.
  - The disclosure is the owner's wording, kept in `site/src/owner/disclosure.md`; the site's tests fail while it holds the placeholder. Templates for it, for log entries and for replies are in `docs/wording-bank.md`.
- **Nov 15, 2026 — Forsyth and Davidson, decisions only (issue #118, AC1, AC6, AC10, §D):**
  - Forsyth County and Davidson County join as `platform: manual`, `coverage_tier: decisions`: decisions recorded from the official record, no hearing watch, no events or calendar, and no freshness promise (AC10 applies to `watched` only, #115). The site's existing tier sentence and /radar's split already cover them, so no code changed.
  - Where they post differs from the issue: Forsyth uses CivicWeb (iCompass/Diligent), whose robots.txt disallows all robots, so its documents are archived with `--file`; Davidson's Board of Commissioners uses AgendaSuite (Provox), which has no robots.txt, so the archive-document workflow fetches it (its CivicPlus Agenda Center carries only other boards).
  - Davidson, all `confirmed_official` from archived minutes: the Aug 24 six-month moratorium motion (failed 3-3), the Sept 28 vote to schedule a four-month moratorium hearing (4-2), the Oct 26 data-center text amendments (`adopted_amended` 5-2, approved with board-requested revisions) and the Oct 26 moratorium (failed, written 1-6). The Aug and Sept votes were added to the issue's list at the owner's request.
  - Forsyth: rezoning case F-1669 (Jul 30, failed, written 2-5) is `confirmed_official` from the adopted minutes, saved in a browser and archived with `--file`; WFAE's report is kept as a second source. The Sept 28 "Data Center discussion" at a Monday briefing is not recorded: Forsyth posts no briefing minutes, so no official record says what the action was. No `not_advanced` outcome was added (owner decision); it can be added if a record ever needs it.
- **Nov 15, 2026 — Reports through the site, not GitHub (ADR-0009, AC12, §D, §E):**
  - Corrections, questions and requests to stop fetching go through a form at `/report/` (no script) that posts to a Pages Function, `/api/report`. Reports are stored in Cloudflare D1 and read on `/inbox/`, behind Cloudflare Access with the Access JWT verified by the Function. Setup and handling: `docs/reports.md`. This adds one server endpoint ahead of the Workers migration in §D; `_routes.json` keeps every other path static.
  - The reader's email is optional. With one, the reply is an email from the maintainer within 48 hours; without one, the corrections log is the answer. Reports are never published; the log names a report only by number (`R-<n>`), and corrections entries' `issue` key is now `report`.
  - No page links the repository. The about page's links to the stability promise, data dictionary and changelog now point at `/data/` and `/changelog/`, rendered from `docs/stability.md` (moved out of `docs/export.md`), `docs/data-dictionary.md` and `CHANGELOG.md`. A remark plugin rewrites their links for the site, leaves out issue references and stops the build on any link into the repository or mention of its internals.
  - The correction issue template is removed; `.github/ISSUE_TEMPLATE/config.yml` turns off blank issues and links the form.
- **Nov 16, 2026 — Locator maps, first-page pictures and one web font (issue #132, items 2 to 4; AC2, AC8, AC12, §E):**
  - Each jurisdiction page draws a static SVG locator: North Carolina, its county lines and the jurisdiction filled
    (ringed when a city is too small to see), from the Census Bureau's cartographic boundary files (public domain).
    `tools/build_locators.py` builds `site/src/geo/nc-boundaries.json` once and it is committed, so builds stay
    offline; no basemap data is drawn, so no ODbL attribution is needed. The SVG is `role="img"` with an accessible
    name, the credit is a visible caption, and the page ships no script.
  - Documents gain an optional `kind` and, for official `minutes` and `legislation` only, an optional `thumbnail`
    (`sha256`, `width`, `height`, `reviewed`) with its picture in `data/thumbnails/{id}.webp` (ADR-0002's layout gains
    that folder). The picture is rendered from the archived bytes themselves (`tools/render_thumbnail.py` refuses a
    file whose SHA-256 isn't a record's ID), at archive time rather than at publish, because publish holds no
    document credentials (WS-AC9). The quality gate rejects a picture of any other kind (agenda packets stay private),
    one whose file is missing or differs from its hash, an unclaimed file (`THUMBNAIL_INVALID`) and one not marked
    reviewed for private individuals' names (`THUMBNAIL_UNREVIEWED`). Export copies each listed document's picture to
    `public/thumbnails/` and `events.json` gains `kind` and `thumbnail` per document (schema version 1, additive;
    ADR-0004 amended). The `thumbnail` workflow renders a fetched document from the private bucket for review
    (ADR-0003 amended). Pictures are lazy-loaded at a fixed size beside their source.
  - The site self-hosts one font: EB Garamond (SIL OFL 1.1), roman, weights 400 to 700, subset to Latin and the
    punctuation in use, one 37,120-byte WOFF2 preloaded with `font-display: swap` and a metric-matched Times
    fallback (`tools/subset_font.py`). Italic stays on the system serif to keep within 60 KB. The license report
    gains a "vendored" section that checks the font's and the boundaries' hashes and license texts.
- **Nov 17, 2026 — Expiring npm audit exceptions (§E, Gate 0B §6):**
  - The audit job's npm step now pipes `npm audit --omit=dev --json` to `tools/npm_audit_gate.py`. Every advisory at
    any severity still fails, except one listed in `tools/audit-exceptions.json` with its GHSA id, package, a reason
    it doesn't reach readers, and an expiry date; an expired exception fails like none. An npm error fails too.
  - First exception: GHSA-fc5q-39xc-vjpf (picomatch ReDoS, every release through 4.0.7, no fix yet), until
    Dec 31, 2026. picomatch runs only at build time on the repository's own glob patterns, and the site is static.
- **Nov 17, 2026 — Henderson, decisions only (AC1, AC6, §D):**
  - The City of Henderson (Vance County seat, place FIPS 3730660) joins as `platform: manual`,
    `coverage_tier: decisions`, like Forsyth and Davidson: decisions recorded from the record, no hearing watch. First
    decision: the Nov 9, 2026 six-month moratorium (`reported_news` until the minutes are archived). Its locator map
    needs `site/src/geo/nc-boundaries.json` rebuilt with `tools/build_locators.py`.

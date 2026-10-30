# Week-1 spikes (throwaway)

Proves or disproves the unverified items in `docs/SPEC.md` §I before any production code is written.
Results go to `docs/spikes/`; then this folder, `.github/workflows/spikes.yml`, `cron-skew.yml`, `spike-cli-handoff.yml` are deleted.

| Spike | Where it runs | What it answers |
|---|---|---|
| S1 / S9 | `spikes.yml` → `sources` | Is the Legistar Web API open for `guilford` / `winston-salem`? What do the robots.txt files and the eSCRIBE portal allow and expose? |
| S13 | `spikes.yml` → `sources` | Hours between agenda publication and meeting start, last 6 months (only if S1 says the API is open) |
| S4 | `spikes.yml` → `basemap` | go-pmtiles release assets (incl. Windows) and North Carolina extract size at z14 / z15 |
| S6 | `spikes.yml` → `dotnet` (Linux + Windows) | Ical.Net 5.2.3 + pinned VTIMEZONE; DST conversions with `TimeZoneInfo` |
| S7 | `spikes.yml` → `dotnet` | DuckDB.NET.Data.Full 1.5.6.1 natives per OS; Parquet export with no extension downloads |
| S10 | `spikes.yml` → `dotnet` | xunit.v3 4.0.2 + Verify.XunitV3 + Shouldly + WireMock.Net; `dotnet run` vs `dotnet test` (MTP); transitive license report |
| S2 / S3 | `spikes.yml` → `r2` | AWSSDK.S3 4.x against R2 (needs secrets `R2_ACCOUNT_ID`, `R2_PRIVATE_BUCKET`, `R2_PRIVATE_ACCESS_KEY_ID`, `R2_PRIVATE_SECRET_ACCESS_KEY`; variables `R2_PUBLIC_BUCKET`, `BASEMAP_URL`) |
| S5 | `cron-skew.yml` | How late GitHub starts scheduled runs (14 days, hourly at :17) |
| S17 | `spike-cli-handoff.yml` (dispatch) | Can a dispatched workflow run `dcl` from the newest green `ci` build on main? Uses `.github/actions/fetch-cli` (ADR-0007), which stays |
| S16 | ran once from `spike-pages.yml` (deleted) | `_headers` noindex on direct upload and the Pages: Edit-only token: both passed; results in `docs/spikes/2026-10-29-pages-headers.md` |
| S18 | the real `/map` page, `npm run lighthouse` in ci | Lighthouse mobile ≥ 90 with MapLibre on demand: 100/100; results in `docs/spikes/2026-10-29-map-lighthouse.md` |

`vtimezone/check_against_iana.py` re-checks the pinned block against Python's IANA database: `pip install icalendar` then `python3 -I spikes/vtimezone/check_against_iana.py spikes/vtimezone/america-new_york.vtimezone.ics`.

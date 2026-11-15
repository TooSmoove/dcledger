# Export

`dcl export` turns the reviewed ledger in `data/` into the files the site and its subscribers use. It runs the quality
gate first and writes nothing if the data fails it (SPEC AC8).

```sh
dotnet run --project src/DcLedger.Cli -- export --data data --status ingest-status.json --out out
```

With `--out out` from the repository root, the site build finds its data at `out/site-data/events.json` (ADR-0001).

## What it writes

| File | Contents |
|---|---|
| `site-data/events.json` | The site's only input (schema version 1, ADR-0004): jurisdictions with "last checked" and the stale flag, and every event with its provenance (each document's `url`, `retrieved_utc`, `sha256` and `retrieval`) and `location` (`{"lat", "lon"}` or null), and every local action (see below). Mirrored by `site/src/content.config.ts`. |
| `site-data/corrections.json` | The corrections log for `/methodology/` (issue #117): `schema_version` 1, `opened_on`, and every entry from `data/corrections/` oldest first, each with `id`, `reported_on`, `corrected_on`, `record_ids`, `summary` and `issue` (a number or null). Written even when the log is empty. Site data only: not served, not downloadable, and not part of "Stability" below. Mirrored by `site/src/lib/corrections.js`. |
| `public/radar.ics` | Every event in one calendar (SPEC AC3). |
| `public/radar/{jurisdiction}.ics` | One calendar per watched jurisdiction, even before it has events. A jurisdiction that isn't watched (issue #115) gets none. |
| `public/feed.xml` | RSS 2.0 (SPEC AC5): one item per event revision and per decision revision, newest first (see below). |
| `public/events.csv` | One row per event, with `location_lat` and `location_lon` (empty when not recorded); documents as space-separated lists in the record's order, ending with `document_retrieval` (`fetched` or `manual` for each). UTF-8 with BOM and a formula guard on text cells (SPEC AC7). Every column: `docs/data-dictionary.md`. |
| `public/events.parquet` | The CSV's rows with native types: `starts_utc` a UTC timestamp, `sequence` an integer, coordinates doubles, and `documents` one list of `{url, retrieved_utc, sha256, retrieval}` in the record's order. Written by DuckDB `COPY` (snappy); values unguarded. |
| `public/actions.csv`, `public/actions.parquet` | One row per local action (decision), same rules as the events' pair (#90). Columns: `docs/data-dictionary.md`. |

Files under `public/` are served as they are, at the same paths under the site's root. Existing files of the same name
are replaced; nothing is deleted, so use a fresh `--out` for a clean set.

**Calendars:** `DTSTART;TZID=America/New_York` with the pinned VTIMEZONE (2007+ rules) inserted before the first event;
the UID is the hash of jurisdiction, platform and meeting ID and never changes; `SEQUENCE` comes from the record; a
cancelled meeting gets `STATUS:CANCELLED` and "Cancelled: " in front of its title; a located one gets `GEO` (6 decimals). `DTSTAMP` is the newest retrieval
time of the event's documents, so an unchanged event keeps the same bytes on every publish. The description lists each
source with its retrieval time (Eastern) and SHA-256 (SPEC AC2). A source a person saved in a browser (`retrieval: manual`)
is followed by "Saved by a person in a browser (the source does not allow automated downloads)."; a fetched one by nothing (issue #113).

**Feed:** export keeps no history, so each event is one item for its current revision. The GUID is the calendar UID
plus `/` and `sequence` (`isPermaLink="false"`); every published change raises `sequence` (`SEQUENCE_NOT_RAISED`), so
a change, including a cancellation, reaches readers as a new item and an unchanged event keeps its GUID. Titles start
with "Cancelled: " or "Updated: " (sequence above 0); the description is escaped HTML with the time in Eastern, the
case number, the label and every source with its retrieval time and SHA-256 (SPEC AC2), with the same sentence as the
calendars after a source a person saved. `pubDate` is the newest
retrieval of the event's documents, like the calendars' `DTSTAMP`, and `lastBuildDate` the newest `pubDate`, so the
feed changes only when the ledger does. A cancellation sorts as new when the notice that confirms it is archived and
added to `document_ids`. Links are absolute: `--site-url` (default `https://ncdcledger.org`; publish passes its
`SITE_URL`). Each decision is an item too (#90): "Decision: Moratorium adopted (Guilford County)", GUID
`action:{id}/{sequence}`, linked to its meeting page (or its jurisdiction's page without one), with the outcome, the
vote line as the site shows it, the label and every source; "Decision updated: " once `sequence` is above 0. Written with `XmlWriter`, UTF-8 without BOM, LF.

**Same input, same bytes:** every file depends only on the ledger, the ingest status and `--site-url`. JSON and the feed
are LF, calendars and CSV are CRLF, all UTF-8; Parquet is byte-identical across runs of the same DuckDB version; nothing depends on the machine's culture or time zone. Tests check this under several cultures.

**Actions** (issue #25): `events.json` lists every local action, ordered by decision date then ID, after the events.
The key was added without bumping `schema_version`, because a reader that ignores it loses nothing (ADR-0004):

```json
{
  "id": "fixture-county-moratorium-2026-11-05",
  "jurisdiction_id": "fixture-county",
  "event_id": "fixture-county-boc-2026-11-05",
  "action_type": "moratorium",
  "outcome": "adopted_amended",
  "decided_on": "2026-11-05",
  "votes": "8-1",
  "unanimous": false,
  "sequence": 0,
  "label": "confirmed_official",
  "documents": [{ "url": "…", "retrieved_utc": "…", "sha256": "…", "retrieval": "fetched" }]
}
```

`event_id` and `votes` may be null. `unanimous` is always a boolean: true when the vote was reported unanimous, which
can be before `votes` is known (added Nov 6, 2026, still schema version 1; the site treats a missing key as false). `decided_on` is a date, not a time, so it carries no offset. Each document's `retrieval` is `fetched` or `manual`
(added Nov 15, 2026 for events and actions alike, still schema version 1; the site treats a missing key as `fetched`). The calendars and the
CSV list meetings only; a decision shows on the site, on its meeting's page and its jurisdiction's page.

## Stability

Once someone subscribes to a calendar, bookmarks a download or cites an item, changing its address or its shape
breaks them. This section is the promise, from v0.1.0 on (issue #116). `CHANGELOG.md` records every change to it.

**These don't change:**

- **Record IDs:** event, decision (local action) and jurisdiction IDs, and document IDs (the SHA-256 of the bytes).
- **Page addresses:** `/radar/`, `/map/`, `/about/`, `/methodology/`, `/items/{event id}/`,
  `/jurisdictions/{jurisdiction id}/`, the `#decision-{decision id}` anchor on a jurisdiction page, and the
  `#correction-{correction id}` anchor on the methodology page.
- **Calendar and feed identity:** each event's calendar UID (a hash of jurisdiction, platform and platform meeting
  ID) and each feed GUID (`{UID}/{sequence}` for an event revision, `action:{id}/{sequence}` for a decision
  revision).
- **File paths:** `/radar.ics`, `/radar/{jurisdiction id}.ics`, `/feed.xml`, `/events.csv`, `/events.parquet`,
  `/actions.csv` and `/actions.parquet`.
- **Columns:** the name, order, type and meaning of every column in the CSV and Parquet files
  (`docs/data-dictionary.md`), and the keys and types of `events.json` within its `schema_version`.
- **Vocabularies:** a published value keeps its meaning (`docs/data-dictionary.md`, "Vocabularies").

**These can happen at any time, because they don't break a reader:**

- New columns, added at the end; new keys in `events.json`; new files, pages and feeds.
- New values in a vocabulary. Readers should treat a value they don't know as "other", not as an error.
- New records, and corrections to a record's values. A published correction raises the record's `sequence`, so
  calendars and feed readers see it.

**A breaking change** renames, removes, reorders or retypes anything in the first list. It is announced in
`CHANGELOG.md` at least **90 days** before it ships, saying what changes, when, and what to use instead.
`events.json` also bumps `schema_version` (ADR-0004). A record is never renamed. If one has to be removed (for
example, a duplicate entered by mistake), the CHANGELOG lists it.

**Held by tests:** the frozen public paths (`PublicContractTests` in .NET and the site's `dist` tests), the data
dictionary against the exporters' headers and Parquet columns, and the dictionary's vocabularies against the code's
tokens. A change that breaks the promise fails CI until the test, the dictionary and the CHANGELOG all say so.

## The ingest status (`--status`)

Publish builds `ingest-status.json` from ingest run history with `tools/ingest_status.py` ([publish.md](publish.md)), so export never calls GitHub (ADR-0004):

```json
{
  "schema_version": 1,
  "generated_utc": "2026-10-29T14:47:00Z",
  "jurisdictions": [
    { "id": "guilford", "last_checked_utc": "2026-10-29T12:17:43Z" }
  ]
}
```

- `generated_utc` is "now" for the export: it becomes events.json's `generated_utc`, and the stale flag is measured
  against it.
- `last_checked_utc` is when the newest successful ingest run checked the jurisdiction, or `null` when there is none.
- Times are UTC with a `Z`. Unknown or missing fields, duplicate IDs and other schema versions are errors (exit code 2).
- An automated jurisdiction that isn't listed counts as never checked. An entry for a jurisdiction that isn't an
  automated one in the ledger is ignored with a warning, so an old entry can't stop publishing.
- Manual jurisdictions take "last checked" from `last_manual_check_utc` in their YAML (SPEC AC10).
- A jurisdiction that isn't watched (`coverage_tier` `decisions` or `registered`, issue #115) has no "last checked"
  and is never stale: it exports `last_checked_utc: null` and `stale: false`, and a status entry for it is ignored
  with the warning above.

A jurisdiction is **stale** when it has no successful check, or the last one is more than 48 hours before
`generated_utc` (exactly 48 hours is still fresh). For a manual jurisdiction the threshold is 7 days (issue #27), and a
`last_manual_check_utc` more than an hour after `generated_utc` counts as stale, with a warning. `events.json` states
each jurisdiction's threshold as `stale_after_hours` (48 or 168), so the site's flag says which applies.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Files written |
| 1 | The ledger failed the quality gate; nothing written |
| 2 | Bad arguments, or the ingest status is missing or malformed |
| 4 | A bug |

## In ci

The `ledger data` job exports `data/` on every pull request and push and attaches the files as the `dcledger-data`
artifact (WS-AC10). ci has no ingest history, so it passes a status with no jurisdictions and the commit's time; every
automated jurisdiction shows as never checked there. The `package` job also runs export with the self-contained build,
which checks that DuckDB's native library loads.

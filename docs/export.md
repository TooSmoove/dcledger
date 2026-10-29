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
| `site-data/events.json` | The site's only input (schema version 1, ADR-0004): jurisdictions with "last checked" and the stale flag, and every event with its provenance. Mirrored by `site/src/content.config.ts`. |
| `public/radar.ics` | Every event in one calendar (SPEC AC3). |
| `public/radar/{jurisdiction}.ics` | One calendar per jurisdiction, even before it has events. |
| `public/events.csv` | One row per event; documents as space-separated lists in the record's order. BOM, formula guard and data dictionary come in #31. |

Files under `public/` are served as they are, at the same paths under the site's root. Existing files of the same name
are replaced; nothing is deleted, so use a fresh `--out` for a clean set.

**Calendars:** `DTSTART;TZID=America/New_York` with the pinned VTIMEZONE (2007+ rules) inserted before the first event;
the UID is the hash of jurisdiction, platform and meeting ID and never changes; `SEQUENCE` comes from the record; a
cancelled meeting gets `STATUS:CANCELLED` and "Cancelled: " in front of its title. `DTSTAMP` is the newest retrieval
time of the event's documents, so an unchanged event keeps the same bytes on every publish. The description lists each
source with its retrieval time (Eastern) and SHA-256 (SPEC AC2).

**Same input, same bytes:** every file depends only on the ledger and the ingest status. JSON is LF, calendars and CSV
are CRLF, all UTF-8; nothing depends on the machine's culture or time zone. Tests check this under several cultures.

## The ingest status (`--status`)

Publish (#23) builds `ingest-status.json` from ingest run history, so export never calls GitHub (ADR-0004):

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

A jurisdiction is **stale** when it has no successful check, or the last one is more than 48 hours before
`generated_utc` (exactly 48 hours is still fresh).

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

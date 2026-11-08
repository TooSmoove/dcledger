# Data dictionary

What's in the downloads: `events.csv` and `events.parquet`, served at the site's root next to the calendars and the
feed (`docs/export.md`). Both have one row per reviewed event, ordered by start time, then ID. Everything in them is
public by design: neutral titles written by a reviewer, case numbers, meeting metadata, source URLs, retrieval times
and SHA-256 hashes. No private individual's name appears (SPEC §E).

## `events.csv`

Made for spreadsheets (SPEC AC7):

- **Encoding:** UTF-8 with a byte-order mark (BOM). Without it, Excel guesses the file is in the system code page and
  garbles characters like "é".
- **Records:** RFC 4180. Commas separate fields, CRLF ends records, and a field containing a comma, a double quote, CR
  or LF is enclosed in double quotes, with inner double quotes doubled. The first record is the header.
- **Empty cells:** an empty cell means the value isn't recorded (see "Empty when" below).
- **Formula guard:** a text cell that starts with `=`, `+`, `-`, `@`, a tab or a carriage return gets a leading
  apostrophe (`'`), so a spreadsheet shows it as text instead of running it as a formula ("CSV injection"). A text
  cell that already starts with an apostrophe gets one too.
  - **To recover the original value,** remove one leading apostrophe from any text cell that starts with one. Only
    text columns are guarded; the number columns (`sequence`, `location_lat`, `location_lon`) never are, so a
    longitude like `-79.792` stays a number.
  - The apostrophe is part of the cell's value in the file; a spreadsheet may show it.
  - The guard applies to every text column, even ones whose values can't start with a trigger today, so the rule has
    no exceptions to remember.

| Column | Type | Empty when | Meaning | Example |
|---|---|---|---|---|
| `id` | text | never | The event's permanent ID: jurisdiction, body and date. Never changes once published. | `guilford-board-of-commissioners-2026-11-05` |
| `uid` | text | never | The calendar UID, a hash of jurisdiction, platform and platform meeting ID. The same in `radar.ics` and the feed. | `7c58…eb4c@dcledger` |
| `jurisdiction_id` | text | never | The jurisdiction. | `guilford` |
| `body` | text | never | The meeting body, as configured. | `Board of Commissioners` |
| `title` | text | never | A neutral title written by the reviewer (never the platform's original title). | `Public hearing on a proposed data center moratorium` |
| `case_number` | text | not recorded | The platform's case or file number. | `2026-452` |
| `starts_utc` | text, ISO 8601 UTC | never | The start time in UTC. | `2026-11-05T22:30:00Z` |
| `starts_local` | text, ISO 8601 with offset | never | The start time in Eastern, with the offset in effect that day (EDT `-04:00` or EST `-05:00`). | `2026-11-05T17:30:00-05:00` |
| `status` | text | never | `scheduled` or `cancelled`. | `scheduled` |
| `sequence` | integer | never | The revision number. It goes up by at least 1 with every published change, and calendar apps apply the change only when it does. | `0` |
| `label` | text | never | `reported_news` ("Reported (news)") or `confirmed_official` ("Confirmed (official record)"). Confirmed requires an official document. | `confirmed_official` |
| `location_lat` | number | not recorded | Latitude of the meeting place, WGS 84. | `36.0726` |
| `location_lon` | number | not recorded | Longitude of the meeting place, WGS 84. | `-79.792` |
| `document_urls` | text, space-separated | never | Each source's URL, in the record's order. URLs contain no spaces. | `https://… https://…` |
| `document_retrieved_utc` | text, space-separated | never | When each source was retrieved, ISO 8601 UTC, in the same order. | `2026-10-28T12:47:03Z` |
| `document_sha256` | text, space-separated | never | Each source's SHA-256, lowercase hex, in the same order. A copy of the source with this hash is kept privately. | `98e5…5ea9` |

The three `document_*` columns line up: the second URL was retrieved at the second time and has the second hash.

## `events.parquet`

The same rows as the CSV for analysis tools (DuckDB, pandas, R, Arrow). Values are stored as they are: **no formula
guard and no BOM**. Snappy compression. The columns are the CSV's, with native types:

| Column | Parquet type (as DuckDB reads it) | Difference from the CSV |
|---|---|---|
| `id`, `uid`, `jurisdiction_id`, `body`, `title`, `case_number`, `starts_local`, `status`, `label` | `VARCHAR` | Not guarded; `case_number` is null when not recorded. |
| `starts_utc` | `TIMESTAMP` | An instant in UTC, stored without a time zone. |
| `sequence` | `INTEGER` | |
| `location_lat`, `location_lon` | `DOUBLE` | Null when not recorded. |
| `documents` | `STRUCT(url VARCHAR, retrieved_utc TIMESTAMP, sha256 VARCHAR)[]` | One list instead of three joined columns, in the record's order. |

```sql
-- DuckDB: one row per source
SELECT id, unnest(documents, recursive := true) FROM 'events.parquet';
```

## Changes

New columns are added at the end. Existing columns aren't renamed, retyped or removed without a note here and in the
SPEC's amendments log (`docs/SPEC.md` §J). Recorded decisions get their own files (`actions.csv`, `actions.parquet`;
#90), documented here when they land.

# Data dictionary

What's in the downloads, served at the site's root next to the calendars and the feed (`docs/export.md`):
`events.csv` and `events.parquet` have one row per reviewed event, ordered by start time, then ID; `actions.csv` and
`actions.parquet` one row per recorded decision. Everything in them is
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
| `document_retrieval` | text, space-separated | never | How each source's copy was obtained, in the same order: `fetched` (our program downloaded it from the URL) or `manual` (a person saved it from the URL in a browser, because the source does not allow automated downloads). Either way the hash is of the bytes kept. | `fetched manual` |

The four `document_*` columns line up: the second URL was retrieved at the second time, has the second hash and was
obtained the second way.

## `events.parquet`

The same rows as the CSV for analysis tools (DuckDB, pandas, R, Arrow). Values are stored as they are: **no formula
guard and no BOM**. Snappy compression. The columns are the CSV's, with native types:

| Column | Parquet type (as DuckDB reads it) | Difference from the CSV |
|---|---|---|
| `id`, `uid`, `jurisdiction_id`, `body`, `title`, `case_number`, `starts_local`, `status`, `label` | `VARCHAR` | Not guarded; `case_number` is null when not recorded. |
| `starts_utc` | `TIMESTAMP` | An instant in UTC, stored without a time zone. |
| `sequence` | `INTEGER` | |
| `location_lat`, `location_lon` | `DOUBLE` | Null when not recorded. |
| `documents` | `STRUCT(url VARCHAR, retrieved_utc TIMESTAMP, sha256 VARCHAR, retrieval VARCHAR)[]` | One list instead of four joined columns, in the record's order. `retrieval` is `fetched` or `manual`. |

```sql
-- DuckDB: one row per source
SELECT id, unnest(documents, recursive := true) FROM 'events.parquet';
```

## `actions.csv` and `actions.parquet`

One row per recorded decision (a local action: a vote on an item, including continuances and tablings), ordered by
decision date, then ID. The CSV follows every rule of `events.csv` above: BOM, RFC 4180, CRLF, and the formula guard
on text columns (`unanimous` and `sequence` aren't text). The Parquet file stores the values as they are, like
`events.parquet`.

| Column | CSV | Parquet | Empty when | Meaning | Example |
|---|---|---|---|---|---|
| `id` | text | `VARCHAR` | never | The decision's permanent ID. | `guilford-moratorium-2026-11-05` |
| `jurisdiction_id` | text | `VARCHAR` | never | The jurisdiction that decided. | `guilford` |
| `event_id` | text | `VARCHAR` | the meeting isn't in the ledger | The meeting where it was decided; matches `events.csv`'s `id`. | `guilford-board-of-commissioners-2026-11-05` |
| `action_type` | text | `VARCHAR` | never | `moratorium`, `rezoning`, `text_amendment` or `resolution`. | `moratorium` |
| `outcome` | text | `VARCHAR` | never | `adopted`, `adopted_amended`, `failed`, `continued` or `tabled`, as the platform records it. | `adopted` |
| `decided_on` | text, `YYYY-MM-DD` | `DATE` | never | The date of the decision (Eastern). | `2026-11-05` |
| `votes` | text | `VARCHAR` | no tally recorded | Ayes-noes as recorded, without abstentions or absences. Never inferred from the board's size. | `9-0` |
| `unanimous` | `true` / `false` | `BOOLEAN` | never | Reported unanimous; can be `true` while `votes` is still empty. | `true` |
| `sequence` | integer | `INTEGER` | never | The revision number; it goes up with every published change, and the feed shows each revision as a new item. | `0` |
| `label` | text | `VARCHAR` | never | `reported_news` or `confirmed_official`, as for events. | `reported_news` |
| `document_urls`, `document_retrieved_utc`, `document_sha256`, `document_retrieval` | text, space-separated | `documents`: `STRUCT(url VARCHAR, retrieved_utc TIMESTAMP, sha256 VARCHAR, retrieval VARCHAR)[]` | never | The sources, as for events, including how each copy was obtained (`fetched` or `manual`). | |
| `instrument_number` | text | `VARCHAR` | not stated in an archived document | The ordinance or resolution number, as the body numbers it. | `2026-11` |
| `threshold` | text | `VARCHAR` | not stated | What triggers the decision, copied as written. | `electrical power demand exceeding 10 megawatts` |
| `scope` | text | `VARCHAR` | not stated | What approvals or area it covers, copied as written. | `temporary development moratorium applicable to data center facilities` |
| `effective_on` | text, `YYYY-MM-DD` | `DATE` | not stated | The start date. | `2026-11-05` |
| `expires_on` | text, `YYYY-MM-DD` | `DATE` | not stated | The outside expiry date. Never before `effective_on`. | `2027-02-14` |
| `early_termination` | text | `VARCHAR` | not stated | What ends it sooner, copied as written. | `upon adoption of text amendments` |
| `exemptions` | text | `VARCHAR` | not stated | What is exempt, copied as written. | |
| `successor_id` | text | `VARCHAR` | not replaced or extended | The `id` of the decision that replaced or extended it: same jurisdiction, decided the same day or later. | `greensboro-moratorium-2027-02-02` |
| `status` | text | `VARCHAR` | not recorded | `in_effect`, `expired`, `superseded` (then `successor_id` is set) or `repealed`, set by a reviewer from an archived document. Nothing changes it automatically: check the dates. | `in_effect` |

The last nine columns are a decision's terms (issue #115). They are filled only for an adopted decision
(`adopted` or `adopted_amended`), and only as an archived document states them: text terms are copied as written,
never summarized, on one line of at most 500 characters. A term taken from a news report stays under the decision's
`reported_news` label until the official text is archived.

## Vocabularies

These columns take only the values listed. A value keeps its meaning once published; new values may be added, so
treat one you don't know as "other" rather than an error (`docs/export.md`, "Stability").

| Column | File | Values |
|---|---|---|
| `status` | `events.csv`, `events.parquet` | `scheduled`, `cancelled` |
| `label` | events and actions | `reported_news`, `confirmed_official` |
| `document_retrieval` (`retrieval` in Parquet) | events and actions | `fetched`, `manual` |
| `action_type` | actions | `moratorium`, `rezoning`, `text_amendment`, `resolution` |
| `outcome` | actions | `adopted`, `adopted_amended`, `failed`, `continued`, `tabled` |
| `status` | `actions.csv`, `actions.parquet` | `in_effect`, `expired`, `superseded`, `repealed` |
| `coverage_tier` | a jurisdiction (on the site and in `events.json`) | `watched`, `decisions`, `registered` |

## Changes

New columns are added at the end. Existing columns aren't renamed, retyped or removed without a note here and in the
SPEC's amendments log (`docs/SPEC.md` §J).

- **Nov 15, 2026 (issue #113):** `document_retrieval` added as the last column of `events.csv` and `actions.csv`. In
  both Parquet files the `documents` struct gained `retrieval` as its last field; `url`, `retrieved_utc` and `sha256`
  keep their names, types and positions, so a query that names them is unaffected.
- **Nov 15, 2026 (issue #115):** `actions.csv` gained nine columns after `document_retrieval`: `instrument_number`,
  `threshold`, `scope`, `effective_on`, `expires_on`, `early_termination`, `exemptions`, `successor_id` and
  `status`. `actions.parquet` gained the same columns after `documents`, with `effective_on` and `expires_on` as
  `DATE`. No existing column moved.

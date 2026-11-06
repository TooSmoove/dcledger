# Ledger data

The reviewed, canonical records (ADR-0002). One YAML file per record; the file name is the record's `id`. Merging a PR here is the publication decision.

```
jurisdictions/<id>.yaml   documents/<sha256>.yaml   events/<id>.yaml   actions/<id>.yaml
```

Check your changes before opening a PR:

```sh
dotnet run --project src/DcLedger.Cli -- validate --data data
```

## Rules the quality gate enforces

- Only the keys shown below. Names of private people and original source titles are **never** stored (`FORBIDDEN_FIELD`).
- IDs are lowercase letters, digits and single hyphens, match the file name (`ID_MISMATCH`), and are **permanent** once merged (`SLUG_CHANGED`). To retire a meeting, set `status: cancelled`.
- Every event and action lists at least one document, and every listed document exists (`DOC_MISSING`); its jurisdiction exists (`FK_MISSING`).
- An action's `event_id`, when given, names an event in the same jurisdiction on the same Eastern date as `decided_on` (`FK_MISSING`, `EVENT_MISMATCH`). `action_type` and `outcome` take only the tokens below, and `votes` is ayes-noes like `"9-0"` (`INVALID_VALUE`).
- `confirmed_official` needs at least one document with `source_type: official` (`LABEL_INVALID`).
- `starts_at` carries the Eastern offset in effect that day (`-04:00` or `-05:00`); times skipped or repeated by daylight saving are rejected.
- `location` is optional: the proposed project's site, as decimal degrees with at most 6 decimals, inside North
  Carolina (`LOCATION_OUT_OF_RANGE` otherwise; usually a swapped pair or a missing minus sign). Only `lat` and `lon`:
  never a name, address or owner. It puts the item on the map.
- Files are UTF-8, at most 64 KB, one YAML mapping, no anchors or aliases. Quote values that start with a special character or look like numbers you want kept as text.

## Shapes

```yaml
# jurisdictions/guilford.yaml
id: guilford
name: Guilford County
platform: legistar            # legistar | manual
legistar_client: guilford     # legistar only
bodies: [Board of Commissioners]
last_manual_check_utc: 2026-10-27T14:00:00Z   # manual only, optional

# documents/<sha256>.yaml — metadata only; the bytes stay in private storage
id: <64 lowercase hex characters: SHA-256 of the retrieved bytes>
url: https://guilford.legistar.com/...
retrieved_at: 2026-10-27T14:00:00Z
media_type: application/pdf
bytes: 123456
source_type: official         # official | news

# events/<id>.yaml  (example values)
id: fixture-county-boc-2026-11-05
jurisdiction_id: fixture-county
platform: legistar            # legistar | manual
platform_meeting_id: "1599"   # Legistar EventId, quoted
body: Board of Commissioners
title: Reviewer-written neutral title
case_number: Z-00-00          # optional
starts_at: 2026-11-05T17:30:00-05:00
status: scheduled             # scheduled | cancelled
sequence: 0                   # +1 on every published change
label: confirmed_official     # confirmed_official | reported_news
document_ids: [<sha256>]
first_seen_utc: 2026-10-27T14:00:00Z   # set by ingest, optional
location:                     # optional: the proposed project's site
  lat: 36.0726
  lon: -79.792

# actions/<id>.yaml  (example values)
id: fixture-county-moratorium-2026-01-15
jurisdiction_id: fixture-county
event_id: fixture-county-boc-2026-01-15   # optional: the meeting where it was decided
action_type: moratorium       # moratorium | rezoning | text_amendment | resolution
outcome: adopted              # adopted | adopted_amended | failed | continued | tabled
decided_on: 2026-01-15        # the meeting's date in Eastern time
votes: "5-2"                  # optional: ayes-noes, quoted
unanimous: true               # optional: true or left out; reported unanimous, tally or not
label: confirmed_official
document_ids: [<sha256>]
```

## Recording a decision

A decision is an `actions/` record. It shows on its meeting's item page (through `event_id`) and on its jurisdiction's
page. Record every vote on the item, including a continuance or a tabling (`continued`, `tabled`), as its own action.

1. **Archive the source.** Run the **archive-document** workflow (Actions tab → archive-document → Run workflow) with
   the page's https URL and who published it. For a Legistar decision, use the meeting item's action-detail page
   (`official`). It keeps the bytes in the private bucket and prints the `documents/` record in the run summary.
   Locally, `dcl archive-document --url <url> --source-type official --dry-run` prints the same record without storing
   anything.
2. **Label it mechanically.** `confirmed_official` once the platform's own record of the action (the action-detail page)
   is archived; until then, `reported_news` backed by an archived news story. When the official record arrives, add its
   document and change the label in a new PR; the action's ID stays the same.
3. **Take the outcome and vote from the record, not from coverage.** `outcome` is the closest token to what the record
   says; `votes` is ayes-noes as recorded, without abstentions or absences. When the source says the vote was unanimous
   but gives no count, set `unanimous: true` and leave `votes` out; the site shows "Unanimous (tally not yet recorded)".
   Don't infer a count from the size of the board: a member may have been absent. With a tally, `unanimous: true` needs
   no noes (`INVALID_VALUE`). Leave `votes` out for a voice vote with no
   tally.
4. **Check and open the PR:** `dotnet run --project src/DcLedger.Cli -- validate --data data`. The gate checks that
   `event_id` exists, is in the same jurisdiction and falls on `decided_on` (`FK_MISSING`, `EVENT_MISMATCH`).

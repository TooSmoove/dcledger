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
- A published event that changes (`body`, `title`, `case_number`, `starts_at`, `status`, `label`, `document_ids` or `location`) raises `sequence` by at least 1, and `sequence` never goes down (`SEQUENCE_NOT_RAISED`). `jurisdiction_id`, `platform` and `platform_meeting_id` never change once published, because the calendar UID is built from them (`UID_CHANGED`). CI compares against the base branch with `--previous-data`.
- Actions follow the same rule: a published action that changes (`event_id`, `action_type`, `outcome`, `decided_on`, `votes`, `unanimous`, `label` or `document_ids`) raises its `sequence` (optional, default 0), and `jurisdiction_id` never changes (`JURISDICTION_CHANGED`). The feed gives each revision of a decision its own item.
- An event's `platform` is its jurisdiction's (`PLATFORM_MISMATCH`). `last_manual_check_utc` appears only on a `manual` jurisdiction (`INVALID_VALUE`); export shows one dated after the export as stale and warns.
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
retrieval: manual             # optional: only for a file a person saved (see below); left out means fetched

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
sequence: 0                   # optional, default 0: +1 on every published change
label: confirmed_official
document_ids: [<sha256>]
```

## Cancelling or rescheduling a meeting

Ingest comments on a meeting's review issue when the platform moves it, hints at a cancellation, withdraws its agenda or
stops listing it (`docs/ingest.md`). It never changes the ledger itself. Once the meeting page confirms the change:

- **Cancelled:** set `status: cancelled` and raise `sequence` by 1. Archive the notice that confirms it and add it to
  `document_ids`: that's the provenance, and it dates the feed item and calendar update to when you found it. Keep the file, `id` and `platform_meeting_id`: the
  calendar UID is built from the jurisdiction, platform and meeting ID, so subscribers' calendars mark the existing event
  cancelled instead of dropping it, and /radar shows "Cancelled".
- **Rescheduled:** set the new `starts_at`, with the Eastern offset in effect on the new date, and raise `sequence` by 1.
- **Back on after a cancellation:** set `status: scheduled` and raise `sequence` by 1.

Calendar apps ignore an update whose `sequence` didn't go up, so every published change to an event raises it; the gate
checks this against the base branch (`SEQUENCE_NOT_RAISED`, `UID_CHANGED`). Locally:

```sh
git worktree add /tmp/dcl-base origin/main
dotnet run --project src/DcLedger.Cli -- validate --data data --previous-data /tmp/dcl-base/data
```

## Recording a decision

A decision is an `actions/` record. It shows on its meeting's item page (through `event_id`) and on its jurisdiction's
page. Record every vote on the item, including a continuance or a tabling (`continued`, `tabled`), as its own action.

1. **Archive the source.** Run the **archive-document** workflow (Actions tab → archive-document → Run workflow) with
   the page's https URL and who published it. For a Legistar decision, use the meeting item's action-detail page
   (`official`). It keeps the bytes in the private bucket and prints the `documents/` record in the run summary.
   Locally, `dcl archive-document --url <url> --source-type official --dry-run` prints the same record without storing
   anything. If the source answers HTTP 403 to programs, see the next section.
2. **Label it mechanically.** `confirmed_official` once the platform's own record of the action (the action-detail page)
   is archived; until then, `reported_news` backed by an archived news story. When the official record arrives, add its
   document, change the label and raise `sequence` by 1 in a new PR; the action's ID stays the same.
3. **Take the outcome and vote from the record, not from coverage.** `outcome` is the closest token to what the record
   says; `votes` is ayes-noes as recorded, without abstentions or absences. When the source says the vote was unanimous
   but gives no count, set `unanimous: true` and leave `votes` out; the site shows "Unanimous (tally not yet recorded)".
   Don't infer a count from the size of the board: a member may have been absent. With a tally, `unanimous: true` needs
   no noes (`INVALID_VALUE`). Leave `votes` out for a voice vote with no
   tally.
4. **Check and open the PR:** `dotnet run --project src/DcLedger.Cli -- validate --data data`. The gate checks that
   `event_id` exists, is in the same jurisdiction and falls on `decided_on` (`FK_MISSING`, `EVENT_MISMATCH`).

## Entering a manual jurisdiction

Some jurisdictions publish agendas on a platform without an adapter. Greensboro is on eSCRIBE, and #94's Stokes and
Vance post PDF lists. Their meetings are entered by hand, until #96 automates the check.

1. **The jurisdiction:** `platform: manual`, with `bodies` listing the bodies you watch (documentation only) and no
   `legistar_client`. Ingest skips it.
2. **Check, then record the check.** Open the jurisdiction's meeting calendar and read the agendas for the next 30
   days, looking for data-center items: data centers, data processing, moratoriums, zoning text amendments,
   rezonings of large industrial sites, and economic development agreements. Then set `last_manual_check_utc` to the
   time you finished, in UTC with a `Z` (for example `2026-11-08T23:00:00Z`), and commit it with your sign-off, even
   when you found nothing. That timestamp is a statement that someone looked, so never set it ahead of time. The site
   shows it as "last checked" and flags the jurisdiction stale after **7 days** without a check (48 hours for
   automated ones; SPEC AC10).
3. **Each item becomes an event.** Archive the agenda or notice with **archive-document** (`official`; for eSCRIBE,
   which blocks programs, save it in a browser and use `--file`, see "Archiving a file saved in a browser"), then add
   `events/<id>.yaml` as usual, with:
   - `platform: manual`;
   - `platform_meeting_id`: the platform's own ID for the meeting (for eSCRIBE, the `Id=` value in the meeting page's
     URL), quoted. The calendar UID is built from it, so it never changes once published;
   - `first_seen_utc`: when you found the agenda, which serves as the start of the review clock (AC9);
   - a neutral title of your own, and `location` only for a publicly known project site.
4. **Decisions** are `actions/` records like any other (see "Recording a decision"). Without an event in the ledger,
   leave out `event_id`; the decision then shows on the jurisdiction's page and in the feed.

Where each manual jurisdiction posts (#94):

| Jurisdiction | Where to look | `platform_meeting_id` |
|---|---|---|
| Greensboro | eSCRIBE, `pub-greensboro-nc.escribemeetings.com` (save documents in a browser: `--file`) | the meeting page's `Id=` value |
| Stokes | the county website's yearly Board of Commissioners agenda pages; PDFs on `cms9files.revize.com/stokescountync`, which the archive workflow can fetch (checked Nov 14, 2026) | no platform ID: the meeting's date and kind, e.g. `"2026-12-07 regular"` |
| Vance | `vancecounty.org/departments/board-of-commissioners`, "MASTER-File" PDF packets. Its robots.txt disallows all robots, so the workflow refuses its URLs: save in a browser and use `--file` | same as Stokes |

Whatever you choose for a platform without IDs, keep it stable: it can't change once published.

## Archiving a file saved in a browser

Some sources only serve browsers. Greensboro's eSCRIBE portal puts every document download behind a Cloudflare
"Verifying your browser" check, so the archive-document workflow gets HTTP 403. We don't try to get past that check.
Instead, a person saves the public file, and `--file` records that a person saved it (issue #101).

1. **Save the file.** Open the document in a browser (a private window proves it's public) and save it, e.g. the
   minutes PDF. Note its URL from the address bar, such as
   `https://pub-greensboro-nc.escribemeetings.com/FileStream.ashx?DocumentId=75288`. Don't edit the file, and archive it
   soon: its last-modified time becomes `retrieved_at`.
2. **Archive it from your computer.** The workflow can't take a file, and raw documents must never be committed. Set the
   four bucket variables (`R2_ACCOUNT_ID`, `R2_PRIVATE_BUCKET`, `R2_PRIVATE_ACCESS_KEY_ID`, `R2_PRIVATE_SECRET_ACCESS_KEY`;
   the same values as the repository secrets) and run:

   ```sh
   dotnet run --project src/DcLedger.Cli -- archive-document --file "minutes.pdf" --url "<the URL>" --source-type official
   ```

   Add `--dry-run` first to check the record without storing anything (no variables needed). On Windows, set the
   variables with `set R2_ACCOUNT_ID=...` in Command Prompt or `$env:R2_ACCOUNT_ID="..."` in PowerShell.
3. **Add the printed record.** It is the usual `documents/` record plus `retrieval: manual`. The command also prints the
   `retrieved_at` it used. If that isn't when you saved the file (say the file was copied or edited), save it again.
4. **Label as usual.** The publisher is what decides the label, not who saved the file: a manual copy of an official
   record (`source_type: official`) can back `confirmed_official`.

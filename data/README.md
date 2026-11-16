# Ledger data

The reviewed, canonical records (ADR-0002). One YAML file per record; the file name is the record's `id`. Merging a PR here is the publication decision.

```
jurisdictions/<id>.yaml   documents/<sha256>.yaml   events/<id>.yaml   actions/<id>.yaml
thumbnails/<sha256>.webp  (optional: a reviewed picture of a document's first page)
```

Check your changes before opening a PR:

```sh
dotnet run --project src/DcLedger.Cli -- validate --data data
```

## Rules the quality gate enforces

- Only the keys shown below. Names of private people and original source titles are **never** stored (`FORBIDDEN_FIELD`).
- IDs are lowercase letters, digits and single hyphens, match the file name (`ID_MISMATCH`), and are **permanent** once merged (`SLUG_CHANGED`). To retire a meeting, set `status: cancelled`.
- A published event that changes (`body`, `title`, `case_number`, `starts_at`, `status`, `label`, `document_ids` or `location`) raises `sequence` by at least 1, and `sequence` never goes down (`SEQUENCE_NOT_RAISED`). `jurisdiction_id`, `platform` and `platform_meeting_id` never change once published, because the calendar UID is built from them (`UID_CHANGED`). CI compares against the base branch with `--previous-data`.
- Actions follow the same rule: a published action that changes (`event_id`, `action_type`, `outcome`, `decided_on`, `votes`, `unanimous`, `label`, `document_ids` or any of its terms) raises its `sequence` (optional, default 0), and `jurisdiction_id` never changes (`JURISDICTION_CHANGED`). The feed gives each revision of a decision its own item.
- Every jurisdiction has a `coverage_tier` (`REQUIRED_FIELD`). Only a `watched` one has events, and a `registered` one has no actions either (`TIER_MISMATCH`).
- A decision's terms (see "Recording a decision's terms") appear only on an `adopted` or `adopted_amended` action; `expires_on` is not before `effective_on`; `status: superseded` needs `successor_id`, which names another action in the same jurisdiction decided the same day or later (`TERMS_INVALID`; a missing one is `FK_MISSING`). Text terms are one line of at most 500 characters (`INVALID_VALUE`).
- An event's `platform` is its jurisdiction's (`PLATFORM_MISMATCH`). `last_manual_check_utc` appears only on a `manual` jurisdiction (`INVALID_VALUE`); export shows one dated after the export as stale and warns.
- Every event and action lists at least one document, and every listed document exists (`DOC_MISSING`); its jurisdiction exists (`FK_MISSING`).
- An action's `event_id`, when given, names an event in the same jurisdiction on the same Eastern date as `decided_on` (`FK_MISSING`, `EVENT_MISMATCH`). `action_type` and `outcome` take only the tokens below, and `votes` is ayes-noes like `"9-0"` (`INVALID_VALUE`).
- `confirmed_official` needs at least one document with `source_type: official` (`LABEL_INVALID`).
- `starts_at` carries the Eastern offset in effect that day (`-04:00` or `-05:00`); times skipped or repeated by daylight saving are rejected.
- `location` is optional: the proposed project's site, as decimal degrees with at most 6 decimals, inside North
  Carolina (`LOCATION_OUT_OF_RANGE` otherwise; usually a swapped pair or a missing minus sign). Only `lat` and `lon`:
  never a name, address or owner. It puts the item on the map.
- A corrections log entry (`corrections/<id>.yaml`, see "Logging a correction") names only jurisdictions, events or actions that exist (`FK_MISSING`), each once; is reported on or after Nov 22, 2026, when the log opened, and corrected on or after it was reported; and cites a report number of 1 or more (`CORRECTION_INVALID`). Its `summary` is one line of at most 500 characters (`INVALID_VALUE`). Entries are permanent like every record (`SLUG_CHANGED`).
- A document's first-page picture (`thumbnail`, see "Showing a document's first page") is allowed only on an
  `official` document of `kind` `minutes` or `legislation`; `data/thumbnails/<id>.webp` must exist with the
  `sha256` the record states, and every file there needs such a record (`THUMBNAIL_INVALID`). A picture nobody has
  marked `reviewed: true` fails (`THUMBNAIL_UNREVIEWED`). Pictures are WebP, at most 100 KB, 480 × 820 pixels.
- Files are UTF-8, at most 64 KB, one YAML mapping, no anchors or aliases. Quote values that start with a special character or look like numbers you want kept as text.

## Shapes

```yaml
# jurisdictions/guilford.yaml
id: guilford
name: Guilford County
platform: legistar            # legistar | manual
coverage_tier: watched        # watched | decisions | registered (see "Coverage tiers")
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
kind: minutes                 # optional: minutes | legislation | agenda | agenda_packet | notice | news_release | news_article | other
thumbnail:                    # optional: official minutes or legislation only (see "Showing a document's first page")
  sha256: <SHA-256 of data/thumbnails/<id>.webp>
  width: 240
  height: 311
  reviewed: true              # a person checked the picture for private individuals' names

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
# Terms (optional, adopted decisions only), each only as an archived document states it:
instrument_number: "26-01"    # the ordinance or resolution number
threshold: "electrical power demand exceeding 10 megawatts"   # as written
scope: "..."                  # as written
effective_on: 2026-01-15
expires_on: 2026-07-14
early_termination: "..."      # as written
exemptions: "..."             # as written
successor_id: fixture-county-moratorium-2026-07-13   # the action that replaced or extended it
status: in_effect             # in_effect | expired | superseded | repealed
```

## Coverage tiers

Every jurisdiction states how closely it is covered, and its page says so in a sentence.

- `watched`: upcoming meetings and hearings are on the radar, and decisions are recorded. Only these have events, a
  calendar, a "last checked" date and the stale flag (SPEC AC10). Legistar ones are ingested; manual ones are
  checked by hand ("Entering a manual jurisdiction").
- `decisions`: decisions are recorded after they are made, from minutes or coverage. No events, no calendar, no
  freshness promise, and ingest skips it whatever its platform.
- `registered`: listed only, with no events or decisions. To record a decision, change it to `decisions` first.

Changing a jurisdiction's tier changes what the site promises about it, so say why in the PR.

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

## Recording a decision's terms

An adopted decision, such as a moratorium, can carry its terms (issue #115). G.S. 160D-107 requires a moratorium
ordinance to state most of them, so they come from the adopted text without interpretation.

- **Only from an archived document.** Fill a term only when a document in `document_ids` states it. Don't work one
  out: an end date isn't the start date plus a period of days unless the document gives that date. Until the
  official text is archived, a term taken from a news report stays under `reported_news`.
- **As written.** `threshold`, `scope`, `early_termination` and `exemptions` are copied word for word, with no
  summarizing adjectives (SPEC §E). Copy the operative words when the passage is long: one line, at most 500
  characters, quoted. A quoted value may wrap across lines in the file; `|` and `>` blocks are rejected.
- **Status is yours to set.** Nothing changes `status` automatically. Set `in_effect` when a document shows it in
  force; `expired` once its end has passed with no extension; `repealed` when the body ended it early;
  `superseded` with `successor_id` when a later action replaced or extended it (record that action first).
- **Say where each term came from** in a comment at the top of the file, and raise `sequence` by 1.

## Entering a manual jurisdiction

Some jurisdictions publish agendas on a platform without an adapter. Greensboro is on eSCRIBE, Stokes is on Granicus
and Vance posts PDF packets (#94). Their meetings are entered by hand, until #96 automates the check.

1. **The jurisdiction:** `platform: manual`, `coverage_tier: watched`, with `bodies` listing the bodies you watch
   (documentation only) and no `legistar_client`. Ingest skips it.
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
| Stokes | Granicus, `stokescountync.granicus.com/ViewPublisher.php?view_id=1` (agendas, packets, minutes; the county website's yearly PDF pages list years through 2025). Its robots.txt disallows all robots (checked Nov 14, 2026), so the workflow refuses its URLs: save in a browser and use `--file` | no platform ID: the meeting's date and kind, e.g. `"2026-12-07 regular"` |
| Vance | `vancecounty.org/departments/board-of-commissioners`, "MASTER-File" PDF packets. Its robots.txt disallows all robots, so the workflow refuses its URLs: save in a browser and use `--file` | same as Stokes |

Decisions-only jurisdictions (`coverage_tier: decisions`, #118) have no events, so no `platform_meeting_id` and no
`last_manual_check_utc`. Look there only to record a decision:

| Jurisdiction | Where to look |
|---|---|
| Forsyth | CivicWeb, `coforsythnc.civicweb.net` (agendas under Meetings; adopted minutes in the Document Center under Minutes → Board of Commissioners Meeting). Its robots.txt disallows all robots (checked Nov 15, 2026): save in a browser and use `--file`. Monday briefings post no minutes |
| Davidson | AgendaSuite, `agendasuite.org/iip/davidson` (agendas, per-item voting records and approved minutes). It has no robots.txt (checked Nov 15, 2026), so the archive-document workflow can fetch it |

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
4. **What readers see.** The exports carry it (`document_retrieval` in the CSVs, `retrieval` in Parquet and
   `events.json`; `docs/data-dictionary.md`), and the site, the feed and the calendars say beside that source:
   "Saved by a person in a browser (the source does not allow automated downloads)." A fetched copy gets no note (issue #113).
5. **Label as usual.** The publisher is what decides the label, not who saved the file: a manual copy of an official
   record (`source_type: official`) can back `confirmed_official`.

## Showing a document's first page

Item and jurisdiction pages can show a small picture of page 1 beside a source (issue #132), rendered from the exact
copy whose SHA-256 the page prints. Only official **minutes** and **legislation** pages (a Legistar legislation or
action-detail page, an ordinance or resolution's adopted text) get one. Agendas, agenda packets, notices, news
releases and news stories never do: packets stay in the private bucket (SPEC §E), and news pages are other publishers'
work.

1. **Say what the document is.** Add `kind` to its `documents/` record. You can add `kind` to any document; it shows
   nothing by itself.
2. **Render page 1 from the archived bytes.** For a file you saved in a browser (and archived with `--file`), run
   `python3 tools/render_thumbnail.py --file <that file> --data data` (PDF needs `pdftoppm` from poppler-utils; HTML
   needs `--chrome <path to Chrome>`; both need `pip install Pillow==12.3.0`). For a fetched document, merge the
   `kind` first, then run the **thumbnail** workflow (Actions → thumbnail → Run workflow) with its SHA-256, and download
   its artifact into `data/thumbnails/`. Either way the tool refuses a file whose SHA-256 isn't a record's ID, and any
   document that isn't official minutes or legislation. It prints the `thumbnail:` block with `reviewed: false`.
3. **Check the picture for private individuals' names** (residents who spoke, applicants, property owners). Public
   officials acting in office are fine. If you find one, delete the picture and leave the record without
   `thumbnail`. If not, paste the block into the record, set `reviewed: true` and open the PR. Review it at full
   size: the site shows it at half size, but anyone can open the file.
4. The gate checks the rest: the picture's hash matches the record, the kind may be shown, and nothing in
   `thumbnails/` is unclaimed. Replacing a picture means a new file and a new `sha256`; no `sequence` changes, since
   the picture isn't part of an event or action.

## Logging a correction

When a published record was wrong, fix the record and add an entry to the public corrections log in the same pull
request (issue #117). The log is listed on `/methodology/#log`; entries are never removed.

1. **Fix the record** and raise its `sequence`, so calendars and the feed show the update.
2. **Add the entry** as `corrections/<id>.yaml`. Use the correction date and a short description for the ID:

   ```yaml
   # corrections/2026-11-24-guilford-vote-count.yaml
   id: 2026-11-24-guilford-vote-count
   reported_on: 2026-11-23        # Eastern date the report arrived or the problem was found
   corrected_on: 2026-11-24       # Eastern date the fix merges
   record_ids:                    # optional: the jurisdictions, events or actions changed
     - guilford-moratorium-2026-11-05
   summary: "The vote was recorded as 9-0; the minutes show 8-1."
   report: 131                    # optional: the form report it came from (R-131 in the inbox)
   ```

3. **Word the summary** from `docs/wording-bank.md` ("Corrections log entries"): what was wrong and what the record
   says now, in neutral words, with no one's name.
4. **Reply to the reporter** by email from the inbox if they left an address (`docs/wording-bank.md`, "Replies to
   correction reports"), and set the report's status once the fix is live (`docs/reports.md`).

A change to the page's own wording that readers relied on gets an entry with no `record_ids`. A label moving from
reported to confirmed because a new official record was archived is routine and gets none.

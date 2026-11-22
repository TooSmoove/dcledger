# ADR-0010: Known dates: what the record already says is coming

**Status:** Accepted (Nov 20, 2026, PR #160). Generalizes issue #124
(its `/expiries/` page, `expiries.ics` and post-expiry flag become the first slice of this design, under new paths,
before any of them ship). Amends ADR-0002 (one new optional action field), ADR-0004 (the contract additions below)
and ADR-0005 (one new command). Builds on ADR-0008's idempotent issue comments.

**Context:**
- The Hearing Radar learns about a meeting only once its agenda posts, usually about a week ahead (AC9). On Nov 20,
  2026 the home page reads "Nothing scheduled through Sun, Dec 20, 2026", while the reviewed record already holds
  dates when a decision ends or a matter comes back:
  - Chatham's moratorium `expires_on` Feb 11, 2027; Greensboro's Feb 14, 2027 (both `in_effect`).
  - Davidson's Sept 28 minutes say the board "scheduled a public hearing ... to be placed on the October 26th agenda".
    That date was known four weeks ahead, but no field holds it, so it lived only in a YAML comment and the reviewer's
    head. Continuances such as Guilford's of Oct 15 and Vance's of Apr 6 have the same gap whenever their record
    names the date they return.
  - Vance's Nov 13 extension "runs out in mid-December" according to its own record's comment. That is exactly the
    kind of date this design must **not** list until an archived document states it (data/README.md, "Recording a
    decision's terms").
- Readers: the SPEC's primary users are journalists, who need lead time to plan coverage. The owner adds residents,
  who need enough notice to attend or comment (Nov 20, 2026).
- Delivery stays within SPEC §D and §H: no email, accounts or push notifications, and the domain sends no mail. People
  follow the ledger through calendars and the feed. That is the owner's choice of "feeds only" (Nov 20, 2026).
- A known date is a fact about the record, not a prediction. A board can act earlier, later or not at all, so every
  statement says what the record states and nothing about what will happen (SPEC §E, neutrality).
- Statewide trackers count decisions after the fact. Durable, sourced dates ahead of them are what this ledger can
  add, and only from reviewed records.

**Options considered:**
- (A) Ship #124 as written: expiries only, at `/expiries/` and `expiries.ics`, with a post-expiry flag.
- (B) Generalize #124 to every date the reviewed record fixes ("known dates"), add one document-sourced field for
  the next-step date, give advance notice through the feed, and remind the reviewer through the existing review queue.
- (C) Infer dates: count 30, 60 or 180 days from a decision, or take them from news "expected" wording. Rejected:
  data/README.md forbids computed terms, and an inferred date that turns out wrong would be worse than no date.
- (D) Put known dates into `radar.ics`. Rejected: the radar calendar means "meetings we watch". Decisions-only
  jurisdictions (Henderson, Davidson, Forsyth) have no radar calendar but do have expiries. And mixing all-day
  deadlines into a meetings calendar makes both harder to use.
- (E) Email or social posts. Out of scope (SPEC §D, §H; owner, Nov 20, 2026).

**Decision:** B.

1. **What counts as a known date.** A date the reviewed ledger states, of one of two kinds, each derived only from
   merged YAML (AC8):
   - `expiry`: an action's `expires_on`. As in #124, the expiry date is taken as the last day in force, so it has
     passed from the next day on. A jurisdiction whose text says otherwise carries that in `early_termination` or a
     note.
   - `next`: an action's new `next_on` (Decision 2).
   Nothing else becomes a known date: no meeting schedule, no computed period, no news "expected by". Statuses,
   flags and the reminder schedule are worked out from the ledger and **D**, the Eastern calendar date of the
   export's `generated_utc`, so one ledger and one D always give the same output (as for /radar).

2. **New action field `next_on`** (optional, `YYYY-MM-DD`, Eastern): the date an archived document in the action's
   `document_ids` gives for the body's next step on the same matter. Examples are a continuance "to the November 5
   meeting", or a vote that "scheduled a public hearing ... on the October 26th agenda". The rules:
   - It is allowed on any `outcome`, unlike the terms, which are allowed only on adopted decisions. It must be after
     `decided_on` (`INVALID_VALUE`).
   - It is copied from the document, never computed. A comment at the top of the file says where it came from. A
     date taken from a news report keeps the action `reported_news` (the existing rule for terms).
   - It is a published field, so a change raises `sequence` (`SEQUENCE_NOT_RAISED`). A rescheduled date is an edit
     with `sequence` + 1, so subscribed calendars move the entry rather than duplicate it (Decision 4).
   - Exports: `actions.csv` and `actions.parquet` gain `next_on` as their last column (a `DATE` in Parquet), and
     `events.json` actions gain `next_on` after `status`, null when not stated (schema version 1, additive). The
     data dictionary, `PublicContractTests` and the site's contract fixture gain it.

3. **States of a known date at D.** Each known date is in exactly one state:
   - `upcoming`: its date is D or later.
   - `ended`: an `expiry` whose action is `superseded` or `repealed`. It drops off the page, its calendar entry turns
     `STATUS:CANCELLED`, and the successor's own expiry is a new known date (as #124 has it).
   - `recorded`: the date has passed and the record shows what happened. For an `expiry`, `status` is `expired`,
     `superseded` or `repealed`, or `successor_id` is set. For a `next`, the same jurisdiction has an event on that
     Eastern date, or an action decided on that date.
   - `passed_unrecorded`: the date has passed and none of the above holds. This is #124's post-expiry flag, extended
     to `next`. Wording (draft, `docs/wording-bank.md`): "Date passed. Nothing recorded for it yet." It must not
     imply that a moratorium lapsed, was renewed, or that a meeting did or didn't happen.
   A `recorded` date shows its outcome on the decision's own page and leaves `/ahead/` after 90 days.

4. **Calendars.** `public/ahead.ics` covers every known date. `public/ahead/{jurisdiction}.ics` covers each
   jurisdiction whose tier is `watched` or `decisions`, so Henderson, Davidson and Forsyth get their first calendar.
   - Each known date is one all-day `VEVENT` (`DTSTART;VALUE=DATE`, `DTEND` the next day), so the file needs no
     VTIMEZONE.
   - `UID` is a hash of `known`, the kind and the action ID. The date is left out on purpose, so a corrected or
     rescheduled date updates the same entry. `SEQUENCE` is the action's `sequence`.
   - `SUMMARY` uses fixed words from one table (drafts: "Moratorium expiry date: Greensboro", "Date set for the next
     step: Davidson County, moratorium"). `DESCRIPTION` holds the stated terms, the label, the source URLs and a link
     to the decision. It uses no editorial words and never says "deadline" about a date the record doesn't call one.
   - There is no `VALARM`, which is #124's default. Advance notice comes from the feed (Decision 5), and subscribers
     set their own calendar alerts.

5. **Feed items** (`public/feed.xml`, merged with the existing items newest first). For each known date that is not
   `ended`:
   - **Reminders, 30 and 7 days ahead.** These are owner choices (Nov 20, 2026): 30 days lets journalists plan
     coverage, 7 days lets residents decide whether to attend or comment.
     - GUID: `known:{kind}:{action id}/{date}/{lead}`.
     - `pubDate`: 00:00 Eastern on `date - lead`.
     - The item is present once `date - lead` is on or before D.
     - A reminder whose day falls before the action's own `pubDate` (its newest document retrieval) is skipped,
       because the decision's own feed item already announced the date. A continuance recorded 12 days ahead gets
       only the 7-day reminder.
   - **Passed with nothing recorded.**
     - GUID: `known:{kind}:{action id}/{date}/passed`.
     - `pubDate`: 00:00 Eastern on the day after the date.
     - The item is present while the state is `passed_unrecorded`. Recording the outcome doesn't remove an item
       already sent, because the action's own revision item reports the change.
   - Because the GUID includes the date, a rescheduled date gets fresh reminders. Items for the old date drop out of
     the feed, since the ledger no longer states that date.
   - Item text comes from the same fixed table as the calendar, plus the date in Eastern words, the label and a
     link to `/ahead/#{action id}-{kind}` and the decision.
   - Publish runs every 6 hours (best effort, spike S5). An item appears at the first publish on or after its
     `pubDate`, and the `pubDate` itself never depends on when publish ran.

6. **Page `/ahead/`** ("Coming up on the record"). It has no script (AC12) and is noindex like every page.
   - Three sections:
     - "Next 30 days": `upcoming` and within the radar's window, soonest first.
     - "Later": all other `upcoming`, soonest first.
     - "Date passed, nothing recorded": newest first.
   - Each row shows the jurisdiction, the decision in the existing type and outcome words, the date in Eastern words,
     the kind in words, the stated terms (`threshold`, `scope`), the label, and links to the decision and its
     sources. A row whose date matches an event on the radar links that meeting.
   - It has a coverage line: "Only dates stated in an archived document are listed. A body can act on a matter before
     or after these dates."
   - Each row has an anchor `#{action id}-{kind}`.
   - Linked from the header, /radar, each jurisdiction page that has a known date, the FAQ and the methodology page.
   - The `passed_unrecorded` flag also appears on the decision's card on jurisdiction and meeting pages, as in #124.

7. **Home page "Next up" when the window is empty** (owner choice, Nov 20, 2026). After the existing sentence, one
   line names the soonest `upcoming` known date with its jurisdiction and date, links its `/ahead/` row, and links
   `/ahead/`. With nothing upcoming, the line is left out. A window with items is unchanged.

8. **Reviewer reminders** (owner choice, Nov 20, 2026), so that the record is current before readers rely on it.
   - A new command, `dcl remind --data <dir> [--today <yyyy-mm-dd>] [--dry-run] --format github`, works out the same
     known dates and states.
   - It opens or comments through `IReviewQueue`, which gains `FindByMarkerAsync(marker)`. The GitHub adapter lists
     open and closed issues with the `known-date` label and matches a hidden marker. Two stages:
     - **Check before**, 14 days ahead: opens an issue such as "Check before Feb 11, 2027: Chatham County moratorium
       expiry date". The body has the date, the record's terms and links, the official pages to check, and the
       statement that nothing is published by the issue.
     - **Passed with nothing recorded**, the day after: comments on that issue and reopens it.
   - Idempotence follows ADR-0008 Decision 11. Every body and comment ends with
     `<!-- dcl:known-date v1 {kind}:{action id}/{date}/{stage} -->`, and the command skips a stage whose marker
     already exists. A stage missed because GitHub skipped a scheduled run is caught up on the next run while its
     window is still open: from the 14-day mark until the date for "check before", and while the date is
     `passed_unrecorded` for the second stage.
   - Bodies are built only from merged YAML, so no fetched content and no names reach them (SPEC §E, ADR-0003).
   - A new workflow, `remind`, runs on a cron every 6 hours at :37 and on dispatch. It uses the CLI that `ci` built
     and signed (ADR-0007), with `contents: read` and `issues: write` only, and no R2 or Cloudflare environment.
   - `--today` exists for tests and incident replays only; the workflow never passes it.
   - Exit codes follow ADR-0004. If the queue can't be reached, that is exit code 3 (ADR-0005).

9. **Module placement (ADR-0001).**
   - Domain: `KnownDate` (kind, action ID, jurisdiction ID, date) and the pure rules: state at D, reminder days, and
     the UID hash beside `EventUid`.
   - Application: `KnownDates.From(LedgerSnapshot, D)`, `ExportModel` gains them, the validator rule for `next_on`,
     and `Remind` with the port change.
   - Publishing: `AheadCalendarExporter` (Ical.Net, no VTIMEZONE), the `RssFeedWriter` items, and `events.json`'s
     `next_on`.
   - Infrastructure: `GitHubReviewQueue.FindByMarkerAsync`.
   - Cli: `remind`.
   - Site: `site/src/lib/ahead.js` (mirrors the Domain rules for the page, each pinned by a shared fixture test like
     `actionStatusText`), `pages/ahead.astro`, and the home and decision-card changes.

**Rollout** (issues to open on acceptance; slices 1 to 4 live by **Jan 8, 2027**, before the first 30-day reminders:
Chatham on Jan 12, Greensboro on Jan 15):
1. `next_on`: validator, exports, dictionary and contract tests. Backfill `davidson-moratorium-2026-09-28` (Oct 26,
   from the archived minutes). Backfill Guilford's Oct 15 and Vance's Apr 6 continuances only if an archived document
   states their return date. All of these dates are already `recorded`, which makes them good fixtures.
2. Known dates in Domain and Application, plus `ahead.ics` and the jurisdiction calendars, with golden tests.
3. Feed items, with golden tests for each lead and the passed item, and a determinism test across two exports with
   the same ledger and D.
4. `/ahead/`, the home line, links and the decision-card flag, plus the FAQ and methodology text and the wording
   bank. Closes #124.
5. `dcl remind` and the `remind` workflow, live by Jan 28, 2027 (14 days before Chatham's date).

**Consequences:**
- Readers can subscribe to dates weeks or months ahead of an agenda, for every covered jurisdiction, including the
  decisions-only ones. Nothing is stated beyond what an archived document says.
- Public contract additions, none breaking:
  - Paths: `/ahead/`, `/ahead.ics`, `/ahead/{jurisdiction id}.ics`, and the `#{action id}-{kind}` anchors.
  - The `known:` feed GUID scheme, the calendar UID rule, and the `next_on` column and key.
  - The `/expiries/` and `expiries.ics` names from #124 are never published.
  - All of this goes into `docs/stability.md`, the CHANGELOG and the frozen-path tests.
- The feed gains up to three items per known date. At today's record that is two expiries, so six items over the
  next three months.
- Reviewers get one more issue stream, bounded by the number of known dates. An issue that sits unanswered is a sign
  the record is falling behind, and that is the point of it.
- The feed now depends on D as well as the ledger: the same ledger exported on different days gives different
  items. That was already true of #124's design and of /radar. Tests pin D.
- Out of scope: email or push alerts (§D, §H); state, NCUC or NCGA dates; regular meeting schedules; dates from
  legal notices (a later ADR could add them as a third kind sourced from archived notices); changing `status`
  automatically (the gate has no clock, #115).

**Verification:**
- The validator rejects `next_on` on or before `decided_on`, and a changed `next_on` without a raised `sequence`.
- Fixtures cover each state, with dates before, on and after D. A superseded expiry is cancelled in the calendar with
  a raised SEQUENCE. A rescheduled `next_on` keeps its UID and gets new reminder GUIDs.
- A reminder day that falls before the action's `pubDate` is skipped.
- `remind` opens one issue per known date at 14 days, comments once on a passed date, and does nothing on a second
  run with the same D (recorded queue).
- Neither the issues nor the feed carry any field excluded by ADR-0008 Decision 4.
- axe reports zero critical or serious issues, Lighthouse mobile scores at least 90 on `/ahead/`, and no page except
  /map ships a `<script>`.

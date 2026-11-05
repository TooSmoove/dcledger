# ADR-0008: Outcome watching

**Status:** Proposed (Nov 5, 2026). Acceptance is gated on spike S19 below; the action-name map (option choice in **Decision 6**) is open.

**Context:**
- Ingest notices new data-center agenda items and changed agenda bytes, but not what happened at the meeting. Decisions reach the ledger only when a reviewer notices them and archives the action-detail page by hand (SPEC §J, Nov 3; the Oct 15 Guilford continuance, PR #82).
- The Legistar Web API carries outcomes on the agenda item: `EventItemActionName`, `EventItemPassedFlag`/`PassedFlagName` and `EventItemTally` on `GET /v1/{client}/events/{EventId}?EventItems=1`, and the same clerk entry as a matter-history row on `GET /v1/{client}/matters/{MatterId}/histories` (`MatterHistoryEventId`, `…ActionDate`). Granicus's own examples point to matter histories for "actions taken on a Matter (like a vote)". Minutes appear on the event as `EventMinutesFile` and `EventMinutesStatusName`.
- Known hazards: action names are each client's own labels; `PassedFlag` and the tally can be blank on a recorded action (the Guilford Oct 15 action-detail page has no tally); a carried *motion to continue* reads "Pass"; timestamps don't move when minutes post (OCD python-legistar-scraper re-reads a week back for this reason); the API lists hidden duplicate events (1598/1629, 1599/1631; spike S15 addendum); and several fields carry officials' or applicants' names (`EventItemActionText`, `…Mover`, `…Seconder`, vote `VotePersonName`, item titles).
- Owner decision (Nov 5): capture outcomes as a **comment on the existing review issue**; the reviewer still writes the `actions/` record. Nothing is published automatically (AC8). Cancellations stay with #29.

**Options considered:**
- (A) Poll `LastModifiedUtc` with `$filter` and comment on any newer row. Cheap, but timestamps don't reliably move, so outcomes and minutes would be missed.
- (B) Parse the public MeetingDetail/LegislationDetail HTML. Gets the exact page the reviewer archives, but scrapes markup the API already exposes as typed JSON (ADR-0003 prefers static-typed JSON).
- (C) Fingerprint the outcome fields of each watched item, its matter-history rows and the event's minutes, from the API, and comment when the fingerprint changes.

**Decision:** C.

1. **Watch set.** Every meeting that has a review issue, keyed `client:EventId:EventItemId` for each matched item, with its `MatterId` and case number from first ingest. Its same-slot siblings (same trimmed body, date and time) are watched as one group.
2. **Window.** From the first run after the meeting's start (Eastern) until **60 days** after the meeting. Watching **continues after an `actions/` record is merged**, so approved minutes are still caught for the "second document" step (SPEC §J, Nov 3).
3. **Calls per watched meeting per run** (inside the existing ingest run; no new workflow): `events/{EventId}?EventItems=1` with notes and attachments off, then `matters/{MatterId}/histories` once per distinct matched matter. Sequential, through the existing fetcher (rate limit, size caps, user agent). **No `/votes` or `/rollcalls` calls.**
4. **Fingerprint.** SHA-256 of canonical JSON (sorted keys, trimmed strings, explicit nulls) over:
   - item: `ActionId`, `ActionName`, `PassedFlag`, `PassedFlagName`, `Tally`, `MinutesSequence` set or not, `MatterStatus`;
   - matter-history rows whose `MatterHistoryEventId` is in the meeting's group: `Id`, `ActionId`, `ActionName`, `PassedFlag`, `Tally`, `ActionDate`, `EventId`;
   - event: `EventMinutesFile`, `EventMinutesStatusName`, and the minutes file's SHA-256 once archived.
   `LastModifiedUtc`, `RowVersion`, notes, action text, mover, seconder and video are excluded.
5. **When to comment.** Only when the fingerprint differs from the stored one **and** something is present (an action name, a history row for this meeting, or a minutes file). One comment per meeting per run, classed *outcome recorded*, *outcome changed* (old → new action name), *minutes posted* or *minutes changed*. **Every class reopens a closed issue.** A 200 with "Invalid parameters!", non-JSON, or an item list that has gone empty is a source error (ADR-0005): logged, fingerprint kept, no comment. Absence is never read as "outcome removed".
6. **Outcome suggestion (open; see options below).** The comment always shows the raw Legistar action name as data. It pre-fills `outcome` in a draft `actions/` block only through the chosen mapping; an unmapped or ambiguous name (referred, received and filed, hearing held, amended alone) gets no suggestion and the line "needs a human decision". `PassedFlag = Pass` never implies `adopted` by itself.
7. **Votes.** No tally on the platform means **no `votes`**. A tally is used only when it parses as two counts (e.g. `9:0`) and is rewritten `"9-0"`; anything else is shown raw and left out of the draft.
8. **Minutes.** For watched (data-center) meetings only, the minutes file is fetched and kept in the **private bucket** through the agenda path, and its SHA-256 and a draft `documents/` record go in the comment. API minutes are never labelled "approved"; the raw status is shown.
9. **Continuances.** When the outcome maps to `continued`, the comment says so. When a later event's matter history carries the **same `MatterId`**, the comment links that event's review issue, if it has one. A new case number (Guilford's 2026-439 → 2026-452) can't be linked automatically; the keyword ingest opens its own issue for it.
10. **Duplicates.** Matter histories decide which event in a same-slot group carries the action, and the comment names it. If more than one does, or none matches the event the issue was opened for, the comment carries a warning line rather than a silent choice.
11. **Idempotence.** Ingest state (R2) gains `{outcome_fingerprint, comment_id, commented_at}` per meeting. Every comment ends with a hidden marker `<!-- dcl:outcome v1 {client}/{EventId} fp={sha256} -->`; before posting, ingest lists the issue's comments and skips if that marker is already there. This covers lost state and a crash between posting and saving.
12. **Comment content.** Fixed, neutral template: class heading; meeting link and date (Eastern); agenda number and case number; platform action (quoted as data); result or "not recorded"; tally or "no tally recorded"; matter status; action date; minutes link, status and hash or "not yet posted"; fingerprint and retrieval time (UTC, shown Eastern); a draft `actions/` block marked *draft, verify against the official record*, with the action-detail page as the document to archive. **Never** action text, mover, seconder, vote names, item or matter titles, or notes (§E privacy).

**Options for Decision 6 (to choose before acceptance):**
- (6a) **Raw name only.** No suggestion ever; the reviewer picks the token. Zero risk of a wrong pre-fill, no config. At about three data-center items in eight months (S15), the saving from a map is small.
- (6b) **Exact-match map per jurisdiction**, in `data/jurisdictions/<id>.yaml` as `outcome_map: { "<action name>": <token> }` (validator: keys trimmed and case-folded, values only the five outcome tokens). Seeded once from `GET /v1/{client}/actions` and reviewed by hand. Config lives with the bodies list, like #26.
- (6c) **6b plus drift detection.** Each run reads `/actions`; a name not in the map is listed as "unmapped platform action" in the comment, so the map grows from real cases instead of guesses.
- (6d) **Keyword rules** (`continu|postpon|defer` → continued, and so on) with per-client overrides. Covers new names with no setup, but is the likeliest to mislabel ("discontinued", "motion to continue failed").
- **Recommendation: 6c.** Exact matches only keep the label mechanical (§E neutrality); drift detection keeps the map honest without a weekly chore; and anything unmapped degrades to 6a.

**Spike S19 (gates acceptance), run on a GitHub runner** because this environment can't reach the API:
- For Guilford 1599, 1631 and 1600 (after tonight) and one Winston-Salem council meeting with a recorded vote: are `EventItemActionName`, `PassedFlag`, `Tally` and matter histories filled, and with what values? Is `EventMinutesFile` set, with what `EventMinutesStatusName`?
- Does a matter-history `Id` equal the `EventItemId`? Does `EventItemMatterGuid` equal the `GUID=` of the public LegislationDetail URL, so the comment can link the action-detail page directly? If not, link MeetingDetail and say so.
- `GET /v1/{client}/actions` for both clients, to seed 6b/6c.
- What the tally string looks like when present.

**Consequences:**
- Ingest makes one or two more API calls per watched meeting per run; at current volumes that's a handful per run.
- Review issues reopen when outcomes or minutes appear; a reviewer closes them once the `actions/` PR or the minutes document is merged.
- Minutes of data-center meetings join the private bucket, under the same privacy rules as agendas.
- A test fails if any name-bearing field reaches the comment renderer; regression fixtures record a continuance (Guilford 1599), an adoption with a tally and a minutes posting.
- SPEC §J gets an amendment on acceptance (new scope: outcome comments; AC9's review issue now also carries outcomes).
- Cancellations (#29), notifications outside GitHub, and automatic `actions/` PRs stay out of scope.

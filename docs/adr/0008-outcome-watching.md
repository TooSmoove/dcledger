# ADR-0008: Outcome watching

**Status:** Proposed (Nov 5, 2026). Spike S19 done (below); owner chose 6c for the action-name map (Nov 5).

**Context:**
- Ingest notices new data-center agenda items and changed agenda bytes, but not what happened at the meeting or when minutes appear. Decisions reach the ledger only when a reviewer notices them and archives a page by hand (SPEC §J, Nov 3; the Oct 15 Guilford continuance, PR #82).
- Owner decision (Nov 5): **capturing every change comes first.** Changes are reported as a **comment on the existing review issue**; the reviewer keeps writing `actions/` records, amendments and documents by hand. Nothing is published automatically (AC8). Cancellations stay with #29.
- Spike S19 (Nov 5, `spike/s19-outcomes`, live API and public pages from a GitHub runner) found the two clients behave differently:
  - **Winston-Salem** records outcomes in the Web API. 13 of 14 council meetings since March have item actions (`EventItemActionName`, e.g. `Approved`, `EventItemPassedFlagName` `Pass`); each also appears as a matter-history row whose `MatterHistoryId` **equals** the `EventItemId`. `EventItemTally` was blank on every item. Final minutes reach the API (`EventMinutesFile`, status `Final`) 15–24 days after most meetings, 80–87 days after two in June.
  - **Guilford** records no outcomes anywhere in Legistar: 0 actions, 0 tallies and 0 matter histories across 36 Board meetings since March, and the public pages agree (blank Action/Result columns, "Action Details: Not available", empty legislation history). Its case page changes only status (`Held`) and a "Final action" date. **Guilford's minutes are on its public meeting pages but never in the API**: 0 of 175 Board meetings since 2024 have `EventMinutesFile`, and every one says `Draft`, yet the `MeetingDetail` pages for Sep 3 and both Sep 17 meetings link `View.ashx?M=M` PDFs. Oct 1 and Oct 15 had none on Nov 5, so Guilford minutes take more than five weeks.
  - Both clients' action vocabularies (`GET /v1/{client}/actions`) reuse the same `ActionId` for different names (386 is "adopted as amended" in Guilford, "Approved on Second Reading" in Winston-Salem). `ActionUsedFlag` is 0 for every action, so it says nothing.
  - The LegislationDetail `GUID=` in public URLs does not equal `EventItemMatterGuid`, so the API can't build that link; the meeting page links it.
- Other hazards: a carried *motion to continue* reads `Pass`; timestamps don't move when minutes post (OCD python-legistar-scraper re-reads a week back for this reason); the API lists hidden duplicate events (1598/1629, 1599/1631, S15 addendum; Guilford's Board page also lists two Nov 5 meetings); several fields carry officials' or applicants' names (`EventItemActionText`, `…Mover`, `…Seconder`, `VotePersonName`, item titles).

**Options considered:**
- (A) Poll `LastModifiedUtc` and comment on newer rows. Misses minutes and late entries; timestamps don't reliably move.
- (B) API outcome fields only. Works for Winston-Salem; never fires for Guilford, whose minutes and status changes would go unseen.
- (C) Fingerprint everything that carries no names, from the API **and** the public meeting page ingest already reads, and comment with the list of what changed.

**Decision:** C.

1. **Watch set.** Every meeting that has a review issue, keyed `client:EventId:EventItemId` for each matched item, with its `MatterId`, case number and meeting page (`EventInSiteURL`) from first ingest. Same-slot siblings (same trimmed body, date and time) are watched as one group.
2. **Window.** From the first run after the meeting's start (Eastern) until **60 days** after it. Watching continues after an `actions/` record is merged, so minutes are still caught for the "second document" step (SPEC §J, Nov 3). *S19 note: some minutes arrive later (Winston-Salem 80–87 days twice; Guilford over five weeks); see open question 1.*
3. **Reads per watched meeting per run**, inside the existing ingest run, through the existing fetcher (rate limit, size caps, user agent, robots.txt), sequentially:
   - API: `events/{EventId}?EventItems=1` (notes and attachments off), and `matters/{MatterId}` plus `matters/{MatterId}/histories` once per distinct matched matter.
   - Public page: the meeting page (`EventInSiteURL`), the same page ingest reads for the agenda link (`LegistarMeetingPage`). Only fixed shapes are recognized, as for the agenda link: the minutes link `View.ashx?M=M&ID=…&GUID=…`, the "Minutes status" label, and, on the matched case's row, the Action and Result cells and its `LegislationDetail.aspx?ID=…&GUID=…` link.
   - No `/votes` or `/rollcalls` calls.
4. **Fingerprint and change list.** A canonical JSON snapshot (sorted keys, trimmed strings, explicit nulls) of these fields, hashed with SHA-256; the previous snapshot is kept in ingest state so a comment can list each field as *old → new*:
   - event: date, time, body, agenda status, minutes status (API and page), minutes link (API and page), minutes file SHA-256 once archived;
   - each matched item: agenda number, case number, version, action id and name, passed flag and name, tally, minutes sequence, matter status, page Action and Result cells, case-page link;
   - matter: status, type, "final action"/passed and enactment dates, version, agenda and intro dates;
   - matter-history rows for the meeting's group: id, event id, action date, action id and name, acting body, passed flag, tally.
   Never in the snapshot or comment: titles, names (matter or item), action text, mover, seconder, vote names, notes, attachments' names, `LastModifiedUtc`, `RowVersion`.
5. **When to comment.** Whenever the fingerprint differs from the stored one, except on the first snapshot of a meeting (the baseline, taken when watching starts). **Every comment reopens a closed issue.** Each comment is classed by what changed: *outcome recorded/changed* (action, result, history), *minutes posted/changed*, *status changed* (matter status, final-action date, versions), or *other change*. One comment per meeting per run. A 200 with "Invalid parameters!", non-JSON, a page whose shape isn't recognized (FormatChanged, ADR-0005), or an item list that has gone empty is a source error: logged, snapshot kept, no comment. Absence is never read as "removed".
6. **Outcome suggestion: 6c.** Comments always show the raw action name as data. A per-jurisdiction exact-match map in `data/jurisdictions/<id>.yaml` (`outcome_map: { "<action name>": <token> }`, keyed by **name**, never `ActionId`; trimmed and case-folded; values only the five outcome tokens; seeded from `GET /v1/{client}/actions`) pre-fills `outcome` in a draft `actions/` block. A name not in the map is listed as "unmapped platform action" and gets no suggestion ("needs a human decision"). `PassedFlag = Pass` never implies `adopted` by itself. Guilford comments carry no suggestion, because Guilford records no actions.
7. **Votes.** No tally on the platform means **no `votes`**. A tally is used only when it parses as two counts and is rewritten ayes-noes; anything else is shown raw and left out.
8. **Minutes.** For watched (data-center) meetings only, a newly seen minutes file (API or page link) is fetched and kept in the **private bucket** through the agenda path; its SHA-256 and a draft `documents/` record go in the comment. Minutes are never labelled "approved" from the platform; the raw status is shown (Guilford's page says `Draft` even with a PDF posted).
9. **Continuances.** When the outcome maps to `continued`, the comment says so. When a later event's matter history or agenda carries the **same `MatterId`**, the comment links that event's review issue, if it has one. A new case number (Guilford's 2026-439 → 2026-452) can't be linked automatically; the keyword ingest opens its own issue for it.
10. **Duplicates.** The whole same-slot group is snapshotted. Matter histories (Winston-Salem) or the page that links the agenda (Guilford) identify which event is live; if that is ambiguous, the comment carries a warning line rather than a silent choice.
11. **Idempotence.** Ingest state (R2) gains `{snapshot, fingerprint, comment_id, commented_at}` per meeting. Every comment ends with a hidden marker `<!-- dcl:outcome v1 {client}/{EventId} fp={sha256} -->`; before posting, ingest lists the issue's comments and skips if that marker is already there.
12. **Comment content.** Fixed, neutral template: class heading; meeting link and date (Eastern); agenda number and case number; the change list (*field: old → new*); platform action (quoted as data) and result, or "not recorded on the platform"; tally or "no tally recorded"; minutes link, status and hash, or "not yet posted"; case-page link; snapshot fingerprint and retrieval time (UTC, shown Eastern); for an outcome, a draft `actions/` block marked *draft, verify against the official record*. The names listed in Decision 4 never appear.

**Options considered for Decision 6:**
- (6a) Raw name only; no suggestion.
- (6b) Exact-match map per jurisdiction in its YAML.
- (6c) 6b plus drift detection: names not in the map are listed so the map grows from real cases.
- (6d) Keyword rules with overrides; likeliest to mislabel ("motion to continue failed").
- **Chosen: 6c** (owner, Nov 5).

**Consequences:**
- Per watched meeting per run: three or four API calls and one page fetch. At about three data-center meetings in eight months (S15) that's negligible.
- Comments are more frequent than outcome-only watching would give (any status or minutes change). That is intended: missing a change is worse than one extra comment.
- Guilford outcomes still need a human: its votes are in no Legistar field, so the reviewer records them from the minutes (now captured) or coverage (`reported_news`).
- Reading the public meeting page extends ADR-0003's untrusted-HTML surface by three fixed link and label shapes; the same validation applies as for the agenda link.
- Minutes of data-center meetings join the private bucket under the same privacy rules as agendas.
- `data/jurisdictions/` gains the optional `outcome_map` key; the validator and ADR-0004 are amended when it lands.
- Tests: a renderer test fails if any field from Decision 4's exclusion list reaches a comment; regression fixtures record a Winston-Salem approval (2821, case 26-0310), a Guilford meeting page before and after minutes appear (Sep 17), a duplicate-slot group, and a source error that must not comment.
- SPEC §J gets an amendment on acceptance (new scope: change comments on review issues).
- Out of scope: cancellations (#29), notifications outside GitHub, automatic `actions/` PRs, minutes posted outside Legistar.

**Open questions for the owner:**
1. Keep 60 days, or extend the minutes watch to 90 days? S19 shows Guilford minutes take over five weeks and Winston-Salem's twice took 80+ days.

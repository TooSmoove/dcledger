# Ingest

`dcl ingest` reads one jurisdiction's agenda platform. When an agenda item matches the keyword rules, it keeps the
agenda in the private bucket and opens a review issue. A person then decides what is published (SPEC AC8). The
`ingest` workflow runs it for every automated jurisdiction every 6 hours at :17 UTC.

## What one run does

1. **Asks the platform for meetings.** Covers the jurisdiction's configured bodies, dated 30 days back to 60 days ahead
   (Eastern dates).
2. **Records first sightings.** For every meeting with a published agenda, records when ingest first saw it
   (`first_seen_utc`, the review clock in SPEC AC9). Only the time is stored; the agenda itself isn't downloaded.
3. **Downloads matching agendas only.** When at least one agenda item matches, downloads the agenda and stores the bytes
   under their SHA-256. The first time, it opens a review issue with the provenance and ready-to-paste YAML. Original
   titles never appear in an issue, the job log or the summary.
4. **Follows republished agendas.** When a matching meeting's agenda bytes change later, it comments on the issue,
   reopening it if needed. A rerun with nothing new opens nothing.
5. **Writes the run log.** Each run's log in the private bucket lists every document it retrieved: URL, time, SHA-256,
   media type and size.

**Where Legistar agendas come from.** The API's `EventAgendaFile` points to Legistar's file host
(`{client}.legistar1.com`). Its robots.txt is `User-agent: *` / `Disallow: /`, checked Oct 29, 2026, so ingest never
fetches from it.
- Instead, the adapter reads the agenda link on the meeting's public page (`{client}.legistar.com/MeetingDetail.aspx`),
  `View.ashx?M=A&ID=…&GUID=…`, and downloads the PDF there. That host sets no robots.txt restrictions and serves the
  same bytes (same SHA-256).
- Only that link shape is recognized. If the page stops linking exactly one agenda, the meeting fails as a format change
  instead of guessing.

| Private bucket key | Contents |
|---|---|
| `docs/sha256/{ab}/{hash}` | Agenda bytes; never rewritten |
| `state/{platform}/{client}/{meetingId}.json` | First seen, last agenda hash, review issue number |
| `runs/{yyyy}/{MM}/{jurisdiction}/{runId}.json` | Run log and manifest |

## When it fails

A red job means that jurisdiction wasn't checked (ADR-0005). The workflow opens, reopens or comments on the issue
**"Ingest failing: {jurisdiction}"** (label `scraper-broken`) with a link to the run, and the next successful run
closes it.

| Exit code | Meaning |
|---|---|
| 2 | Bad arguments or configuration |
| 3 | The source, the private bucket or GitHub failed after retries |
| 4 | A bug |

If an agenda can't be downloaded, only that meeting fails, and the run still goes red.

## Running it

**In GitHub.** Go to Actions → *ingest* → *Run workflow*. You can:
- pick one jurisdiction;
- choose a **dry run**, which reads the source and downloads matching agendas but stores nothing and opens no issue;
- allow a stale CLI build, which is for incidents only (ADR-0007).

**Locally**, a dry run needs no credentials:

```sh
dotnet run --project src/DcLedger.Cli -- ingest --jurisdiction guilford --dry-run
```

A real run needs these environment variables. In Actions they come from repository variables, the `r2-docs`
environment's secrets and the job token:
- `R2_ACCOUNT_ID`
- `R2_PRIVATE_BUCKET`
- `R2_PRIVATE_ACCESS_KEY_ID`
- `R2_PRIVATE_SECRET_ACCESS_KEY`
- `GITHUB_REPOSITORY`
- `GITHUB_TOKEN`

## Adding a jurisdiction

Add `data/jurisdictions/{id}.yaml` with `platform: legistar`, its `legistar_client` and its `bodies` exactly as the
Legistar API spells them. The workflow picks it up on its next run; no code or workflow change is needed.

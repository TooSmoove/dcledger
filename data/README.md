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
- `confirmed_official` needs at least one document with `source_type: official` (`LABEL_INVALID`).
- `starts_at` carries the Eastern offset in effect that day (`-04:00` or `-05:00`); times skipped or repeated by daylight saving are rejected.
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

# actions/<id>.yaml  (example values)
id: fixture-county-moratorium-2026-01-15
jurisdiction_id: fixture-county
action_type: moratorium
outcome: adopted              # as recorded in the official minutes
decided_on: 2026-01-15
votes: "5-2"                  # optional
label: confirmed_official
document_ids: [<sha256>]
```

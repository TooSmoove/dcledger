# Stability

Once someone subscribes to a calendar, bookmarks a download or cites an item, changing its address or its shape
breaks them. This page is the promise, from v0.1.0 on (issue #116). The [changelog](../CHANGELOG.md) records every
change to it.

<!--
Published on the site at /data/#stability, so write it for the site's readers. Links to other files are rewritten for
the site and issue references like "(#116)" are left out there (site/src/lib/public-docs.js).
-->

## These don't change

- **Record IDs:** event, decision (local action) and jurisdiction IDs, and document IDs (the SHA-256 of the bytes).
- **Page addresses:** `/radar/`, `/map/`, `/about/`, `/methodology/`, `/items/{event id}/`,
  `/jurisdictions/{jurisdiction id}/`, the `#decision-{decision id}` anchor on a jurisdiction page, and the
  `#correction-{correction id}` anchor on the methodology page.
- **Calendar and feed identity:** each event's calendar UID (a hash of jurisdiction, platform and platform meeting
  ID), each known date's calendar UID (a hash of `known`, the kind and the decision ID, so a corrected date updates in
  place) and each feed GUID (`{UID}/{sequence}` for an event revision, `action:{id}/{sequence}` for a decision
  revision).
- **File paths:** `/radar.ics`, `/radar/{jurisdiction id}.ics`, `/ahead.ics`, `/ahead/{jurisdiction id}.ics`, `/feed.xml`, `/events.csv`, `/events.parquet`,
  `/actions.csv` and `/actions.parquet`.
- **Columns:** the name, order, type and meaning of every column in the CSV and Parquet files (the
  [data dictionary](data-dictionary.md)), and the keys and types of `events.json` within its `schema_version`.
- **Vocabularies:** a published value keeps its meaning (see
  [Vocabularies](data-dictionary.md#vocabularies)).

## These can happen at any time, because they don't break a reader

- New columns, added at the end; new keys in `events.json`; new files, pages and feeds.
- New values in a vocabulary. Readers should treat a value they don't know as "other", not as an error.
- New records, and corrections to a record's values. A published correction raises the record's `sequence`, so
  calendars and feed readers see it.

## Breaking changes

A breaking change renames, removes, reorders or retypes anything in the first list. It is announced in the
[changelog](../CHANGELOG.md) at least **90 days** before it ships, saying what changes, when, and what to use
instead. `events.json` also bumps `schema_version`. A record is never renamed. If one has to be removed (for example,
a duplicate entered by mistake), the changelog lists it.

Automated tests check the published addresses, the columns and the vocabularies against this promise on every
change, so a change that breaks it can't ship without being announced.

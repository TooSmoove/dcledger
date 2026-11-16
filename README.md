# NC Data Center Ledger

Public records on data-center decisions in North Carolina: upcoming meetings and hearings, the decisions local
governments make, and the official documents behind them. Every item links to its source, with the time it was
retrieved and the SHA-256 hash of the copy kept. A person reviews every item before it is published.

The site is at <https://ncdcledger.org> (unlisted until launch).

## The data

- **Calendars and feed:** `/radar.ics`, `/radar/{jurisdiction}.ics` and `/feed.xml` on the site.
- **Downloads:** `/events.csv`, `/events.parquet`, `/actions.csv` and `/actions.parquet`. Every column is described in
  [`docs/data-dictionary.md`](docs/data-dictionary.md).
- **What stays stable:** IDs, addresses, columns and vocabularies don't change without 90 days' notice in the
  [CHANGELOG](CHANGELOG.md). See [`docs/stability.md`](docs/stability.md).
- **The reviewed records** are YAML files in [`data/`](data/README.md), which also explains how to add and review them.

## The code

- [`docs/SPEC.md`](docs/SPEC.md): what v0.1 does, its acceptance criteria and every amendment.
- [`docs/adr/`](docs/adr/README.md): architecture decisions.
- `src/`: the `dcl` command-line tool (.NET 10): ingest, validate, export, archive-document.
- [`site/`](site/README.md): the static site (Astro).

## Changes

See [CHANGELOG.md](CHANGELOG.md).

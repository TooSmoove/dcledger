# Changelog

Changes to what NC Data Center Ledger publishes: its pages, calendars, feed and downloads. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

What may change and what may not is in [Stability](docs/stability.md). A breaking change is
listed here at least 90 days before it ships, with what to use instead. Column-level detail is in
the [data dictionary](docs/data-dictionary.md).

## [Unreleased]

The first release, v0.1.0 (#35): the site goes live unlisted at <https://ncdcledger.org>.

Some decisions go live labelled "reported (news)", or with terms or a vote count left out, because the government
has not posted the official record yet. Each gains them, as an update in the feed, once that record is archived
(#112).

### Added

- **Hearing Radar** (`/radar/`): every reviewed meeting and hearing about data centers in the next 30 days, in
  Eastern time, each linked to its official source with the retrieval time and SHA-256 of the copy kept.
- **Pages** for each meeting (`/items/{id}/`) and jurisdiction (`/jurisdictions/{id}/`), a map with an equivalent
  table (`/map/`), and an about page (`/about/`).
- **Methodology and corrections** (`/methodology/`): how items get in, what the labels mean, provenance, coverage,
  who publishes the ledger, how to cite an item, how to report a mistake, and a dated corrections log that opens
  Nov 22, 2026 (#117).
- **Freshness deadline:** each watched jurisdiction's "last checked" line also says when it counts as stale if
  nothing checks it again, so a page from a publish that stopped running still dates itself (#103).
- **Design (Claret):** a burgundy band and brass rail over parchment, with a dark scheme and inlined CSS. The radar
  draws its 30-day window as dated cells, each recorded vote is drawn as a half circle of seats, and each SHA-256 is
  drawn as an 8 by 8 pattern beside the hash. All three are decorative; the text stays the record (#133).
- **Imagery** (#132): each jurisdiction page draws a static locator map of where it is in North Carolina, from U.S.
  Census Bureau boundaries, with no script. Sources that are official minutes or legislation pages can show a
  reviewed picture of page 1 of the exact copy whose SHA-256 they print, at `/thumbnails/{sha256}.webp`; agenda
  packets never do. A ring marks only an area too small to see both ways (so a long, narrow county such as Vance has
  none), and a ringed city is drawn inside its county, shaded lightly (Henderson in Vance County). Headings and body text are set in EB Garamond, one 37 KB font file served from this site.
- **Home page** (`/`): the record at a glance, built from the export. Next up from the radar's window, decisions
  the record states are in effect with their end dates, the latest decisions, coverage with freshness, and the feeds
  (#134). "How to check the record" moved to the FAQ, and the home page no longer links the calendar feeds (they stay
  on the radar, on jurisdiction pages and in the FAQ). When nothing is scheduled, "Next up" says how many
  jurisdictions are watched and since when all were checked, and a "What's new on the record" section lists the
  five records most recently added or updated.
- **FAQ** (`/faq/`): how to check the record and a hash, what the labels and coverage tiers mean, how to subscribe to
  a calendar feed (and why opening the `.ics` file only imports a copy), other ways to follow new items, and how to
  report a mistake. Linked from every header and footer.
- **Report form** (`/report/`): report a mistake, ask a question or ask us to stop fetching, with an optional email
  address for a reply. Reports are private; a fix shows in the corrections log. Linked from every footer.
- **Using the data** (`/data/`), with the stability promise and the data dictionary, and this changelog
  (`/changelog/`), both on the site.
- **Coverage:** Guilford County, Winston-Salem and Chatham County (automated Legistar ingest), and Greensboro, Stokes
  County and Vance County (entered by hand). Each jurisdiction states its coverage tier; all six are `watched`.
  Forsyth County and Davidson County are `decisions`: decisions recorded from the official record, hearings not
  watched (#118).
- **Jurisdiction notes:** a jurisdiction page can show one neutral "Note on the record", with its sources, for
  something that bears on its record without being one of its body's decisions, such as a court order on a
  moratorium. `events.json` jurisdictions gain `note` and `note_documents`; the downloads are unchanged. Each decision
  of a jurisdiction with a note, on its jurisdiction and meeting pages and on the home page, links to the note
  (`/jurisdictions/{id}/#note`), so a reader who arrives at a `#decision-` link doesn't miss it.
- **Sources** show the URL, the retrieval time and how the copy was retrieved; the SHA-256 and its pattern are under
  a "More details" disclosure beside each source, still in the page. A source link opens in a new tab, marked with
  "↗" and, for screen readers, "(opens in a new tab)".
- **Decisions** (local actions) on meeting and jurisdiction pages, with outcome, vote and sourcing label, and, for an
  adopted decision, the terms its archived text states: threshold, scope, start and expiry dates, early termination,
  exemptions, a successor and a status.
- **Calendars:** `/radar.ics` and `/radar/{jurisdiction}.ics` for each watched jurisdiction. UIDs are stable, and a
  cancelled meeting keeps its UID with `STATUS:CANCELLED`.
- **Feed:** `/feed.xml` (RSS 2.0), one item per reviewed change to a meeting or a decision.
- **Downloads:** `/events.csv` and `/events.parquet` (one row per meeting), `/actions.csv` and `/actions.parquet`
  (one row per decision). The CSVs are UTF-8 with a BOM, RFC 4180, with a formula guard on text cells.
- **Columns added during the week before release** (they are part of the v0.1.0 contract):
  - `document_retrieval` as the last column of `events.csv` and `actions.csv`, and `retrieval` as the last field of
    the Parquet `documents` struct: whether each copy was fetched by our program or saved by a person (#113).
  - `instrument_number`, `threshold`, `scope`, `effective_on`, `expires_on`, `early_termination`, `exemptions`,
    `successor_id` and `status` after `document_retrieval` in `actions.csv`, and after `documents` in
    `actions.parquet` (#115).
- **Stability promise** for IDs, page addresses, UIDs, feed GUIDs, file paths, columns and vocabularies, held by
  tests (#116).

"""Records Legistar Web API responses as scrubbed test fixtures (issue #17, ADR-0006).

  python3 tools/record_legistar_fixtures.py --client guilford --from 2026-10-01 --to 2026-12-01 \
      --body "Board of Commissioners" --body "Board of Commissioners - Work Session" \
      --out tests/fixtures/legistar/guilford

Writes events.json (every body in the window) and eventitems-{EventId}.json (configured bodies only), keeping
Legistar's field names, types and formats. Scrubbing is an allowlist: a field not listed below is kept only if it
isn't text. Agenda item titles, matter names and comments are replaced with synthetic text that keeps one signal:
whether the original matched the keyword classifier (and, for comments, whether it mentioned a cancellation).
Each item list keeps its first few items plus every classifier match. Run it where Legistar is reachable (a GitHub
runner); review the output before committing. Standard library only.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import date
from pathlib import Path

API = "https://webapi.legistar.com/v1"
USER_AGENT = "dc-ledger-fixtures/0.1 (+https://ncdcledger.org/about/)"
PAUSE_SECONDS = 0.5
ITEMS_KEPT = 5
SCRUBBED = "[scrubbed]"

# Same rules as DcLedger.Domain.MeetingItemClassifier; only used to keep the "matched" signal through scrubbing.
CLASSIFIER = re.compile(
    r"\bdata[\s-]*(cent(er|re)s?|processing)\b|\bdatacent(er|re)s?\b|\bhyper[\s-]?scale\b"
    r"|\bserver\s+farms?\b|\bco-?location\s+(facilit|center|centre)",
    re.IGNORECASE,
)
CASE_NUMBER = re.compile(r"^[A-Za-z0-9-]{1,40}$")

EVENT_KEEP = {
    "EventId", "EventGuid", "EventLastModifiedUtc", "EventRowVersion", "EventBodyId", "EventBodyName",
    "EventDate", "EventTime", "EventVideoStatus", "EventAgendaStatusId", "EventAgendaStatusName",
    "EventMinutesStatusId", "EventMinutesStatusName", "EventLocation", "EventAgendaFile", "EventMinutesFile",
    "EventAgendaLastPublishedUTC", "EventMinutesLastPublishedUTC", "EventVideoPath", "EventMedia", "EventInSiteURL",
}
ITEM_KEEP = {
    "EventItemId", "EventItemGuid", "EventItemLastModifiedUtc", "EventItemRowVersion", "EventItemEventId",
    "EventItemAgendaSequence", "EventItemMinutesSequence", "EventItemAgendaNumber", "EventItemVideo",
    "EventItemVideoIndex", "EventItemVersion", "EventItemActionId", "EventItemActionName", "EventItemPassedFlag",
    "EventItemPassedFlagName", "EventItemRollCallFlag", "EventItemFlagExtra", "EventItemTally",
    "EventItemAccelaRecordId", "EventItemConsent", "EventItemMatterId", "EventItemMatterGuid",
    "EventItemMatterType", "EventItemMatterStatus",
}


def scrub_value(value: object) -> object:
    """Text becomes a placeholder; numbers, booleans and nulls stay; containers are scrubbed throughout."""
    if isinstance(value, str):
        return SCRUBBED if value else value
    if isinstance(value, list):
        return [scrub_value(v) for v in value]
    if isinstance(value, dict):
        return {k: scrub_value(v) for k, v in value.items()}
    return value


def scrub_event(event: dict) -> dict:
    scrubbed = {}
    for key, value in event.items():
        if key in EVENT_KEEP:
            scrubbed[key] = value
        elif key == "EventComment" and value:
            mentions_cancel = "cancel" in str(value).lower()
            scrubbed[key] = "Synthetic comment: meeting cancelled" if mentions_cancel else "Synthetic comment"
        elif key == "EventItems":
            scrubbed[key] = []
        else:
            scrubbed[key] = scrub_value(value)
    return scrubbed


def item_text(item: dict) -> str:
    keys = ("EventItemTitle", "EventItemMatterName", "EventItemMatterFile", "EventItemMatterType")
    return " | ".join(str(item.get(key) or "") for key in keys)


def scrub_item(item: dict, number: int) -> dict:
    candidate = bool(CLASSIFIER.search(item_text(item)))
    scrubbed = {}
    for key, value in item.items():
        if key in ITEM_KEEP:
            scrubbed[key] = value
        elif key == "EventItemTitle":
            scrubbed[key] = f"Synthetic agenda item {number}" + (" about a data center" if candidate else "")
        elif key == "EventItemMatterName" and value:
            scrubbed[key] = f"Synthetic matter {number}"
        elif key == "EventItemMatterFile" and value:
            scrubbed[key] = value if CASE_NUMBER.match(str(value)) else SCRUBBED
        else:
            scrubbed[key] = scrub_value(value)
    return scrubbed


def trim_items(items: list[dict], keep: int = ITEMS_KEPT) -> list[dict]:
    """The first `keep` items in agenda order, plus every later item the classifier matches."""
    return [item for index, item in enumerate(items) if index < keep or CLASSIFIER.search(item_text(item))]


def events_url(client: str, start: date, end: date) -> str:
    query = urllib.parse.quote(f"EventDate ge datetime'{start.isoformat()}' and EventDate lt datetime'{end.isoformat()}'")
    return f"{API}/{urllib.parse.quote(client)}/events?$filter={query}"


def fetch_json(url: str) -> object:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read()
    time.sleep(PAUSE_SECONDS)
    return json.loads(body)


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--client", required=True)
    parser.add_argument("--from", dest="start", type=date.fromisoformat, required=True)
    parser.add_argument("--to", dest="end", type=date.fromisoformat, required=True)
    parser.add_argument("--body", dest="bodies", action="append", required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args(argv)

    args.out.mkdir(parents=True, exist_ok=True)
    events = fetch_json(events_url(args.client, args.start, args.end))
    write_json(args.out / "events.json", [scrub_event(e) for e in events])

    recorded = 0
    for event in events:
        if event.get("EventBodyName") not in args.bodies:
            continue
        items = fetch_json(f"{API}/{urllib.parse.quote(args.client)}/events/{int(event['EventId'])}/eventitems")
        kept = trim_items(items)
        write_json(args.out / f"eventitems-{int(event['EventId'])}.json", [scrub_item(i, n) for n, i in enumerate(kept, 1)])
        recorded += 1
    print(f"Recorded {len(events)} events and item lists for {recorded} meetings in {args.out}.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

"""Spike S19 (ADR-0008): what the Legistar Web API records as an item's outcome and a meeting's minutes.

Prints Markdown to stdout. The repository and its run logs are public, so this prints ONLY fields that carry no
names: ids, case numbers, action names, flags, tallies, dates, minutes status and whether URLs/GUIDs match.
Never action text, mover/seconder, vote names, item or matter titles, or notes (SPEC §E).

Usage: python3 -I spike.py   (needs network; run on a GitHub runner)
"""

import json
import sys
import time
import urllib.error
import urllib.request

UA = "dc-ledger-spike/0.1 (+https://github.com/TooSmoove/dcledger)"
BASE = "https://webapi.legistar.com/v1"

# (client, EventId, case numbers to look at). Guilford 1599/1631 are the Oct 15 same-slot pair (S15 addendum),
# 1600 is Nov 5; Winston-Salem 2821 (Aug 17, case 26-0310) is the recorded data-center match (#26 fixtures).
TARGETS = [
    ("guilford", 1599, {"2026-439"}),
    ("guilford", 1631, {"2026-439"}),
    ("guilford", 1600, {"2026-452"}),
    ("winston-salem", 2821, {"26-0310"}),
]

# The LegislationDetail page archived for the Oct 15 continuance (PR #82): does its GUID equal EventItemMatterGuid?
KNOWN_LEGISLATION_GUID = ("guilford", "B62B5B04-4075-414F-A4A1-B10CA85FF96C")

ITEM_FIELDS = [
    "EventItemId", "EventItemAgendaNumber", "EventItemMatterFile", "EventItemMatterId", "EventItemActionId",
    "EventItemActionName", "EventItemPassedFlag", "EventItemPassedFlagName", "EventItemTally",
    "EventItemRollCallFlag", "EventItemMinutesSequence", "EventItemMatterStatus", "EventItemLastModifiedUtc",
]
HISTORY_FIELDS = [
    "MatterHistoryId", "MatterHistoryEventId", "MatterHistoryActionDate", "MatterHistoryActionId",
    "MatterHistoryActionName", "MatterHistoryActionBodyName", "MatterHistoryPassedFlag",
    "MatterHistoryPassedFlagName", "MatterHistoryTally", "MatterHistoryMatterStatusId",
]


def get(path):
    req = urllib.request.Request(f"{BASE}/{path}", headers={"User-Agent": UA, "Accept": "application/json"})
    time.sleep(1.5)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            body = resp.read(8 * 1024 * 1024)
    except urllib.error.HTTPError as e:
        return None, f"HTTP {e.code}"
    try:
        return json.loads(body), None
    except ValueError:
        return None, f"non-JSON reply: {body[:60]!r}"


def cell(v):
    if v is None:
        return "∅"
    s = str(v).strip().replace("|", "\\|").replace("\n", " ")
    return f"`{s}`" if s else "(blank)"


def table(rows, fields):
    out = ["| " + " | ".join(f.removeprefix("EventItem").removeprefix("MatterHistory") for f in fields) + " |",
           "|" + "---|" * len(fields)]
    out += ["| " + " | ".join(cell(r.get(f)) for f in fields) + " |" for r in rows]
    return "\n".join(out)


def main():
    print("# Spike S19 — Legistar outcomes and minutes (ADR-0008)\n")
    print(f"Run {time.strftime('%Y-%m-%d %H:%M UTC', time.gmtime())}. Names, titles, action text and notes are"
          " deliberately not printed.\n")
    matter_guids = {}
    for client, event_id, cases in TARGETS:
        print(f"## {client} event {event_id} (cases {', '.join(sorted(cases))})\n")
        ev, err = get(f"{client}/events/{event_id}?EventItems=1&AgendaNote=0&MinutesNote=0&EventItemAttachments=0")
        if err or not isinstance(ev, dict):
            print(f"- event: {err or 'unexpected shape'}\n")
            continue
        print(f"- body `{ev.get('EventBodyName', '').strip()}`, date `{ev.get('EventDate')}` `{ev.get('EventTime')}`")
        print(f"- agenda status `{ev.get('EventAgendaStatusName')}`; minutes status `{ev.get('EventMinutesStatusName')}`;"
              f" minutes file {'set' if ev.get('EventMinutesFile') else 'not set'};"
              f" minutes last published `{ev.get('EventMinutesLastPublishedUTC')}`")
        items = ev.get("EventItems") or []
        acted = sum(1 for i in items if (i.get("EventItemActionName") or "").strip())
        tallied = sum(1 for i in items if (i.get("EventItemTally") or "").strip())
        print(f"- items: {len(items)}; with an action name: {acted}; with a tally: {tallied}\n")
        matched = [i for i in items if (i.get("EventItemMatterFile") or "").strip() in cases]
        if not matched:
            print("Matched case not on this event.\n")
            continue
        print(table(matched, ITEM_FIELDS) + "\n")
        for item in matched:
            mid = item.get("EventItemMatterId")
            matter_guids[(client, mid)] = (item.get("EventItemMatterGuid") or "").upper()
            hist, herr = get(f"{client}/matters/{mid}/histories?AgendaNote=0&MinutesNote=0")
            if herr or not isinstance(hist, list):
                print(f"- histories for matter {mid}: {herr or 'unexpected shape'}\n")
                continue
            print(f"Matter {mid} histories ({len(hist)} rows); history id equals an item id here:"
                  f" {any(h.get('MatterHistoryId') == item.get('EventItemId') for h in hist)}\n")
            print(table(hist, HISTORY_FIELDS) + "\n")
    client, guid = KNOWN_LEGISLATION_GUID
    hits = [mid for (c, mid), g in matter_guids.items() if c == client and g == guid]
    print(f"## LegislationDetail GUID check\n\nArchived page GUID equals EventItemMatterGuid of matter(s): "
          f"{hits or 'none of the matched matters'}\n")
    for c in sorted({t[0] for t in TARGETS}):
        acts, aerr = get(f"{c}/actions")
        print(f"## {c} action vocabulary\n")
        if aerr or not isinstance(acts, list):
            print(f"{aerr or 'unexpected shape'}\n")
            continue
        print(table(sorted(acts, key=lambda a: str(a.get("ActionName"))),
                    ["ActionId", "ActionName", "ActionActiveFlag", "ActionUsedFlag"]) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())

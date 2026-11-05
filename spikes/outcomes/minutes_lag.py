"""Spike S19c: which Guilford Board meetings since 2024 have a minutes file in the API, and how long after the
meeting it was published. Prints dates, ids, statuses and day counts only."""
import json, sys, time, urllib.request
from datetime import datetime
UA = "dc-ledger-spike/0.1 (+https://github.com/TooSmoove/dcledger)"
url = ("https://webapi.legistar.com/v1/guilford/events?$filter=EventDate+ge+datetime%272024-01-01%27"
       "+and+EventDate+lt+datetime%272026-11-01%27&$orderby=EventDate")
with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=60) as r:
    evs = json.loads(r.read())
print("# Spike S19c — Guilford minutes since 2024\n")
board = [e for e in evs if (e.get("EventBodyName") or "").strip().startswith("Board of Commissioners")]
with_min = [e for e in board if e.get("EventMinutesFile")]
print(f"Board events: {len(board)}; with a minutes file: {len(with_min)}\n")
print("| EventId | date | body | minutes status | minutes link kind | days to last publish |\n|---|---|---|---|---|---|")
for e in with_min:
    pub = e.get("EventMinutesLastPublishedUTC")
    lag = (datetime.fromisoformat(pub[:19]) - datetime.fromisoformat(e["EventDate"][:19])).days if pub else ""
    link = e["EventMinutesFile"].split("?")[0].rsplit("/", 1)[-1][-12:]
    print(f"| {e['EventId']} | {e['EventDate'][:10]} | {e['EventBodyName'].strip()} | {e.get('EventMinutesStatusName')} | …{link} | {lag} |")
last = max((e["EventDate"][:10] for e in with_min), default="none")
print(f"\nMost recent meeting with minutes in the API: {last}")

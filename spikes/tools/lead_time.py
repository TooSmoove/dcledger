"""Spike S13: agenda lead time (agenda published -> meeting start) per Legistar body.

Usage: python3 -I lead_time.py <events.json> [<events.json> ...]
Each file is a Legistar Web API /events response (JSON array).
Writes a Markdown table to stdout. Standard library only.
"""
import json
import statistics
import sys
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

EASTERN = ZoneInfo("America/New_York")


def meeting_start_utc(ev):
    date = ev.get("EventDate")  # "2026-11-05T00:00:00" (date-only at midnight)
    time = (ev.get("EventTime") or "").strip()  # "5:30 PM"
    if not date or not time:
        return None
    d = datetime.fromisoformat(date[:10])
    try:
        t = datetime.strptime(time.upper(), "%I:%M %p")
    except ValueError:
        return None
    local = d.replace(hour=t.hour, minute=t.minute, tzinfo=EASTERN)
    return local.astimezone(timezone.utc)


def published_utc(ev):
    p = ev.get("EventAgendaLastPublishedUTC")
    if not p:
        return None
    return datetime.fromisoformat(p.rstrip("Z")[:19]).replace(tzinfo=timezone.utc)


def main(paths):
    rows = []
    for path in paths:
        with open(path, encoding="utf-8") as f:
            events = json.load(f)
        by_body = {}
        missing = 0
        for ev in events:
            start, pub = meeting_start_utc(ev), published_utc(ev)
            if start is None or pub is None:
                missing += 1
                continue
            hours = (start - pub).total_seconds() / 3600
            by_body.setdefault(ev.get("EventBodyName", "?"), []).append(hours)
        for body, hrs in sorted(by_body.items()):
            hrs.sort()
            rows.append((path, body, len(hrs), min(hrs), hrs[len(hrs) // 10], statistics.median(hrs)))
        rows.append((path, "(no start or published time)", missing, None, None, None))
    print("| file | body | n | min h | p10 h | median h |")
    print("|---|---|---|---|---|---|")
    for r in rows:
        fmt = lambda v: "" if v is None else f"{v:.1f}"
        print(f"| {r[0]} | {r[1]} | {r[2]} | {fmt(r[3])} | {fmt(r[4])} | {fmt(r[5])} |")


if __name__ == "__main__":
    main(sys.argv[1:])

"""Spike S15 (issue #2): do Legistar event items carry enough signal for a keyword classifier?

Throwaway. Standard library only; runs on Windows, macOS and Linux with Python 3.10+.

  python spikes/legistar-items/spike.py collect     # fetch ~8 months of meetings and items, write out/
  (label out/candidates.csv: fill the `relevant` and `private_name` columns with y or n)
  python spikes/legistar-items/spike.py score       # print aggregate results to paste into issue #2

Privacy: item titles can name private individuals (petitioners, owners). Titles are written only to
out/candidates.csv on your own machine (out/ is git-ignored). Nothing printed by `score` or by
`collect --aggregate-only` contains a title.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from collections import Counter
from datetime import date, timedelta
from pathlib import Path

API = "https://webapi.legistar.com/v1"
CLIENTS = ("guilford", "winston-salem")
USER_AGENT = "dc-ledger-spike/0.1 (+https://github.com/TooSmoove/dcledger)"
OUT = Path(__file__).resolve().parent / "out"
PAUSE_SECONDS = 0.5

# Draft rules. "strong" hits would open a review issue; "broad" hits only widen the net for measuring recall.
STRONG_RULES = {
    "data_center": r"\bdata[\s-]*(cent(er|re)s?|processing)\b",
    "datacenter": r"\bdatacent(er|re)s?\b",
    "hyperscale": r"\bhyper[\s-]?scale\b",
    "server_farm": r"\bserver\s+farms?\b",
    "colocation": r"\bco-?location\s+(facilit|center|centre)",
}
BROAD_RULES = {
    "moratorium": r"\bmoratori(um|a)\b",
    "megawatt": r"\bmega[\s-]?watts?\b|\b\d+(\.\d+)?\s?MW\b",
    "substation_power": r"\bsubstations?\b|\bduke energy\b|\btransmission lines?\b",
    "udo_text_amendment": r"\btext amendments?\b|\bunified development ordinance\b|\bUDO\b|\bdevelopment ordinance\b",
    "industrial_zoning": r"\b(zoning|rezon\w*)\b.*\b(industrial|LI|HI|GI|CU-LI|CU-HI)\b",
    "incentive_agreement": r"\b(economic development|performance|incentive)\s+(incentive|agreement|grant)s?\b",
}
CODE_NAME = re.compile(r"\bProject\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b")
CODE_NAME_NOT = {
    "Agreement", "Amendment", "Budget", "Fund", "Funding", "Grant", "Management", "Manager", "Ordinance",
    "Plan", "Update", "Report", "Status", "Review", "Account", "Accounts", "Close", "Closeout", "Cost", "Costs",
}
PERSON_HINT = re.compile(r"\b(petition(er)? of|petitioned by|applicant|property owner|owners?:|request(ed)? by)\b", re.I)


def fetch_json(url: str) -> object:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read()
    time.sleep(PAUSE_SECONDS)
    return json.loads(body)


def events_url(client: str, start: date, end: date) -> str:
    query = urllib.parse.quote(f"EventDate ge datetime'{start.isoformat()}' and EventDate lt datetime'{end.isoformat()}'")
    return f"{API}/{client}/events?$filter={query}"


def item_text(item: dict) -> str:
    parts = ("EventItemTitle", "EventItemMatterName", "EventItemMatterFile", "EventItemMatterType")
    return " | ".join(str(item.get(key) or "") for key in parts)


def classify(text: str) -> tuple[str, list[str]]:
    strong = [name for name, pattern in STRONG_RULES.items() if re.search(pattern, text, re.I)]
    if strong:
        return "strong", strong
    broad = [name for name, pattern in BROAD_RULES.items() if re.search(pattern, text, re.I)]
    names = [m.group(0) for m in CODE_NAME.finditer(text) if m.group(0).split()[1] not in CODE_NAME_NOT]
    if names:
        broad.append("code_name")
    return ("broad", broad) if broad else ("none", [])


def collect(args: argparse.Namespace) -> int:
    today = date.today()
    start, end = today - timedelta(days=args.days_back), today + timedelta(days=args.days_ahead)
    summary: dict = {"window": f"{start}..{end}", "clients": {}}
    rows: list[dict] = []

    for client in CLIENTS:
        events = fetch_json(events_url(client, start, end))
        stats = {
            "events": len(events),
            "events_by_body": Counter(),
            "items": 0,
            "items_by_tier": Counter(),
            "rule_hits": Counter(),
            "strong_by_body": Counter(),
            "titles_with_person_hint": 0,
            "agenda_status_values": Counter(),
            "minutes_status_values": Counter(),
            "fields_mentioning_cancel": Counter(),
            "item_fields_present": Counter(),
        }
        for event in events:
            body = (event.get("EventBodyName") or "?").strip()
            stats["events_by_body"][body] += 1
            stats["agenda_status_values"][str(event.get("EventAgendaStatusName"))] += 1
            stats["minutes_status_values"][str(event.get("EventMinutesStatusName"))] += 1
            for key, value in event.items():
                if isinstance(value, str) and "cancel" in value.lower():
                    stats["fields_mentioning_cancel"][key] += 1

            items = fetch_json(f"{API}/{client}/events/{event['EventId']}/eventitems")
            for item in items:
                stats["items"] += 1
                stats["item_fields_present"].update(k for k, v in item.items() if v not in (None, ""))
                title = str(item.get("EventItemTitle") or "")
                if PERSON_HINT.search(title):
                    stats["titles_with_person_hint"] += 1
                tier, rules = classify(item_text(item))
                stats["items_by_tier"][tier] += 1
                stats["rule_hits"].update(rules)
                if tier == "strong":
                    stats["strong_by_body"][body] += 1
                if tier != "none":
                    rows.append({
                        "jurisdiction": client,
                        "body": body,
                        "meeting_date": str(event.get("EventDate") or "")[:10],
                        "event_id": event.get("EventId"),
                        "event_item_id": item.get("EventItemId"),
                        "tier": tier,
                        "rules": " ".join(rules),
                        "matter_file": item.get("EventItemMatterFile") or "",
                        "title": " ".join(title.split()),
                        "meeting_url": event.get("EventInSiteURL") or "",
                        "relevant": "",
                        "private_name": "",
                    })
        summary["clients"][client] = {k: (dict(v.most_common()) if isinstance(v, Counter) else v) for k, v in stats.items()}

    summary["candidates"] = {"strong": sum(r["tier"] == "strong" for r in rows), "broad": sum(r["tier"] == "broad" for r in rows)}
    if args.aggregate_only:
        print(json.dumps(summary, indent=2))
        return 0

    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    rows.sort(key=lambda r: (r["tier"] != "strong", r["jurisdiction"], r["meeting_date"]))
    with (OUT / "candidates.csv").open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]) if rows else ["tier"])
        writer.writeheader()
        writer.writerows(rows)
    print(f"Wrote {len(rows)} candidates ({summary['candidates']['strong']} strong) to {OUT / 'candidates.csv'}")
    print("Next: fill `relevant` and `private_name` with y/n for every row, save as CSV, then run `score`.")
    return 0


def read_labels(path: Path) -> list[dict]:
    for encoding in ("utf-8-sig", "cp1252"):
        try:
            with path.open(newline="", encoding=encoding) as handle:
                return list(csv.DictReader(handle))
        except UnicodeDecodeError:
            continue
    raise SystemExit(f"Can't read {path}; save it as CSV (UTF-8).")


def yes(value: str | None) -> bool | None:
    value = (value or "").strip().lower()
    return True if value in ("y", "yes", "1") else False if value in ("n", "no", "0") else None


def score(_: argparse.Namespace) -> int:
    rows = read_labels(OUT / "candidates.csv")
    summary = json.loads((OUT / "summary.json").read_text(encoding="utf-8"))
    unlabeled = sum(yes(r.get("relevant")) is None for r in rows)

    lines = ["## S15 results (aggregates only, no titles)", "", f"Window: {summary['window']}", ""]
    lines += ["| Jurisdiction | Events | Items | Strong hits | Relevant among strong (precision) | Relevant found by strong / all relevant (recall) |", "|---|---|---|---|---|---|"]
    for client in CLIENTS:
        mine = [r for r in rows if r["jurisdiction"] == client and yes(r.get("relevant")) is not None]
        strong = [r for r in mine if r["tier"] == "strong"]
        relevant = [r for r in mine if yes(r["relevant"])]
        tp = sum(yes(r["relevant"]) for r in strong)
        precision = f"{tp}/{len(strong)}" + (f" ({tp / len(strong):.0%})" if strong else "")
        recall = f"{tp}/{len(relevant)}" + (f" ({tp / len(relevant):.0%})" if relevant else "")
        stats = summary["clients"][client]
        lines.append(f"| {client} | {stats['events']} | {stats['items']} | {len(strong)} | {precision} | {recall} |")

    named = [r for r in rows if yes(r.get("private_name")) is not None]
    lines += ["", f"Labeled rows: {len(rows) - unlabeled} of {len(rows)} (unlabeled rows are excluded)."]
    lines += [f"Candidate titles naming a private person: {sum(yes(r['private_name']) for r in named)}/{len(named)}."]
    missed = Counter(rule for r in rows if r["tier"] == "broad" and yes(r.get("relevant")) for rule in r["rules"].split())
    lines += [f"Rules that caught relevant items the strong rules missed: {dict(missed) or 'none'}", ""]
    for client in CLIENTS:
        stats = summary["clients"][client]
        lines += [f"### {client}", "```", json.dumps({k: stats[k] for k in (
            "events_by_body", "strong_by_body", "rule_hits", "titles_with_person_hint",
            "agenda_status_values", "minutes_status_values", "fields_mentioning_cancel")}, indent=2), "```"]
    print("\n".join(lines))
    return 0


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Spike S15: Legistar event items.")
    sub = parser.add_subparsers(dest="command", required=True)
    c = sub.add_parser("collect", help="fetch meetings and items, write out/candidates.csv and out/summary.json")
    c.add_argument("--days-back", type=int, default=183)
    c.add_argument("--days-ahead", type=int, default=60)
    c.add_argument("--aggregate-only", action="store_true", help="print aggregates only; write nothing")
    c.set_defaults(handler=collect)
    s = sub.add_parser("score", help="print aggregate results from the labeled out/candidates.csv")
    s.set_defaults(handler=score)
    args = parser.parse_args(argv)
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

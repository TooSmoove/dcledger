"""Spike S19b (ADR-0008): what Guilford's public Legistar pages show about outcomes and minutes, since the Web API
shows none. Prints Markdown. Run logs are public, so only table headers and cells under known non-personal headers
are printed (dates, case numbers, statuses, actions, results, the acting body, and which links exist). Titles,
names and anything under an unknown header are withheld.
"""

import html
import json
import re
import sys
import time
import urllib.request
from html.parser import HTMLParser

UA = "dc-ledger-spike/0.1 (+https://github.com/TooSmoove/dcledger)"
SAFE = {"file #", "ver.", "agenda #", "type", "status", "action", "result", "action by", "date", "action details",
        "video", "minutes", "agenda", "meeting details", "meeting date", "meeting time", "meeting location",
        "name", "in control", "final action", "on agenda", "published agenda", "published minutes"}
# "Name" is the meeting body on calendar grids; on item grids it is an item name, so it is withheld there (below).
CASES = {"2026-439", "2026-452"}


def fetch(url):
    time.sleep(2)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.status, resp.read(4 * 1024 * 1024).decode("utf-8", "replace")


class Tables(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.tables, self.stack, self.cell, self.links = [], [], None, []

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "table":
            self.stack.append({"id": a.get("id", ""), "rows": []})
        elif tag == "tr" and self.stack:
            self.stack[-1]["rows"].append([])
        elif tag in ("td", "th") and self.stack and self.stack[-1]["rows"]:
            self.cell = {"text": "", "links": []}
        elif tag == "a" and a.get("href"):
            href = html.unescape(a["href"])
            self.links.append(href)
            if self.cell is not None:
                self.cell["links"].append(href)

    def handle_endtag(self, tag):
        if tag in ("td", "th") and self.cell is not None and self.stack and self.stack[-1]["rows"]:
            self.cell["text"] = " ".join(self.cell["text"].split())
            self.stack[-1]["rows"][-1].append(self.cell)
            self.cell = None
        elif tag == "table" and self.stack:
            self.tables.append(self.stack.pop())

    def handle_data(self, data):
        if self.cell is not None:
            self.cell["text"] += data


def kind(href):
    m = re.match(r"(?:https?://[^/]+/)?([A-Za-z]+\.as[hp]x)(?:\?M=([A-Z]+))?", href)
    return f"{m.group(1)}{'?M=' + m.group(2) if m and m.group(2) else ''}" if m else None


def describe(title, url, case_rows_only):
    status, page = fetch(url)
    print(f"## {title}\n\nHTTP {status}, {len(page)} chars.\n")
    for label in ("Minutes status", "Agenda status", "Minutes", "Status", "In control", "Final action", "On agenda"):
        m = re.search(rf">\s*{label}:\s*<.*?<span[^>]*>(.*?)</span>", page, re.S)
        if m:
            print(f"- label `{label}:` → `{' '.join(re.sub('<[^>]+>', ' ', m.group(1)).split())[:80]}`")
    p = Tables()
    p.feed(page)
    kinds = {}
    for href in p.links:
        k = kind(href)
        if k:
            kinds[k] = kinds.get(k, 0) + 1
    print(f"- link kinds on page: {json.dumps(dict(sorted(kinds.items())))}\n")
    for t in p.tables:
        rows = [r for r in t["rows"] if r]
        if len(rows) < 2 or not t["id"]:
            continue
        header = [c["text"].lower() for c in rows[0]]
        if not any(h in SAFE for h in header):
            continue
        item_grid = "file #" in header
        print(f"### table `{t['id']}`: {len(rows) - 1} rows; headers {[c['text'] for c in rows[0]]}\n")
        for r in rows[1:]:
            texts = " ".join(c["text"] for c in r)
            if case_rows_only and not any(c in texts for c in CASES):
                continue
            out = []
            for h, c in zip(header, r):
                ok = h in SAFE and not (item_grid and h == "name")
                links = sorted({kind(l) for l in c["links"] if kind(l)})
                out.append(f"{h}=`{c['text'][:60]}`" + (f" links{links}" if links else "") if ok else f"{h}=(withheld)")
            print("- " + "; ".join(out))
        print()


def minutes_page():
    """Guilford's Board page (Keith, Nov 5): which meetings link minutes, and how those links look."""
    url = "https://guilford.legistar.com/DepartmentDetail.aspx?ID=30411&GUID=B02C5AC9-9E70-4044-A95C-8A9BC5197F35&Mode=MainBody"
    status, page = fetch(url)
    print(f"## Board page (DepartmentDetail, Mode=MainBody)\n\nHTTP {status}, {len(page)} chars.\n")
    p = Tables()
    p.feed(page)
    kinds = {}
    for href in p.links:
        k = kind(href)
        if k:
            kinds[k] = kinds.get(k, 0) + 1
    print(f"- link kinds on page: {json.dumps(dict(sorted(kinds.items())))}\n")
    for t in p.tables:
        rows = [r for r in t["rows"] if r]
        if len(rows) < 2 or not t["id"]:
            continue
        header = [c["text"] for c in rows[0]]
        print(f"### table `{t['id']}`: {len(rows) - 1} rows; headers {header}\n")
        for r in rows[1:]:
            out = []
            for h, c in zip(header, r):
                links = []
                for l in c["links"]:
                    k = kind(l)
                    m = re.search(r"[?&]ID=(\d+)", l)
                    if k:
                        links.append(f"{k}{' ID=' + m.group(1) if m else ''}")
                text = c["text"][:40] if h.lower() in SAFE or h.lower() in ("meeting name", "meeting date") else "(withheld)"
                out.append(f"{h}=`{text}`" + (f" {links}" if links else ""))
            print("- " + "; ".join(out))
        print()


def main():
    print("# Spike S19b — Guilford public pages\n")
    minutes_page()
    ev = json.loads(fetch("https://webapi.legistar.com/v1/guilford/events/1599")[1])
    describe("MeetingDetail for event 1599 (Oct 15), rows for the watched cases", ev["EventInSiteURL"], True)
    describe("LegislationDetail archived for the Oct 15 continuance (PR #82)",
             "https://guilford.legistar.com/LegislationDetail.aspx?ID=8284364&GUID=B62B5B04-4075-414F-A4A1-B10CA85FF96C",
             False)
    return 0


if __name__ == "__main__":
    sys.exit(main())

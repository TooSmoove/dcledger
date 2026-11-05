"""Spike S19d: do Guilford MeetingDetail pages carry the minutes link (View.ashx?M=M) the API never reports, and when
was it posted? Prints ids, link kinds and HTTP headers only."""
import json, re, sys, time, urllib.request, html
UA = "dc-ledger-spike/0.1 (+https://github.com/TooSmoove/dcledger)"
def req(url, method="GET"):
    time.sleep(2)
    r = urllib.request.urlopen(urllib.request.Request(url, method=method, headers={"User-Agent": UA}), timeout=60)
    return r, (r.read(4 * 1024 * 1024) if method == "GET" else b"")
print("# Spike S19d — Guilford minutes on meeting pages\n")
for eid in (1596, 1597, 1626, 1627, 1598, 1599, 1600):
    _, body = req(f"https://webapi.legistar.com/v1/guilford/events/{eid}")
    ev = json.loads(body)
    _, page = req(ev["EventInSiteURL"])
    page = html.unescape(page.decode("utf-8", "replace"))
    web_id = re.search(r"[?&]ID=(\d+)", ev["EventInSiteURL"]).group(1)
    mins = sorted(set(re.findall(r'View\.ashx\?M=M&ID=(\d+)&GUID=([0-9A-Fa-f-]{36})', page)))
    status = re.search(r"Minutes status:\s*<.*?<span[^>]*>(.*?)</span>", page, re.S)
    line = (f"- event {eid} ({ev['EventDate'][:10]}, `{ev['EventBodyName'].strip()}`), web ID {web_id}: "
            f"API minutes file {'set' if ev.get('EventMinutesFile') else 'none'}, API status `{ev.get('EventMinutesStatusName')}`; "
            f"page minutes status `{re.sub('<[^>]+>', '', status.group(1)).strip() if status else '?'}`; page minutes links {[m[0] for m in mins] or 'none'}")
    print(line)
    for mid, guid in mins:
        r, _ = req(f"https://guilford.legistar.com/View.ashx?M=M&ID={mid}&GUID={guid}", "HEAD")
        h = r.headers
        print(f"  - HEAD minutes {mid}: {r.status} {h.get('Content-Type')} {h.get('Content-Length')} bytes, Last-Modified `{h.get('Last-Modified')}`")

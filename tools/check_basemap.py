"""Basemap host checks for publish (SPEC AC13, issue #22).

Two checks, both run by publish's build job before anything is deployed:

  dist  The built site must not mention r2.dev anywhere, and the configured basemap URL must not be on r2.dev
        (r2.dev is rate-limited and was allowed for the skeleton only).
  live  From the site's origin, a ranged GET of nc.pmtiles must return 206 with Access-Control-Allow-Origin, and the
        sprite beside it must be readable cross-origin too (MapLibre fetches tiles, glyphs and sprite with CORS).

Usage:
  python3 -I tools/check_basemap.py dist --dist build/site/dist --basemap-url "$BASEMAP_URL"
  python3 -I tools/check_basemap.py live --basemap-url "$BASEMAP_URL" --origin "$SITE_URL"

Errors are printed as GitHub annotations; the exit code is 1 when any check fails.
"""

from __future__ import annotations

import argparse
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Iterable, Mapping
from pathlib import Path

FORBIDDEN_HOST = "r2.dev"
RANGE = "bytes=0-16383"  # the PMTiles header plus root directory, which is what the map reads first
SPRITE = "sprites/v4/light.json"  # site/src/lib/basemap.js FLAVOR
TIMEOUT = 30


def _on_forbidden_host(host: str) -> bool:
    host = host.lower().rstrip(".")
    return host == FORBIDDEN_HOST or host.endswith("." + FORBIDDEN_HOST)


def check_url(url: str) -> list[str]:
    """Problems with the configured basemap URL itself."""
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or not parsed.hostname or not parsed.path.endswith(".pmtiles"):
        return [f"basemap URL must be an https URL of a .pmtiles file, got {url!r}"]
    if _on_forbidden_host(parsed.hostname):
        return [f"basemap URL is on {FORBIDDEN_HOST} ({parsed.hostname}); use the custom domain (AC13)"]
    return []


def scan_dist(dist: Path) -> list[str]:
    """Files in the build that mention r2.dev. Binary files are scanned as bytes, so nothing slips through."""
    if not dist.is_dir():
        return [f"{dist} is not a directory"]
    needle = FORBIDDEN_HOST.encode()
    found = [
        f"{path.relative_to(dist).as_posix()} mentions {FORBIDDEN_HOST} (AC13)"
        for path in sorted(dist.rglob("*"))
        if path.is_file() and needle in path.read_bytes().lower()
    ]
    return found


def _allows(headers: Mapping[str, str], origin: str) -> bool:
    allowed = headers.get("access-control-allow-origin", "").strip()
    return allowed in ("*", origin)


def check_range_response(status: int, headers: Mapping[str, str], origin: str) -> list[str]:
    problems = []
    if status != 206:
        problems.append(f"ranged GET of the basemap returned {status}, not 206 Partial Content")
    if not _allows(headers, origin):
        got = headers.get("access-control-allow-origin") or "nothing"
        problems.append(f"basemap doesn't allow origin {origin} (Access-Control-Allow-Origin: {got}); set the bucket's CORS policy")
    return problems


def check_sprite_response(status: int, headers: Mapping[str, str], origin: str) -> list[str]:
    problems = []
    if status != 200:
        problems.append(f"GET of the sprite beside the basemap returned {status}, not 200 (run the basemap workflow)")
    elif not _allows(headers, origin):
        problems.append(f"the sprite beside the basemap doesn't allow origin {origin}")
    return problems


def _get(url: str, headers: Mapping[str, str]) -> tuple[int, dict[str, str]]:
    request = urllib.request.Request(url, headers={"User-Agent": "dc-ledger-publish/0.1", **headers})
    try:
        with urllib.request.urlopen(request, timeout=TIMEOUT) as response:
            return response.status, {k.lower(): v for k, v in response.headers.items()}
    except urllib.error.HTTPError as error:
        return error.code, {k.lower(): v for k, v in error.headers.items()}


def check_live(url: str, origin: str) -> list[str]:
    problems = check_url(url) if url.startswith("https:") else []
    if problems:
        return problems
    try:
        status, headers = _get(url, {"Origin": origin, "Range": RANGE})
        problems += check_range_response(status, headers, origin)
        status, headers = _get(urllib.parse.urljoin(url, SPRITE), {"Origin": origin})
        problems += check_sprite_response(status, headers, origin)
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        problems.append(f"couldn't reach the basemap: {error}")
    return problems


def report(problems: Iterable[str]) -> int:
    problems = list(problems)
    for problem in problems:
        print(f"::error title=basemap::{problem}")
    return 1 if problems else 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    dist = sub.add_parser("dist", help="no r2.dev in the build or the configured URL")
    dist.add_argument("--dist", type=Path, required=True)
    dist.add_argument("--basemap-url", required=True)
    live = sub.add_parser("live", help="206 + CORS from the basemap host")
    live.add_argument("--basemap-url", required=True)
    live.add_argument("--origin", required=True)
    args = parser.parse_args(argv)

    if args.command == "dist":
        problems = check_url(args.basemap_url) + scan_dist(args.dist)
        ok = "the build and basemap URL don't mention r2.dev"
    else:
        problems = check_live(args.basemap_url, args.origin.rstrip("/"))
        ok = f"basemap answers ranged, cross-origin requests from {args.origin}"
    code = report(problems)
    if code == 0:
        print(f"OK: {ok}")
    return code


if __name__ == "__main__":
    sys.exit(main())

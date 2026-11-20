"""The map page check for publish (SPEC AC12, WS-AC7; issue #22, follow-up to #95).

/map offers the "Show map" button only when at least one item in the radar's window has a recorded site. Otherwise
it says why there is nothing to map (`mapState` in site/src/lib/radar.js). The page states which case it built on its
heading (`data-map-state`: empty, unsited or sited). This check holds the page to that:

  - the state is one of the three;
  - `sited` exactly when some table row carries data-lat and data-lon;
  - the button is present exactly when the state is `sited`.

So a build that loses the button while items have sites still fails, and a build with nothing to map no longer does.

Usage: python3 -I tools/check_map_page.py build/site/dist/map/index.html
Errors are printed as GitHub annotations; the exit code is 1 when any check fails. Standard library only.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

STATES = ("empty", "unsited", "sited")
STATE = re.compile(r'<h1\b[^>]*\bdata-map-state="([^"]*)"')
LOCATED_ROW = re.compile(r"<tr\b[^>]*\bdata-lat=\"[^\"]+\"[^>]*\bdata-lon=\"[^\"]+\"")
BUTTON = re.compile(r'\bid="show-map"')


def problems(html: str) -> list[str]:
    """Why the built map page breaks its contract, or an empty list."""
    states = STATE.findall(html)
    if len(states) != 1:
        return [f"/map should state its map state once on its heading, found {len(states)}"]
    state = states[0]
    if state not in STATES:
        return [f"/map states an unknown map state {state!r}"]
    found = []
    located = len(LOCATED_ROW.findall(html))
    if (state == "sited") != (located > 0):
        found.append(f"/map says {state!r} but {located} table row(s) have a site")
    has_button = BUTTON.search(html) is not None
    if has_button != (state == "sited"):
        found.append(
            "/map has no map button though items have sites"
            if state == "sited"
            else f"/map offers a map button with nothing to map ({state!r})"
        )
    return found


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print(__doc__, file=sys.stderr)
        return 2
    found = problems(Path(argv[0]).read_text(encoding="utf-8"))
    for message in found:
        print(f"::error title=publish::{message}")
    if not found:
        print("/map: map state matches its table and button.")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

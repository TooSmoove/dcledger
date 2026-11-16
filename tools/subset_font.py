"""Builds the site's one web font (issue #132, item 4): EB Garamond, roman, subset to Latin and the punctuation the site
uses, as one variable WOFF2 (weights 400 to 700). Also prints the metric overrides for the fallback face, so the CSS
values trace to this script's output.

The source is the upstream variable TTF from google/fonts at a pinned commit, checked against its SHA-256 before
anything is built from it. The output is committed (site/src/fonts/), so builds never fetch it, and the license report
checks its hash (tools/license-policy.json, "vendored").

Usage (needs fonttools 4.60.1 and brotli 1.1.0, for example in a venv; the network only for the download):
  python3 tools/subset_font.py --out site/src/fonts \
      --fallback /usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf
  python3 tools/subset_font.py --source EBGaramond[wght].ttf --license OFL.txt --out site/src/fonts --fallback ...

Liberation Serif has Times New Roman's metrics, the serif every supported desktop has, so the overrides fit both.
"""

from __future__ import annotations

import argparse
import hashlib
import shutil
import sys
import tempfile
import urllib.request
from pathlib import Path

COMMIT = "bd3358527f028881c7cc1158aa4c9a5063e94465"  # google/fonts main on Nov 16, 2026
BASE_URL = f"https://raw.githubusercontent.com/google/fonts/{COMMIT}/ofl/ebgaramond"
SOURCE_URL = f"{BASE_URL}/EBGaramond%5Bwght%5D.ttf"
SOURCE_SHA256 = "ef9512f92f6d579e5dc75af59a5a4b1b8b47d2eda89e00b954d44520e5369027"
LICENSE_URL = f"{BASE_URL}/OFL.txt"
LICENSE_SHA256 = "0985066662eb755ed3683ae5482a81a9195b49ce3f7e165cc2388b3dbece7dd7"

OUTPUT_NAME = "eb-garamond-latin-wght.woff2"
LICENSE_NAME = "OFL.txt"
WEIGHTS = (400, 700)  # body 400, headings 500, strong 700

# Basic Latin and Latin-1 (names with accents), plus the typographic punctuation and arrows the pages and records use
# (found by scanning dist/, data/ and the published repository documents): dashes, curly quotes, ellipsis, bullet,
# arrows, primes, minus, thin and narrow no-break spaces. Anything else falls back to the system serif.
UNICODE_RANGES = (
    (0x0020, 0x007E),
    (0x00A0, 0x00FF),
    (0x2009, 0x2009),
    (0x2013, 0x2014),
    (0x2018, 0x2019),
    (0x201C, 0x201D),
    (0x2022, 0x2022),
    (0x2026, 0x2026),
    (0x202F, 0x202F),
    (0x2032, 0x2033),
    (0x2190, 0x2193),
    (0x2212, 0x2212),
)
LAYOUT_FEATURES = ("kern", "liga", "lnum", "tnum", "pnum", "onum")

# Relative frequency of lowercase letters and space in English prose, for the average-width comparison (the same
# method Capsize and next/font use). Close enough for headings and body copy; the goal is less shift, not none.
LETTER_WEIGHTS = {
    " ": 0.1874, "e": 0.0960, "t": 0.0702, "a": 0.0621, "o": 0.0584, "i": 0.0522, "n": 0.0521, "h": 0.0487,
    "s": 0.0477, "r": 0.0443, "d": 0.0352, "l": 0.0320, "u": 0.0225, "m": 0.0194, "w": 0.0182, "c": 0.0177,
    "f": 0.0153, "g": 0.0151, "y": 0.0149, "p": 0.0126, "b": 0.0123, "k": 0.0084, "v": 0.0074, "j": 0.0009,
    "x": 0.0008, "q": 0.0006, "z": 0.0005,
}


def unicodes_argument() -> str:
    """The --unicodes value for pyftsubset, in the order of UNICODE_RANGES."""
    parts = []
    for start, end in UNICODE_RANGES:
        parts.append(f"U+{start:04X}" if start == end else f"U+{start:04X}-{end:04X}")
    return ",".join(parts)


def css_unicode_range() -> str:
    """The same ranges as a CSS unicode-range value."""
    return unicodes_argument().replace(",", ", ")


def sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verified(path: Path, expected: str, what: str) -> Path:
    actual = sha256_of(path)
    if actual != expected:
        raise SystemExit(f"{what}: SHA-256 {actual} does not match the pinned {expected}; refusing to build from it.")
    return path


def download(url: str, target: Path) -> Path:
    with urllib.request.urlopen(url, timeout=60) as response:  # noqa: S310 (fixed https URL)
        target.write_bytes(response.read())
    return target


def average_width(font, weights: dict[str, float] = LETTER_WEIGHTS) -> float:
    """Frequency-weighted average advance of lowercase letters and space, in em."""
    cmap = font.getBestCmap()
    metrics = font["hmtx"].metrics
    upm = font["head"].unitsPerEm
    total = sum(metrics[cmap[ord(char)]][0] * weight for char, weight in weights.items())
    return total / sum(weights.values()) / upm


def metric_overrides(target, fallback) -> dict[str, str]:
    """CSS descriptors for a fallback @font-face that occupies the target's space (size, ascent, descent)."""
    size_adjust = average_width(target) / average_width(fallback)
    upm = target["head"].unitsPerEm
    hhea = target["hhea"]

    def percent(value: float) -> str:
        return f"{value * 100:.2f}%"

    return {
        "size-adjust": percent(size_adjust),
        "ascent-override": percent(hhea.ascent / upm / size_adjust),
        "descent-override": percent(abs(hhea.descent) / upm / size_adjust),
        "line-gap-override": percent(hhea.lineGap / upm / size_adjust),
    }


def build(source: Path, out_dir: Path) -> Path:
    from fontTools import subset
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer

    # Keep the source's head.modified, so the same source gives the same bytes (no build timestamp).
    font = TTFont(source, recalcTimestamp=False)
    instancer.instantiateVariableFont(font, {"wght": WEIGHTS}, inplace=True)
    options = subset.Options()
    options.flavor = "woff2"
    options.layout_features = list(LAYOUT_FEATURES)
    options.hinting = False
    options.desubroutinize = True
    options.name_IDs = [0, 1, 2, 3, 4, 5, 6, 13, 14]  # copyright, names, version, license and its URL
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(unicodes=subset.parse_unicodes(unicodes_argument()))
    subsetter.subset(font)
    out_dir.mkdir(parents=True, exist_ok=True)
    output = out_dir / OUTPUT_NAME
    font.flavor = "woff2"
    font.save(output)
    return output


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", type=Path, help="EBGaramond[wght].ttf; downloaded from the pinned commit if left out")
    parser.add_argument("--license", type=Path, help="OFL.txt; downloaded from the pinned commit if left out")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--fallback", type=Path, required=True, help="Liberation Serif Regular (Times metrics)")
    args = parser.parse_args(argv)

    from fontTools.ttLib import TTFont

    with tempfile.TemporaryDirectory() as scratch:
        source = args.source or download(SOURCE_URL, Path(scratch) / "EBGaramond[wght].ttf")
        license_file = args.license or download(LICENSE_URL, Path(scratch) / LICENSE_NAME)
        verified(source, SOURCE_SHA256, "source font")
        verified(license_file, LICENSE_SHA256, "license")
        output = build(source, args.out)
        shutil.copyfile(license_file, args.out / LICENSE_NAME)
        overrides = metric_overrides(TTFont(source), TTFont(args.fallback))

    print(f"{output}: {output.stat().st_size} bytes, sha256 {sha256_of(output)}")
    print(f"unicode-range: {css_unicode_range()};")
    for descriptor, value in overrides.items():
        print(f"{descriptor}: {value};")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

"""Builds the geometry for each jurisdiction's static locator map (issue #132, item 3).

Reads the Census Bureau's cartographic boundary files (generalized from TIGER/Line; U.S. Government works, public
domain): the North Carolina outline, its 100 counties and the places that are jurisdictions here. Projects them to one
fixed frame for the whole state, simplifies them for a map about 40 rem wide, and writes SVG path data to
site/src/geo/nc-boundaries.json, which is committed: the site build reads it and never fetches anything (SPEC §E,
offline builds). The license report checks the file's SHA-256 (tools/license-policy.json, "vendored"); after a
rebuild, update the hash there.

Usage (needs pyshp 2.3.1; the network only for the download, from www2.census.gov):
  python3 tools/build_locators.py --out site/src/geo/nc-boundaries.json
  python3 tools/build_locators.py --input-dir ~/Downloads/cb --out site/src/geo/nc-boundaries.json

A new jurisdiction needs a line in JURISDICTIONS, then a rebuild; tools/test_build_locators.py fails until it has one.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import sys
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path

VINTAGE = "2024"
SCALE = "500k"
STATE_FIPS = "37"
BASE_URL = f"https://www2.census.gov/geo/tiger/GENZ{VINTAGE}/shp"
FILES = {
    "state": f"cb_{VINTAGE}_us_state_{SCALE}.zip",
    "county": f"cb_{VINTAGE}_us_county_{SCALE}.zip",
    "place": f"cb_{VINTAGE}_{STATE_FIPS}_place_{SCALE}.zip",
}

# Each ledger jurisdiction and its Census geography: a county (state + county FIPS) or an incorporated place (state +
# place FIPS). The name is the accessible name's; the ledger's own name is shown elsewhere on the page.
JURISDICTIONS: dict[str, tuple[str, str]] = {
    "chatham": ("county", "37037"),
    "davidson": ("county", "37057"),
    "forsyth": ("county", "37067"),
    "greensboro": ("place", "3728000"),
    "guilford": ("county", "37081"),
    "henderson": ("place", "3730660"),
    "stokes": ("county", "37169"),
    "vance": ("county", "37181"),
    "winston-salem": ("place", "3775000"),
}

# A place too small to see at state scale, and the ledger county it lies in: the locator shades that county lightly
# around the place, so a reader sees where it is (site/src/lib/locator.js). Only places wholly inside one county.
WITHIN: dict[str, str] = {
    "henderson": "vance",
}

# The frame: North Carolina's basemap bounding box (basemap.yml NC_BBOX), equirectangular with longitude scaled by the
# cosine of the middle latitude. Over NC's 2.75 degrees of latitude that keeps shapes within about 2% of true.
WEST, SOUTH, EAST, NORTH = -84.33, 33.84, -75.46, 36.59
WIDTH = 1000
LAT0 = math.radians((SOUTH + NORTH) / 2)
UNITS_PER_DEGREE = WIDTH / ((EAST - WEST) * math.cos(LAT0))
HEIGHT = round((NORTH - SOUTH) * UNITS_PER_DEGREE)
TOLERANCE = 0.6  # simplification tolerance in frame units (1 unit is about 0.8 km)
MIN_RING_AREA = 0.5  # drop slivers and islets smaller than this, in square frame units

Ring = list[tuple[float, float]]
Polygon = list[Ring]  # outer ring, then holes


@dataclass(frozen=True)
class Feature:
    geoid: str
    name: str
    polygons: list[Polygon]  # in longitude and latitude


def project(lon: float, lat: float) -> tuple[float, float]:
    return ((lon - WEST) * math.cos(LAT0) * UNITS_PER_DEGREE, (NORTH - lat) * UNITS_PER_DEGREE)


def simplify(points: Ring, tolerance: float) -> Ring:
    """Douglas-Peucker, iterative, on an open or closed polyline."""
    if len(points) < 3:
        return list(points)
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        start, end = stack.pop()
        (x1, y1), (x2, y2) = points[start], points[end]
        dx, dy = x2 - x1, y2 - y1
        length = math.hypot(dx, dy)
        farthest, index = -1.0, -1
        for i in range(start + 1, end):
            px, py = points[i]
            if length == 0:
                distance = math.hypot(px - x1, py - y1)
            else:
                distance = abs(dy * px - dx * py + x2 * y1 - y2 * x1) / length
            if distance > farthest:
                farthest, index = distance, i
        if farthest > tolerance:
            keep[index] = True
            stack.extend([(start, index), (index, end)])
    return [point for point, kept in zip(points, keep) if kept]


def number(value: float) -> str:
    """One decimal, without a trailing ".0", and never "-0"."""
    text = f"{value:.1f}".removesuffix(".0")
    return "0" if text == "-0" else text


def ring_area(ring: Ring) -> float:
    return abs(sum(x1 * y2 - x2 * y1 for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]))) / 2


def path_data(polygons: list[Polygon]) -> str:
    """SVG path data: projected, simplified, rounded to 0.1 unit, one subpath per ring, holes kept (evenodd fill)."""
    parts = []
    for polygon in polygons:
        for ring in polygon:
            projected = simplify([project(lon, lat) for lon, lat in ring], TOLERANCE)
            if len(projected) < 4 or ring_area(projected) < MIN_RING_AREA:
                continue
            points = [f"{number(x)} {number(y)}" for x, y in projected[:-1]]
            parts.append("M" + "L".join(points) + "Z")
    return "".join(parts)


def bounds(polygons: list[Polygon]) -> list[float]:
    xs, ys = zip(*(project(lon, lat) for polygon in polygons for lon, lat in polygon[0]))
    return [round(min(xs), 1), round(min(ys), 1), round(max(xs), 1), round(max(ys), 1)]


def read_shapefile_zip(data: bytes, geoid_field: str = "GEOID", name_field: str = "NAME") -> list[Feature]:
    import shapefile  # pyshp

    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = archive.namelist()

        def member(extension: str) -> io.BytesIO:
            return io.BytesIO(archive.read(next(n for n in names if n.endswith(extension))))

        reader = shapefile.Reader(shp=member(".shp"), shx=member(".shx"), dbf=member(".dbf"))
        features = []
        for shape_record in reader.iterShapeRecords():
            record = shape_record.record.as_dict()
            features.append(
                Feature(record[geoid_field], record[name_field], polygons_from_shape(shape_record.shape))
            )
        return features


def polygons_from_shape(shape) -> list[Polygon]:
    """Splits a shapefile polygon's parts into polygons: clockwise rings are outer rings, the rest are holes."""
    parts = list(shape.parts) + [len(shape.points)]
    polygons: list[Polygon] = []
    for start, end in zip(parts, parts[1:]):
        ring = [tuple(point) for point in shape.points[start:end]]
        signed = sum(x1 * y2 - x2 * y1 for (x1, y1), (x2, y2) in zip(ring, ring[1:]))
        if signed < 0 or not polygons:  # clockwise in shapefile order: an outer ring
            polygons.append([ring])
        else:
            polygons[-1].append(ring)
    return polygons


def boundaries(state: Feature, counties: list[Feature], places: list[Feature], sources: dict[str, str]) -> dict:
    """The committed file's content: the frame, the state, every county and each jurisdiction's area."""
    by_geoid = {feature.geoid: feature for feature in [*counties, *places]}
    areas = {}
    for jurisdiction_id, (kind, geoid) in sorted(JURISDICTIONS.items()):
        feature = by_geoid.get(geoid)
        if feature is None:
            raise SystemExit(f"{jurisdiction_id}: no {kind} with GEOID {geoid} in the Census files")
        areas[jurisdiction_id] = {
            "kind": kind,
            "geoid": geoid,
            "census_name": feature.name,
            "d": path_data(feature.polygons),
            "bounds": bounds(feature.polygons),
        }
        if jurisdiction_id in WITHIN:
            county = WITHIN[jurisdiction_id]
            if kind != "place" or JURISDICTIONS.get(county, ("",))[0] != "county":
                raise SystemExit(f"{jurisdiction_id}: WITHIN must map a place to a ledger county, not {county!r}")
            areas[jurisdiction_id]["within"] = county
    return {
        "comment": "Generated by tools/build_locators.py (issue #132). Do not edit by hand.",
        "source": {
            "publisher": "U.S. Census Bureau",
            "product": f"Cartographic boundary files {VINTAGE}, 1:{SCALE.removesuffix('k')},000",
            "license": "U.S. Government work, public domain",
            "credit": f"U.S. Census Bureau, cartographic boundary files ({VINTAGE})",
            "files": sources,
        },
        "frame": {"width": WIDTH, "height": HEIGHT, "bbox": [WEST, SOUTH, EAST, NORTH]},
        "state": path_data(state.polygons),
        "counties": "".join(path_data(county.polygons) for county in sorted(counties, key=lambda c: c.geoid)),
        "areas": areas,
    }


def fetch(name: str, input_dir: Path | None) -> bytes:
    if input_dir is not None:
        return (input_dir / name).read_bytes()
    with urllib.request.urlopen(f"{BASE_URL}/{name}", timeout=120) as response:  # noqa: S310 (fixed https URL)
        return response.read()


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--input-dir", type=Path, help="Folder holding the three Census zips; downloaded if left out")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args(argv)

    data = {kind: fetch(name, args.input_dir) for kind, name in FILES.items()}
    sources = {FILES[kind]: hashlib.sha256(content).hexdigest() for kind, content in data.items()}
    state = next(f for f in read_shapefile_zip(data["state"]) if f.geoid == STATE_FIPS)
    counties = [f for f in read_shapefile_zip(data["county"]) if f.geoid.startswith(STATE_FIPS)]
    places = read_shapefile_zip(data["place"])
    if len(counties) != 100:
        raise SystemExit(f"expected North Carolina's 100 counties, found {len(counties)}")

    content = json.dumps(boundaries(state, counties, places, sources), indent=1, sort_keys=True) + "\n"
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(content, encoding="utf-8", newline="\n")
    digest = hashlib.sha256(content.encode("utf-8")).hexdigest()
    print(f"{args.out}: {len(content)} bytes, sha256 {digest} (update tools/license-policy.json)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

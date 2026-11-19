import json
import unittest
from pathlib import Path
from types import SimpleNamespace

import build_locators as bl

REPO = Path(__file__).resolve().parent.parent
BOUNDARIES = REPO / "site" / "src" / "geo" / "nc-boundaries.json"


def square(west: float, south: float, size: float) -> list[tuple[float, float]]:
    """A closed ring, clockwise in longitude and latitude (shapefile order for an outer ring)."""
    return [(west, south), (west, south + size), (west + size, south + size), (west + size, south), (west, south)]


class GeometryTests(unittest.TestCase):
    def test_numbers_drop_a_trailing_zero_and_never_print_minus_zero(self):
        self.assertEqual([bl.number(v) for v in (1.0, 1.25, -0.04, 12.34)], ["1", "1.2", "0", "12.3"])

    def test_simplify_keeps_the_ends_and_drops_points_within_the_tolerance(self):
        line = [(0, 0), (1, 0.1), (2, -0.1), (3, 0), (4, 5), (5, 0)]
        self.assertEqual(bl.simplify(line, 0.5), [(0, 0), (3, 0), (4, 5), (5, 0)])

    def test_the_frame_puts_the_bounding_box_corners_at_the_frame_corners(self):
        self.assertEqual(bl.project(bl.WEST, bl.NORTH), (0.0, 0.0))
        x, y = bl.project(bl.EAST, bl.SOUTH)
        self.assertAlmostEqual(x, bl.WIDTH)
        self.assertAlmostEqual(y, bl.HEIGHT, delta=0.5)

    def test_path_data_closes_each_ring_and_drops_slivers(self):
        d = bl.path_data([[square(-80, 36, 0.2)], [square(-79, 35, 0.0001)]])
        self.assertEqual(d.count("M"), 1)
        self.assertTrue(d.startswith("M") and d.endswith("Z"))

    def test_shapefile_parts_split_into_outer_rings_and_holes(self):
        outer = square(-80, 35, 1)
        hole = list(reversed(square(-79.8, 35.2, 0.2)))
        island = square(-78, 35, 0.5)
        shape = SimpleNamespace(points=outer + hole + island, parts=[0, len(outer), len(outer) + len(hole)])
        polygons = bl.polygons_from_shape(shape)
        self.assertEqual([len(polygon) for polygon in polygons], [2, 1])

    def test_a_jurisdiction_missing_from_the_census_files_stops_the_build(self):
        state = bl.Feature("37", "North Carolina", [[square(-84, 34, 2)]])
        with self.assertRaises(SystemExit):
            bl.boundaries(state, [], [], {})


    def test_a_place_within_a_county_names_it_and_only_a_place_can(self):
        state = bl.Feature("37", "North Carolina", [[square(-84, 34, 2)]])
        counties = [bl.Feature(geoid, geoid, [[square(-80, 35, 0.2)]]) for _, geoid in bl.JURISDICTIONS.values()]
        content = bl.boundaries(state, counties, [], {})
        for jurisdiction_id, county in bl.WITHIN.items():
            self.assertEqual(content["areas"][jurisdiction_id]["within"], county)
        self.assertTrue(all("within" not in a for i, a in content["areas"].items() if i not in bl.WITHIN))
        original = dict(bl.WITHIN)
        try:
            bl.WITHIN["vance"] = "henderson"
            with self.assertRaises(SystemExit):
                bl.boundaries(state, counties, [], {})
        finally:
            bl.WITHIN.clear()
            bl.WITHIN.update(original)


class CoverageTests(unittest.TestCase):
    def test_the_committed_boundaries_carry_every_within(self):
        content = json.loads(BOUNDARIES.read_text(encoding="utf-8"))
        within = {i: a["within"] for i, a in content["areas"].items() if "within" in a}
        self.assertEqual(within, bl.WITHIN, "rebuild site/src/geo/nc-boundaries.json")

    def ledger_ids(self) -> set[str]:
        return {path.stem for path in (REPO / "data" / "jurisdictions").glob("*.yaml")}

    def test_every_ledger_jurisdiction_has_a_census_geography(self):
        self.assertEqual(self.ledger_ids() - set(bl.JURISDICTIONS), set())

    def test_the_committed_boundaries_draw_every_ledger_jurisdiction(self):
        content = json.loads(BOUNDARIES.read_text(encoding="utf-8"))
        missing = sorted(self.ledger_ids() - set(content["areas"]))
        self.assertEqual(
            missing, [], "rebuild site/src/geo/nc-boundaries.json: python3 tools/build_locators.py --out site/src/geo/nc-boundaries.json"
        )

    def test_the_committed_boundaries_are_listed_in_the_license_report(self):
        policy = json.loads((REPO / "tools" / "license-policy.json").read_text(encoding="utf-8"))
        entry = next(e for e in policy["vendored"] if e["path"] == "site/src/geo/nc-boundaries.json")
        self.assertEqual(entry["license"], "LicenseRef-US-Government-Work")


if __name__ == "__main__":
    unittest.main()

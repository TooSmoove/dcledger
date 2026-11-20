import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_map_page  # noqa: E402

BUTTON = '<button type="button" id="show-map" data-style="/map-style.json">Show map</button>'
LOCATED = '<tr data-lat="36.1495" data-lon="-79.8711"><td>…</td></tr>'
UNLOCATED = "<tr><td>…</td></tr>"


def page(state: str, *, button: bool, rows: str) -> str:
    return f'<main><h1 data-map-state="{state}">Map</h1>{BUTTON if button else ""}<table>{rows}</table></main>'


class ProblemsTests(unittest.TestCase):
    def test_each_consistent_state_passes(self):
        cases = {
            "empty": page("empty", button=False, rows="<tr><td>No items.</td></tr>"),
            "unsited": page("unsited", button=False, rows=UNLOCATED * 2),
            "sited": page("sited", button=True, rows=LOCATED + UNLOCATED),
        }
        for name, html in cases.items():
            with self.subTest(name):
                self.assertEqual(check_map_page.problems(html), [])

    def test_a_lost_button_with_sites_fails(self):
        found = check_map_page.problems(page("sited", button=False, rows=LOCATED))
        self.assertEqual(found, ["/map has no map button though items have sites"])

    def test_a_button_with_nothing_to_map_fails(self):
        for state, rows in (("empty", ""), ("unsited", UNLOCATED)):
            with self.subTest(state):
                found = check_map_page.problems(page(state, button=True, rows=rows))
                self.assertEqual(found, [f"/map offers a map button with nothing to map ({state!r})"])

    def test_a_state_that_disagrees_with_the_table_fails(self):
        found = check_map_page.problems(page("unsited", button=False, rows=LOCATED))
        self.assertIn("/map says 'unsited' but 1 table row(s) have a site", found)
        found = check_map_page.problems(page("sited", button=True, rows=UNLOCATED))
        self.assertIn("/map says 'sited' but 0 table row(s) have a site", found)

    def test_a_missing_duplicated_or_unknown_state_fails(self):
        self.assertIn("found 0", check_map_page.problems("<h1>Map</h1>")[0])
        twice = '<h1 data-map-state="empty">Map</h1><h1 data-map-state="empty">Map</h1>'
        self.assertIn("found 2", check_map_page.problems(twice)[0])
        self.assertIn("unknown map state 'busy'", check_map_page.problems('<h1 data-map-state="busy">Map</h1>')[0])

    def test_main_reports_annotations_and_exit_codes(self):
        with tempfile.TemporaryDirectory() as tmp:
            good = Path(tmp, "good.html")
            good.write_text(page("empty", button=False, rows=""), encoding="utf-8")
            bad = Path(tmp, "bad.html")
            bad.write_text(page("sited", button=False, rows=LOCATED), encoding="utf-8")
            self.assertEqual(check_map_page.main([str(good)]), 0)
            self.assertEqual(check_map_page.main([str(bad)]), 1)
            self.assertEqual(check_map_page.main([]), 2)


if __name__ == "__main__":
    unittest.main()

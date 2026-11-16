import tempfile
import unittest
from pathlib import Path

from subset_font import UNICODE_RANGES, css_unicode_range, unicodes_argument, verified


class SubsetFontTests(unittest.TestCase):
    def test_ranges_cover_basic_latin_latin1_and_the_typographic_punctuation_in_use(self):
        covered = {code for start, end in UNICODE_RANGES for code in range(start, end + 1)}
        for char in "Az09 .,;:!?'\"()-§©°é’‘“”–—…←→·•":
            self.assertIn(ord(char), covered, char)

    def test_ranges_are_sorted_and_disjoint(self):
        for (_, end), (start, _) in zip(UNICODE_RANGES, UNICODE_RANGES[1:]):
            self.assertLess(end, start)

    def test_css_and_pyftsubset_forms_agree(self):
        self.assertEqual(css_unicode_range().replace(", ", ","), unicodes_argument())
        self.assertTrue(unicodes_argument().startswith("U+0020-007E,U+00A0-00FF,"))

    def test_a_source_with_another_hash_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "font.ttf")
            path.write_bytes(b"not the pinned font")
            with self.assertRaises(SystemExit):
                verified(path, "0" * 64, "source font")


if __name__ == "__main__":
    unittest.main()

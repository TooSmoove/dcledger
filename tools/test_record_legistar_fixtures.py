import unittest
from datetime import date

from record_legistar_fixtures import SCRUBBED, events_url, scrub_event, scrub_item, trim_items


class ScrubEventTests(unittest.TestCase):
    def test_structural_fields_are_kept(self):
        event = {"EventId": 1599, "EventBodyName": "Board of Commissioners", "EventDate": "2026-11-05T00:00:00",
                 "EventTime": "5:30 PM", "EventAgendaFile": "https://example.gov/a.pdf"}
        self.assertEqual(scrub_event(event), event)

    def test_comment_keeps_only_the_cancellation_signal(self):
        self.assertEqual(scrub_event({"EventComment": "CANCELLED - see Mr. Smith"})["EventComment"],
                         "Synthetic comment: meeting cancelled")
        self.assertEqual(scrub_event({"EventComment": "Call Ms. Jones"})["EventComment"], "Synthetic comment")
        self.assertIsNone(scrub_event({"EventComment": None})["EventComment"])

    def test_unknown_text_is_scrubbed_but_types_survive(self):
        scrubbed = scrub_event({"EventNewField": "Jane Doe", "EventCount": 3, "EventFlag": True, "EventNested": {"a": "b"}})
        self.assertEqual(scrubbed, {"EventNewField": SCRUBBED, "EventCount": 3, "EventFlag": True, "EventNested": {"a": SCRUBBED}})


class ScrubItemTests(unittest.TestCase):
    def test_title_and_people_are_replaced(self):
        item = {"EventItemId": 7, "EventItemTitle": "Rezoning petition of John Q. Public", "EventItemMover": "Commissioner X",
                "EventItemMatterName": "Public rezoning", "EventItemMatterFile": "2026-339", "EventItemMatterType": "Public Hearing"}
        scrubbed = scrub_item(item, 4)
        self.assertEqual(scrubbed["EventItemId"], 7)
        self.assertEqual(scrubbed["EventItemTitle"], "Synthetic agenda item 4")
        self.assertEqual(scrubbed["EventItemMover"], SCRUBBED)
        self.assertEqual(scrubbed["EventItemMatterName"], "Synthetic matter 4")
        self.assertEqual(scrubbed["EventItemMatterFile"], "2026-339")
        self.assertEqual(scrubbed["EventItemMatterType"], "Public Hearing")
        self.assertNotIn("John", repr(scrubbed))

    def test_classifier_signal_survives_scrubbing(self):
        scrubbed = scrub_item({"EventItemTitle": "Text amendment for Data Centers in LI districts"}, 2)
        self.assertEqual(scrubbed["EventItemTitle"], "Synthetic agenda item 2 about a data center")

    def test_case_number_with_other_characters_is_scrubbed(self):
        self.assertEqual(scrub_item({"EventItemMatterFile": "Smith 2026/1"}, 1)["EventItemMatterFile"], SCRUBBED)


class TrimAndUrlTests(unittest.TestCase):
    def test_trim_keeps_the_first_items_and_every_match(self):
        items = [{"EventItemTitle": f"item {n}"} for n in range(8)] + [{"EventItemTitle": "hyperscale campus"}]
        kept = trim_items(items, keep=3)
        self.assertEqual([i["EventItemTitle"] for i in kept], ["item 0", "item 1", "item 2", "hyperscale campus"])

    def test_events_url_filters_by_date(self):
        url = events_url("guilford", date(2026, 10, 1), date(2026, 12, 1))
        self.assertTrue(url.startswith("https://webapi.legistar.com/v1/guilford/events?$filter="))
        self.assertIn("datetime%272026-10-01%27", url)
        self.assertIn("datetime%272026-12-01%27", url)


if __name__ == "__main__":
    unittest.main()

import tempfile
import unittest
from pathlib import Path

from render_thumbnail import (
    SANDBOX_POLICY,
    Refusal,
    check_may_render,
    chrome_arguments,
    read_record,
    sandboxed,
    sha256_file,
    thumbnail_block,
)

SHA = "a" * 64


def record(**overrides: str) -> dict[str, str]:
    base = {"source_type": "official", "kind": "minutes", "media_type": "application/pdf"}
    base.update(overrides)
    return {key: value for key, value in base.items() if value}


class RecordTests(unittest.TestCase):
    def test_a_file_whose_hash_is_not_an_archived_document_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaisesRegex(Refusal, "isn't an archived document"):
                read_record(Path(tmp), SHA)

    def test_the_record_s_flat_keys_are_read_quoted_or_bare_and_nested_ones_skipped(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp, "documents")
            folder.mkdir()
            (folder / f"{SHA}.yaml").write_text(
                f'id: {SHA}\nmedia_type: "text/html"\nsource_type: official  # comment\nkind: legislation\n'
                "thumbnail:\n  width: 240\n",
                encoding="utf-8",
            )
            values = read_record(Path(tmp), SHA)
        self.assertEqual(values["media_type"], "text/html")
        self.assertEqual(values["source_type"], "official")
        self.assertEqual(values["kind"], "legislation")
        self.assertNotIn("width", values)

    def test_sha256_is_of_the_file_s_bytes(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp, "empty")
            path.write_bytes(b"")
            self.assertEqual(sha256_file(path), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")


class MayRenderTests(unittest.TestCase):
    def test_official_minutes_and_legislation_in_pdf_or_html_are_rendered(self):
        self.assertEqual(check_may_render(record()), "application/pdf")
        self.assertEqual(check_may_render(record(kind="legislation", media_type="text/html; charset=utf-8")), "text/html")

    def test_agenda_packets_other_kinds_and_unknown_kind_are_refused(self):
        for kind in ("agenda_packet", "agenda", "notice", "news_release", "other", ""):
            with self.subTest(kind=kind), self.assertRaisesRegex(Refusal, "only minutes and legislation"):
                check_may_render(record(kind=kind))

    def test_news_is_refused(self):
        with self.assertRaisesRegex(Refusal, "only official documents"):
            check_may_render(record(source_type="news"))

    def test_other_media_types_are_refused(self):
        with self.assertRaisesRegex(Refusal, "can't render media type"):
            check_may_render(record(media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document"))


class SandboxTests(unittest.TestCase):
    def test_the_policy_follows_the_doctype(self):
        page = sandboxed(b"<!DOCTYPE html><html><script>x()</script>")
        self.assertTrue(page.startswith(b"<!DOCTYPE html>" + SANDBOX_POLICY))

    def test_without_a_doctype_the_policy_comes_first(self):
        self.assertTrue(sandboxed(b"<html><body>").startswith(SANDBOX_POLICY))

    def test_the_policy_allows_no_script_and_no_request(self):
        policy = SANDBOX_POLICY.decode("ascii")
        self.assertIn("default-src 'none'", policy)
        self.assertIn("script-src 'none'", policy)
        self.assertNotIn("http:", policy.split("content=")[1])
        self.assertNotIn("https:", policy)

    def test_chrome_sends_every_request_to_a_closed_port_and_resolves_no_host(self):
        arguments = chrome_arguments("chrome", Path("/tmp/page.html").resolve(), Path("/tmp/page.png"))
        self.assertIn("--proxy-server=127.0.0.1:9", arguments)
        self.assertIn("--host-resolver-rules=MAP * ~NOTFOUND", arguments)
        self.assertTrue(arguments[-1].startswith("file://"))


class BlockTests(unittest.TestCase):
    def test_the_printed_block_starts_unreviewed(self):
        block = thumbnail_block("minutes", "b" * 64, 240, 311)
        self.assertIn("kind: minutes\nthumbnail:\n  sha256: " + "b" * 64 + "\n  width: 240\n  height: 311\n", block)
        self.assertIn("  reviewed: false", block)


if __name__ == "__main__":
    unittest.main()

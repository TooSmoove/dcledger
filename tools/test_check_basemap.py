import contextlib
import http.server
import io
import sys
import tempfile
import threading
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_basemap  # noqa: E402

ORIGIN = "https://ncdcledger.org"
GOOD_URL = "https://tiles.ncdcledger.org/basemap/nc.pmtiles"


class CheckUrlTests(unittest.TestCase):
    def test_the_custom_domain_passes(self):
        self.assertEqual(check_basemap.check_url(GOOD_URL), [])

    def test_r2_dev_fails(self):
        for url in ("https://pub-abc123.r2.dev/basemap/nc.pmtiles", "https://R2.DEV./basemap/nc.pmtiles"):
            with self.subTest(url=url):
                self.assertIn("AC13", check_basemap.check_url(url)[0])

    def test_a_lookalike_host_is_not_r2_dev(self):
        self.assertEqual(check_basemap.check_url("https://notr2.dev.example.org/nc.pmtiles"), [])

    def test_non_https_or_non_pmtiles_fails(self):
        for url in ("http://tiles.ncdcledger.org/nc.pmtiles", "https://tiles.ncdcledger.org/nc.mbtiles", ""):
            with self.subTest(url=url):
                self.assertEqual(len(check_basemap.check_url(url)), 1)


class ScanDistTests(unittest.TestCase):
    def test_finds_r2_dev_in_any_file_and_names_it(self):
        with tempfile.TemporaryDirectory() as tmp:
            dist = Path(tmp)
            (dist / "map").mkdir()
            (dist / "index.html").write_text("<p>clean</p>")
            (dist / "map-style.json").write_text('{"url":"pmtiles://https://pub-x.R2.dev/nc.pmtiles"}')
            (dist / "map" / "chunk.js").write_bytes(b"\x00\xffpub-x.r2.dev")
            problems = check_basemap.scan_dist(dist)
        self.assertEqual(len(problems), 2)
        self.assertTrue(problems[0].startswith("map-style.json") or problems[0].startswith("map/chunk.js"))

    def test_a_clean_build_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "index.html").write_text(f"<a href='{GOOD_URL}'>tiles</a>")
            self.assertEqual(check_basemap.scan_dist(Path(tmp)), [])

    def test_a_missing_dist_fails(self):
        self.assertEqual(len(check_basemap.scan_dist(Path("/nonexistent/dist"))), 1)


class ResponseTests(unittest.TestCase):
    def test_206_with_matching_or_wildcard_origin_passes(self):
        for allowed in (ORIGIN, "*"):
            with self.subTest(allowed=allowed):
                self.assertEqual(check_basemap.check_range_response(206, {"access-control-allow-origin": allowed}, ORIGIN), [])

    def test_200_means_range_is_ignored(self):
        problems = check_basemap.check_range_response(200, {"access-control-allow-origin": "*"}, ORIGIN)
        self.assertEqual(len(problems), 1)
        self.assertIn("not 206", problems[0])

    def test_missing_or_other_origin_fails(self):
        for headers in ({}, {"access-control-allow-origin": "https://dcledger.pages.dev"}):
            with self.subTest(headers=headers):
                problems = check_basemap.check_range_response(206, headers, ORIGIN)
                self.assertEqual(len(problems), 1)
                self.assertIn("CORS", problems[0])

    def test_missing_sprite_fails(self):
        self.assertIn("basemap workflow", check_basemap.check_sprite_response(404, {}, ORIGIN)[0])


class _Handler(http.server.BaseHTTPRequestHandler):
    cors = True

    def do_GET(self):  # noqa: N802
        ranged = self.path.endswith(".pmtiles") and self.headers.get("Range")
        self.send_response(206 if ranged else 200 if self.path.endswith(".json") else 404)
        if self.cors and self.headers.get("Origin"):
            self.send_header("Access-Control-Allow-Origin", self.headers["Origin"])
        self.end_headers()

    def log_message(self, *args):
        pass


class LiveTests(unittest.TestCase):
    """check_live against a local server (plain http, so the https URL check is skipped there)."""

    def serve(self, cors: bool):
        handler = type("Handler", (_Handler,), {"cors": cors})
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        return f"http://127.0.0.1:{server.server_port}/basemap/nc.pmtiles"

    def test_a_cors_enabled_host_passes(self):
        self.assertEqual(check_basemap.check_live(self.serve(cors=True), ORIGIN), [])

    def test_a_host_without_cors_fails_both_checks(self):
        self.assertEqual(len(check_basemap.check_live(self.serve(cors=False), ORIGIN)), 2)

    def test_an_unreachable_host_fails(self):
        problems = check_basemap.check_live("http://127.0.0.1:9/basemap/nc.pmtiles", ORIGIN)
        self.assertIn("couldn't reach", problems[0])

    def test_r2_dev_is_refused_before_any_request(self):
        self.assertIn("AC13", check_basemap.check_live("https://pub-x.r2.dev/nc.pmtiles", ORIGIN)[0])


class MainTests(unittest.TestCase):
    def test_dist_command_annotates_and_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / "a.html").write_text("r2.dev")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                code = check_basemap.main(["dist", "--dist", tmp, "--basemap-url", GOOD_URL])
        self.assertEqual(code, 1)
        self.assertIn("::error title=basemap::a.html mentions r2.dev", out.getvalue())


if __name__ == "__main__":
    unittest.main()

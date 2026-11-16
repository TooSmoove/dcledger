"""Renders the first page of an archived document as a small WebP picture for the site (issue #132, item 2).

The picture comes from the very bytes the ledger hashed: the file's SHA-256 must be the ID of a record in
data/documents/, so a picture can't come from another copy. Only official minutes and legislation pages are
rendered (the record's kind says which); agenda packets stay in the private bucket (SPEC §E), and the quality gate
refuses any other picture anyway.

Writes data/thumbnails/{sha256}.webp and prints the `thumbnail:` block to add to the document's record, with
`reviewed: false`. Before the pull request, open the picture and check it for private individuals' names (SPEC §E):
if there are none, set `reviewed: true`; if there are, delete the picture and leave the record without one. The gate
fails a picture whose record isn't marked reviewed (THUMBNAIL_UNREVIEWED).

PDF pages are rendered with pdftoppm (poppler-utils). HTML pages are rendered by headless Chrome with scripts off and
every network request refused, so a saved page can't run code or call anywhere while it is drawn: the copy that is
drawn starts with a Content-Security-Policy that allows no script and no request beyond inline styles and data:
images, and Chrome's proxy points at a closed port. Needs Pillow
(pip install Pillow==12.3.0) to write WebP.

Usage:
  python3 tools/render_thumbnail.py --file ~/Downloads/minutes.pdf --data data
  python3 tools/render_thumbnail.py --file saved-page.html --data data --chrome "$(command -v google-chrome)"
The archived bytes of a fetched document are in the private bucket: the thumbnail workflow downloads and renders
them (Actions -> thumbnail -> Run workflow; "pending" renders every record that needs a picture).
"""

from __future__ import annotations

import argparse
import hashlib
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

WIDTH = 240  # pixels; pages show it at 120 CSS pixels, so it stays sharp at 2x
MAX_HEIGHT = 820  # the gate's cap (DcLedger.Domain.Thumbnail.MaxHeight)
MAX_BYTES = 100 * 1024  # the gate's cap (Thumbnail.MaxBytes)
RENDER_WIDTH = 960  # draw at 4x, then scale down, for legible headings
PAGE_RATIO = 11 / 8.5  # US letter: an HTML page's "first page" is the top of the page at this ratio
SHOWN_KINDS = ("minutes", "legislation")
PDF_TYPES = ("application/pdf",)
HTML_TYPES = ("text/html", "application/xhtml+xml")


class Refusal(Exception):
    """The file can't or mustn't be rendered; the message says why."""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def read_record(data: Path, sha256: str) -> dict[str, str]:
    """The flat top-level keys of data/documents/{sha256}.yaml (quoted or bare scalars); nested mappings are skipped."""
    path = data / "documents" / f"{sha256}.yaml"
    if not path.is_file():
        raise Refusal(
            f"no record {path}: this file's SHA-256 is {sha256}, which isn't an archived document. Render the exact "
            "copy that was archived (archive-document prints its SHA-256), not a new download."
        )
    record = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        match = re.match(r'^([a-z_]+):\s*"?([^"#\n]*?)"?\s*(#.*)?$', line)
        if match and match.group(2):
            record[match.group(1)] = match.group(2)
    return record


def check_may_render(record: dict[str, str]) -> str:
    """The media type to render, after checking the record allows a picture (Thumbnail.MayShow)."""
    if record.get("source_type") != "official":
        raise Refusal("only official documents get a picture; this record's source_type isn't official.")
    kind = record.get("kind")
    if kind not in SHOWN_KINDS:
        raise Refusal(
            f"only minutes and legislation pages get a picture; this record's kind is {kind or 'not set'}. "
            "Set kind first if it is one of those. Agenda packets never get one (SPEC §E)."
        )
    media_type = record.get("media_type", "").split(";")[0].strip().lower()
    if media_type not in PDF_TYPES + HTML_TYPES:
        raise Refusal(f"can't render media type {media_type or 'unknown'}; only PDF and HTML.")
    return media_type


# Scripts off and nothing fetched, set by the page itself so it holds whatever Chrome's flags do. (Chrome's
# scriptEnabled=false setting also stops new headless mode from taking the screenshot, so it can't be used.)
SANDBOX_POLICY = (
    '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; '
    "style-src 'unsafe-inline'; img-src data:; font-src data:; script-src 'none'; frame-src 'none'; "
    "form-action 'none'; base-uri 'none'\">"
).encode("ascii")


def sandboxed(html: bytes) -> bytes:
    """The page with SANDBOX_POLICY as its first element: after the doctype if there is one, else at the very start."""
    match = re.match(rb"\s*(<!doctype[^>]*>)", html, re.IGNORECASE)
    if match:
        return html[: match.end()] + SANDBOX_POLICY + html[match.end() :]
    return SANDBOX_POLICY + html


def chrome_arguments(chrome: str, page: Path, screenshot: Path) -> list[str]:
    """Headless Chrome drawing a local page with every request outside the file refused (scripts: see sandboxed)."""
    height = round(RENDER_WIDTH * PAGE_RATIO)
    return [
        chrome,
        "--headless=new",
        "--disable-gpu",
        "--no-first-run",
        "--no-default-browser-check",
        "--hide-scrollbars",
        "--mute-audio",
        "--disable-extensions",
        "--disable-background-networking",
        "--proxy-server=127.0.0.1:9",  # nothing listens there: every http(s) request fails
        "--proxy-bypass-list=<-loopback>",
        "--host-resolver-rules=MAP * ~NOTFOUND",
        f"--window-size={RENDER_WIDTH},{height}",
        f"--screenshot={screenshot}",
        page.as_uri(),
    ]


def render_png(source: Path, media_type: str, scratch: Path, chrome: str | None) -> Path:
    if media_type in PDF_TYPES:
        if shutil.which("pdftoppm") is None:
            raise Refusal("pdftoppm isn't installed (apt install poppler-utils).")
        stem = scratch / "page"
        subprocess.run(
            ["pdftoppm", "-f", "1", "-l", "1", "-singlefile", "-png", "-scale-to-x", str(RENDER_WIDTH),
             "-scale-to-y", "-1", str(source), str(stem)],
            check=True, timeout=120,
        )
        return stem.with_suffix(".png")

    if chrome is None:
        raise Refusal("an HTML page needs --chrome (the path to Chrome or Chromium).")
    page = scratch / "page.html"
    page.write_bytes(sandboxed(source.read_bytes()))
    screenshot = scratch / "page.png"
    arguments = chrome_arguments(chrome, page, screenshot)
    if sys.platform.startswith("linux"):
        arguments.insert(1, "--no-sandbox")  # CI runners and containers have no user namespaces for the sandbox
    subprocess.run(arguments, check=True, timeout=120, capture_output=True)
    return screenshot


def to_webp(png: Path, output: Path) -> tuple[int, int]:
    from PIL import Image  # Pillow; imported here so the rest of this module needs only the standard library

    with Image.open(png) as image:
        page = image.convert("RGB")
    scaled = round(page.height * WIDTH / page.width)
    height = min(scaled, MAX_HEIGHT)  # a page taller than legal size keeps its top
    page = page.resize((WIDTH, scaled), Image.Resampling.LANCZOS).crop((0, 0, WIDTH, height))
    output.parent.mkdir(parents=True, exist_ok=True)
    page.save(output, "WEBP", quality=60, method=6)  # Pillow writes no EXIF or XMP unless asked
    if output.stat().st_size > MAX_BYTES:
        output.unlink()
        raise Refusal(f"the picture came out over {MAX_BYTES} bytes; the gate would refuse it.")
    return WIDTH, height


def thumbnail_block(kind: str, picture_sha256: str, width: int, height: int) -> str:
    return (
        f"kind: {kind}\n"
        "thumbnail:\n"
        f"  sha256: {picture_sha256}\n"
        f"  width: {width}\n"
        f"  height: {height}\n"
        "  reviewed: false   # true once you've checked the picture for private individuals' names (SPEC §E)\n"
    )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--file", type=Path, required=True, help="The archived bytes (the copy whose SHA-256 is the record's ID)")
    parser.add_argument("--data", type=Path, default=Path("data"))
    parser.add_argument("--chrome", help="Chrome or Chromium, for HTML pages")
    args = parser.parse_args(argv)

    try:
        sha256 = sha256_file(args.file)
        record = read_record(args.data, sha256)
        media_type = check_may_render(record)
        output = args.data / "thumbnails" / f"{sha256}.webp"
        with tempfile.TemporaryDirectory() as scratch:
            png = render_png(args.file, media_type, Path(scratch), args.chrome)
            width, height = to_webp(png, output)
    except Refusal as refusal:
        print(f"render_thumbnail: {refusal}", file=sys.stderr)
        return 2

    print(f"Wrote {output} ({output.stat().st_size} bytes). Add to data/documents/{sha256}.yaml, replacing any kind line:")
    print()
    print(thumbnail_block(record["kind"], sha256_file(output), width, height), end="")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

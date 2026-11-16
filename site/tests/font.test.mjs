// The one self-hosted web font (issue #132, item 4): EB Garamond, subset, WOFF2, from this site only, preloaded, with
// font-display: swap and a metric-matched fallback. Runs over the built site.
import assert from 'node:assert/strict';
import { readFile, stat } from 'node:fs/promises';
import { basename, join } from 'node:path';
import { test } from 'node:test';
import { dist, filesUnder, page, pages } from './helpers/dist.mjs';

const FONT_BUDGET_BYTES = 60 * 1024;
const fonts = (await filesUnder(dist, '.woff2')).concat(
  ...(await Promise.all(['.woff', '.ttf', '.otf'].map((ext) => filesUnder(dist, ext)))),
);
const preloads = (html) => [...html.matchAll(/<link rel="preload"[^>]*>/g)].map((m) => m[0]);

test('the build ships exactly one font file, WOFF2, within 60 KB', async () => {
  assert.equal(fonts.length, 1, fonts.join(', '));
  const [font] = fonts;
  assert.match(basename(font), /^eb-garamond-latin-wght\.[\w-]+\.woff2$/);
  const { size } = await stat(font);
  assert.ok(size <= FONT_BUDGET_BYTES, `${size} bytes`);
  // A WOFF2 file starts with the signature "wOF2".
  assert.equal((await readFile(font)).subarray(0, 4).toString('latin1'), 'wOF2');
});

test('every page preloads the one font from this site, and the @font-face rule points at the same file', () => {
  const [font] = fonts;
  const href = `/_astro/${basename(font)}`;
  for (const { name, html } of pages) {
    const links = preloads(html);
    assert.equal(links.length, 1, name);
    assert.ok(links[0].includes(`href="${href}"`), `${name}: ${links[0]}`);
    assert.match(links[0], /as="font"/, name);
    assert.match(links[0], /type="font\/woff2"/, name);
    assert.match(links[0], /crossorigin/, name);
    assert.ok(html.includes(`url(${href})`), `${name} has no @font-face for ${href}`);
  }
});

test('the font face swaps, and the fallback face carries metric overrides', () => {
  const html = page('radar/index.html');
  const faces = [...html.matchAll(/@font-face\s*\{([^}]*)\}/g)].map((m) => m[1]);
  // The minifier may drop the quotes around family names.
  const web = faces.find((face) => /font-family:\s*['"]?EB Garamond Ledger['"]?\s*;/.test(face));
  const fallback = faces.find((face) => /EB Garamond Ledger Fallback/.test(face));
  assert.ok(web, faces.join('\n'));
  assert.match(web, /font-display:\s*swap/);
  assert.match(web, /unicode-range:/);
  assert.ok(fallback, faces.join('\n'));
  for (const descriptor of ['size-adjust', 'ascent-override', 'descent-override']) {
    assert.match(fallback, new RegExp(`${descriptor}:\\s*[\\d.]+%`), descriptor);
  }
  assert.match(fallback, /local\(/);
});

test('no font, stylesheet or preload leaves this site (no third-party requests)', () => {
  for (const { name, html } of pages) {
    for (const link of preloads(html)) {
      assert.doesNotMatch(link, /href="(https?:)?\/\//, name);
    }
    for (const [, url] of html.matchAll(/url\(([^)]*)\)/g)) {
      assert.doesNotMatch(url, /^['"]?(https?:)?\/\//, `${name}: ${url}`);
    }
    assert.doesNotMatch(html, /<link[^>]+rel="stylesheet"[^>]+href="(https?:)?\/\//, name);
  }
});

test('hashed build assets are cached as immutable', async () => {
  const headers = await readFile(join(dist, '_headers'), 'utf8');
  assert.match(headers, /^\/_astro\/\*\n\s+Cache-Control: public, max-age=31536000, immutable$/m);
});

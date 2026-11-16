// First-page pictures of archived documents (issue #132, item 2): a source entry of official minutes or legislation
// shows the reviewed picture of the exact copy whose SHA-256 it prints, lazy-loaded at a fixed size; nothing else
// shows one. The fixture's Nov 5 Guilford item lists a legislation page with a picture.
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { test } from 'node:test';
import { pageOne } from '../src/lib/thumbnails.js';
import { dist, page, pages } from './helpers/dist.mjs';

const fixture = JSON.parse(
  await readFile(new URL('./fixtures/events.json', import.meta.url), 'utf8'),
);
const pictured = [...fixture.events, ...fixture.actions]
  .flatMap((item) => item.documents)
  .filter((document) => document.thumbnail !== null);
const hash = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';
const thumbnail = { path: `/thumbnails/${hash}.webp`, width: 240, height: 311 };

test('a picture is shown at half its pixel size, with alt text naming the hash', () => {
  assert.deepEqual(pageOne({ sha256: hash, kind: 'minutes', thumbnail }), {
    src: `/thumbnails/${hash}.webp`,
    width: 120,
    height: 156,
    alt: 'Page 1 of the archived copy with SHA-256 e3b0c44298fc…',
    caption: 'Page 1 of the copy with SHA-256 e3b0c442…',
  });
  assert.notEqual(pageOne({ sha256: hash, kind: 'legislation', thumbnail }), null);
});

test('no picture for an agenda packet, any other kind, an unknown kind or no thumbnail, whatever the data says', () => {
  for (const kind of [
    'agenda_packet',
    'agenda',
    'notice',
    'news_release',
    'news_article',
    'other',
    null,
  ]) {
    assert.equal(pageOne({ sha256: hash, kind, thumbnail }), null, String(kind));
  }
  assert.equal(pageOne({ sha256: hash, kind: 'minutes', thumbnail: null }), null);
});

test('the item page shows the picture of the exact copy whose hash its source entry prints', () => {
  const html = page('items/fixture-guilford-boc-2026-11-05/index.html');
  const entries = [...html.matchAll(/<li class="has-page">([\s\S]*?)<\/li>/g)].map((m) => m[1]);
  assert.equal(entries.length, 1, 'one source entry with a picture');
  const [entry] = entries;
  const img = entry.match(/<img ([^>]*)>/)[1];
  const src = img.match(/src="([^"]+)"/)[1];
  const printed = entry.match(/<code>([0-9a-f]{64})<\/code>/)[1];
  assert.equal(src, `/thumbnails/${printed}.webp`);
  assert.ok(existsSync(join(dist, src)), `${src} is in dist`);
  assert.match(img, /width="120"/);
  assert.match(img, /height="156"/);
  assert.match(img, /loading="lazy"/);
  assert.match(img, /decoding="async"/);
  assert.match(
    img,
    new RegExp(`alt="Page 1 of the archived copy with SHA-256 ${printed.slice(0, 12)}…"`),
  );
  assert.match(
    entry,
    new RegExp(`<figcaption>Page 1 of the copy with SHA-256 ${printed.slice(0, 8)}…</figcaption>`),
  );
});

test('the deployed picture is the fixture export’s file, byte for byte', async () => {
  const deployed = await readFile(join(dist, 'thumbnails', `${hash}.webp`));
  const exported = await readFile(
    new URL(`./fixtures/public/thumbnails/${hash}.webp`, import.meta.url),
  );
  assert.ok(deployed.equals(exported));
  assert.equal(deployed.subarray(8, 12).toString('latin1'), 'WEBP');
});

test('only sources of minutes or legislation carry a picture, on every page', () => {
  for (const document of pictured) {
    assert.ok(['minutes', 'legislation'].includes(document.kind), document.sha256);
  }
  const shown = new Set(pictured.map((document) => document.thumbnail.path));
  for (const { name, html } of pages) {
    for (const [, src] of html.matchAll(/<img [^>]*src="([^"]+)"/g)) {
      assert.ok(shown.has(src), `${name} shows ${src}`);
    }
  }
});

test('a source without a picture renders as before', () => {
  const html = page('items/fixture-escaping-check/index.html');
  assert.doesNotMatch(html, /class="page-one"/);
});

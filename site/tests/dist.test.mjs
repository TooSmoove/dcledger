// Assertions over the built site. Run `npm run fixture && npm run build` first.
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const dist = fileURLToPath(new URL('../dist/', import.meta.url));
const src = fileURLToPath(new URL('../src/', import.meta.url));

if (!existsSync(dist)) {
  throw new Error('site/dist not found: run `npm run fixture && npm run build` before `npm test`.');
}

async function filesUnder(root, extension) {
  const entries = await readdir(root, { recursive: true, withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.endsWith(extension))
    .map((entry) => join(entry.parentPath, entry.name));
}

const pages = await Promise.all(
  (await filesUnder(dist, '.html')).map(async (path) => ({
    path,
    html: await readFile(path, 'utf8'),
  })),
);

test('the build produced the home, radar and about pages', () => {
  const names = pages.map((page) => page.path.slice(dist.length).replaceAll('\\', '/'));
  assert.ok(names.includes('index.html'), names.join(', '));
  assert.ok(names.includes('radar/index.html'), names.join(', '));
  assert.ok(names.includes('about/index.html'), names.join(', '));
});

test('the about page tells site operators how to block the fetcher', () => {
  const about = pages.find((page) => page.path.endsWith(join('about', 'index.html')));
  assert.ok(about, 'about/index.html');
  assert.match(about.html, /User-agent: dc-ledger\s+Disallow: \//);
});

test('every page is marked noindex', () => {
  for (const page of pages) {
    assert.match(page.html, /<meta name="robots" content="noindex"/, page.path);
  }
});

test('no page ships a script tag', () => {
  for (const page of pages) {
    assert.doesNotMatch(page.html, /<script/i, page.path);
  }
});

test('source-derived titles are escaped on the radar', () => {
  const radar = pages.find((page) => page.path.endsWith(join('radar', 'index.html')));
  assert.ok(radar);
  assert.ok(radar.html.includes('&lt;script&gt;alert(1)&lt;/script&gt;'));
  assert.ok(
    radar.html.includes('&amp; &quot;quotes&quot;') || radar.html.includes('&amp; "quotes"'),
  );
  assert.ok(radar.html.includes('Fixture: regular meeting'));
});

test('no component renders raw HTML with set:html', async () => {
  for (const path of await filesUnder(src, '.astro')) {
    assert.doesNotMatch(await readFile(path, 'utf8'), /set:html/, path);
  }
});

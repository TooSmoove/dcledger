// Assertions over the built site (SPEC AC1, AC2, AC4, AC10, AC11, AC12, WS-AC7; issues #21, #22). Run `npm run fixture
// && npm run build` first; build with PUBLIC_BASEMAP_URL set (ci does) to cover the map itself. The fixture's generated_utc is 2026-10-25T21:00:00Z, so its Oct 15 item is before the window, its
// Dec 17 item is beyond it, and the Oct 29 and Nov 5 items are on the radar.
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
    name: path.slice(dist.length).replaceAll('\\', '/'),
    html: await readFile(path, 'utf8'),
  })),
);
const names = pages.map((page) => page.name);
const page = (name) => {
  const found = pages.find((p) => p.name === name);
  assert.ok(found, `${name} (built: ${names.join(', ')})`);
  return found.html;
};
const radar = page('radar/index.html');
const map = page('map/index.html');
const mapStyle = JSON.parse(await readFile(join(dist, 'map-style.json'), 'utf8'));
const hasBasemap = Object.keys(mapStyle.sources).length > 0;
const scriptPages = new Set(['map/index.html']);

test('the build produced the home, radar, about, 404, item and jurisdiction pages', () => {
  for (const expected of [
    'index.html',
    'radar/index.html',
    'about/index.html',
    '404.html',
    'items/fixture-guilford-boc-2026-11-05/index.html',
    'items/fixture-escaping-check/index.html',
    'items/fixture-guilford-boc-2026-10-15/index.html',
    'items/fixture-guilford-boc-2026-12-17/index.html',
    'jurisdictions/guilford/index.html',
    'jurisdictions/fixture-city/index.html',
    'jurisdictions/never-checked/index.html',
    'map/index.html',
  ]) {
    assert.ok(names.includes(expected), `${expected} (built: ${names.join(', ')})`);
  }
});

test('the radar lists only items within 30 days of the export, soonest first (AC1)', () => {
  const rows = radar.match(/<tr[\s>]/g).length - 1; // minus the header row
  assert.equal(rows, 2);
  const cancelled = radar.indexOf('/items/fixture-escaping-check/');
  const regular = radar.indexOf('/items/fixture-guilford-boc-2026-11-05/');
  assert.ok(cancelled > 0 && regular > cancelled, 'Oct 29 before Nov 5');
  assert.doesNotMatch(radar, /fixture-guilford-boc-2026-10-15/, 'before the window');
  assert.doesNotMatch(radar, /fixture-guilford-boc-2026-12-17/, 'beyond the window');
  assert.match(
    radar,
    /as of\s*<time datetime="2026-10-25T21:00:00Z">Sun, Oct 25, 2026, 5:00 PM EDT<\/time>/,
  );
});

test('times are Eastern with their zone, in machine-readable form too (AC2, AC3)', () => {
  assert.match(
    radar,
    /<time datetime="2026-11-05T17:30:00-05:00">Thu, Nov 5, 2026, 5:30 PM EST<\/time>/,
  );
  assert.match(
    radar,
    /<time datetime="2026-10-29T18:00:00-04:00">Thu, Oct 29, 2026, 6:00 PM EDT<\/time>/,
  );
});

test('cancellation is visible as text (AC4)', () => {
  assert.match(
    radar,
    /<tr class="cancelled">[\s\S]*?fixture-escaping-check[\s\S]*?<td>Cancelled<\/td>/,
  );
  assert.match(
    page('items/fixture-escaping-check/index.html'),
    /<h1>Cancelled: Fixture: escaping check/,
  );
  assert.match(page('items/fixture-escaping-check/index.html'), /Cancelled \(revision 1\)/);
});

test('sourcing labels are shown in words (AC6)', () => {
  assert.match(radar, /Confirmed \(official record\)/);
  assert.match(radar, /Reported \(news\)/);
});

test('every item page shows each source URL, retrieval time in Eastern and SHA-256 (AC2)', () => {
  const item = page('items/fixture-escaping-check/index.html');
  assert.match(
    item,
    /href="https:\/\/news.example.org\/fixture-story\?a=1&amp;b=<2>"/,
    'attribute escaping',
  );
  assert.match(
    item,
    />https:\/\/news.example.org\/fixture-story\?a=1&amp;b=&lt;2&gt;<\/a>/,
    'text escaping',
  );
  assert.match(item, /href="https:\/\/fixture-city.example.org\/agenda.pdf"/);
  assert.match(
    item,
    /<time datetime="2026-10-10T08:00:00-04:00">Sat, Oct 10, 2026, 8:00 AM EDT<\/time>/,
  );
  assert.match(
    item,
    /<time datetime="2026-10-24T05:05:00-04:00">Sat, Oct 24, 2026, 5:05 AM EDT<\/time>/,
  );
  assert.match(item, /<code>0{64}<\/code>/);
  assert.match(item, /<code>2{64}<\/code>/);
  assert.match(item, /<code>fixture-uid-0002<\/code>/);
  assert.match(item, /case Z-26-01|<dd>Z-26-01<\/dd>/);

  const past = page('items/fixture-guilford-boc-2026-10-15/index.html');
  assert.match(past, /<code>1{64}<\/code>/, 'items outside the window still get a page');
});

test('jurisdiction pages show freshness with a text stale flag and link their feed (AC10)', () => {
  const guilford = page('jurisdictions/guilford/index.html');
  assert.match(
    guilford,
    /Last checked <time datetime="2026-10-25T20:17:00Z">Sun, Oct 25, 2026, 4:17 PM EDT<\/time>/,
  );
  assert.doesNotMatch(guilford, /Stale:/);
  assert.match(guilford, /href="\/radar\/guilford.ics"/);
  assert.match(
    guilford,
    /fixture-guilford-boc-2026-12-17[\s\S]*fixture-guilford-boc-2026-11-05[\s\S]*fixture-guilford-boc-2026-10-15/,
    'newest first',
  );

  const stale = page('jurisdictions/fixture-city/index.html');
  assert.match(stale, /Stale: no successful check in 48 hours/);
  assert.match(page('jurisdictions/never-checked/index.html'), /Never checked\.[\s\S]*Stale:/);
  assert.match(radar, /Stale: no successful check in 48 hours/, 'the radar shows it too');
});

test('the radar links the calendar and CSV, and the build carries them (feeds)', async () => {
  assert.match(radar, /href="\/radar.ics"/);
  assert.match(radar, /href="\/events.csv"/);
  for (const file of ['radar.ics', 'events.csv', 'radar/guilford.ics']) {
    assert.ok(existsSync(join(dist, file)), file);
  }
  assert.match(await readFile(join(dist, 'radar.ics'), 'utf8'), /^BEGIN:VCALENDAR\r\n/);
});

test('the about page tells site operators how to block the fetcher', () => {
  assert.match(page('about/index.html'), /User-agent: dc-ledger\s+Disallow: \//);
});

test('every page is marked noindex, the header file says so too, and there is no sitemap (AC11)', async () => {
  for (const { name, html } of pages) {
    assert.match(html, /<meta name="robots" content="noindex"/, name);
  }
  assert.match(await readFile(join(dist, '_headers'), 'utf8'), /^\/\*\n\s+X-Robots-Tag: noindex\n/);
  const files = (await readdir(dist, { recursive: true })).map((f) => f.replaceAll('\\', '/'));
  assert.equal(
    files.filter((f) => /sitemap/i.test(f) || f === 'robots.txt').length,
    0,
    files.join(', '),
  );
});

test('no page but the map ships a script tag (AC12)', () => {
  for (const { name, html } of pages.filter((p) => !scriptPages.has(p.name))) {
    assert.doesNotMatch(html, /<script/i, name);
  }
});

test('the map page lists the radar items in a table that works without JavaScript (#22, WS-AC7)', () => {
  const rows = map.match(/<tr[\s>]/g).length - 1;
  assert.equal(rows, 2, 'the same 30-day window as /radar');
  assert.match(map, /<th scope="col">Site<\/th>/);
  assert.match(map, /<td>36\.1495° N, 79\.8711° W<\/td>/);
  assert.match(map, /<td>Not recorded<\/td>/);
  assert.match(map, /1 of 2 items have a recorded site/);
});

test('only rows with a location carry coordinates for markers', () => {
  const located = [...map.matchAll(/<tr[^>]*data-lat="([^"]+)"[^>]*data-lon="([^"]+)"/g)];
  assert.deepEqual(
    located.map((m) => [m[1], m[2]]),
    [['36.1495', '-79.8711']],
  );
});

test('the map page loads one small script, and MapLibre only on demand (AC12)', async () => {
  const scripts = [...map.matchAll(/<script\b[^>]*>/gi)].map((m) => m[0]);
  assert.equal(scripts.length, 1, scripts.join(' '));
  const src = scripts[0].match(/\bsrc="([^"]+)"/i)?.[1];
  assert.ok(src, 'a bundled module script, not inline code');
  const loader = await readFile(join(dist, src), 'utf8');
  assert.ok(loader.length < 8 * 1024, `the loader is ${loader.length} bytes`);
  assert.match(loader, /import\(/, 'MapLibre is loaded with a dynamic import()');
  assert.doesNotMatch(loader, /maplibregl-ctrl/, 'MapLibre itself is not in the loader');
  assert.doesNotMatch(map, /rel="stylesheet"/, "MapLibre's CSS is not in the first load");
});

test(
  'with a basemap, the button and style point at the basemap bucket only',
  { skip: !hasBasemap && 'built without PUBLIC_BASEMAP_URL' },
  () => {
    assert.match(map, /<button type="button" id="show-map" data-style="\/map-style.json">/);
    assert.match(
      map,
      /<div id="map" class="map" role="region" aria-label="Map of proposed project sites" hidden/,
    );
    const url = new URL(mapStyle.sources.protomaps.url.replace(/^pmtiles:\/\//, ''));
    const base = `${url.origin}${url.pathname.replace(/[^/]+$/, '')}`;
    assert.ok(mapStyle.glyphs.startsWith(base), mapStyle.glyphs);
    assert.ok(mapStyle.sprite.startsWith(base), mapStyle.sprite);
    assert.ok(mapStyle.layers.length > 10, 'Protomaps layers are included');
    // OpenStreetMap attribution (WS-AC7): on the page, and in the map's own attribution control.
    assert.match(
      map,
      /Map data © <a href="https:\/\/www\.openstreetmap\.org\/copyright">OpenStreetMap contributors<\/a>/,
    );
    assert.match(mapStyle.sources.protomaps.attribution, /© OpenStreetMap contributors/);
  },
);

test(
  'without a basemap, the page says so and offers no button',
  { skip: hasBasemap && 'built with a basemap' },
  () => {
    assert.doesNotMatch(map, /id="show-map"/);
    assert.match(map, /The map isn't available in this build/);
  },
);

test('source-derived text is escaped everywhere it appears', () => {
  for (const name of [
    'radar/index.html',
    'items/fixture-escaping-check/index.html',
    'jurisdictions/fixture-city/index.html',
  ]) {
    const html = page(name);
    assert.ok(html.includes('&lt;script&gt;alert(1)&lt;/script&gt;'), name);
    assert.ok(html.includes('&amp; &quot;quotes&quot;') || html.includes('&amp; "quotes"'), name);
  }
});

test('no component renders raw HTML with set:html', async () => {
  for (const path of await filesUnder(src, '.astro')) {
    assert.doesNotMatch(await readFile(path, 'utf8'), /set:html/, path);
  }
});

test('the home page links the radar', () => {
  assert.match(page('index.html'), /href="\/radar\/"/);
});

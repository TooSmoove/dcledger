// Assertions over the built site (SPEC AC1, AC2, AC4, AC10, AC11, AC12, WS-AC7; issues #21, #22). Run `npm run fixture
// && npm run build` first; build with PUBLIC_BASEMAP_URL set (ci does) to cover the map itself. The fixture's generated_utc is 2026-10-25T21:00:00Z, so its Oct 15 item is before the window, its
// Dec 17 item is beyond it, and the Oct 29 and Nov 5 items are on the radar.
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { hasScriptTag } from '../scripts/quality/static-checks.mjs';

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
    'methodology/index.html',
    '404.html',
    'items/fixture-guilford-boc-2026-11-05/index.html',
    'items/fixture-escaping-check/index.html',
    'items/fixture-guilford-boc-2026-10-15/index.html',
    'items/fixture-guilford-boc-2026-12-17/index.html',
    'jurisdictions/guilford/index.html',
    'jurisdictions/fixture-city/index.html',
    'jurisdictions/never-checked/index.html',
    'jurisdictions/decided-county/index.html',
    'jurisdictions/registered-county/index.html',
    'map/index.html',
    'report/index.html',
    'report/thanks/index.html',
    'data/index.html',
    'changelog/index.html',
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
    />https:\/\/news.example.org\/fixture-story\?a=1&amp;b=&lt;2&gt;<span class="new-tab"/,
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

test('a source a person saved says so in words, beside that source only (#113, AC2)', () => {
  const saved =
    /<dt>How retrieved<\/dt>\s*<dd>Saved by a person in a browser \(the source does not allow automated downloads\)\.<\/dd>/g;
  const item = page('items/fixture-escaping-check/index.html');
  assert.equal(item.match(saved)?.length, 1, 'one of its two sources was saved by hand');
  assert.match(
    item,
    /href="https:\/\/fixture-city.example.org\/agenda.pdf"[\s\S]*?<dt>How retrieved<\/dt>[\s\S]*?<code>2{64}<\/code>/,
    "in the hand-saved agenda's own entry",
  );
  assert.ok(
    item.indexOf('<code>0'.padEnd(70, '0')) < item.indexOf('How retrieved'),
    'the fetched news story, listed first, has no note',
  );

  const city = page('jurisdictions/fixture-city/index.html');
  assert.equal(city.match(saved)?.length, 1, "on the decision's source on a jurisdiction page");

  for (const name of [
    'items/fixture-guilford-boc-2026-11-05/index.html',
    'items/fixture-guilford-boc-2026-10-15/index.html',
    'jurisdictions/guilford/index.html',
    'radar/index.html',
  ]) {
    assert.doesNotMatch(page(name), /How retrieved|Saved by a person/, `${name}: fetched copies`);
  }
});

test('jurisdiction pages show freshness with a text stale flag and link their feed (AC10)', () => {
  const guilford = page('jurisdictions/guilford/index.html');
  assert.match(
    guilford,
    /Last checked <time datetime="2026-10-25T20:17:00Z">Sun, Oct 25, 2026, 4:17 PM EDT<\/time>/,
  );
  assert.doesNotMatch(guilford, /Stale:/);
  assert.match(
    guilford,
    /counts as stale after <time datetime="2026-10-27T20:17:00Z">Tue, Oct 27, 2026, 4:17 PM EDT<\/time>/,
    'a fresh jurisdiction prints its own deadline, so a frozen build still dates itself (#103)',
  );
  assert.match(guilford, /href="\/radar\/guilford.ics"/);
  assert.match(
    guilford,
    /fixture-guilford-boc-2026-12-17[\s\S]*fixture-guilford-boc-2026-11-05[\s\S]*fixture-guilford-boc-2026-10-15/,
    'newest first',
  );

  const stale = page('jurisdictions/fixture-city/index.html');
  assert.match(
    stale,
    /Stale: no successful check in 7 days/,
    'checked by hand: 7 days (issue #27)',
  );
  assert.doesNotMatch(stale, /counts as stale after/, 'no deadline once the flag is up');
  assert.match(
    page('jurisdictions/never-checked/index.html'),
    /Never checked\.[\s\S]*Stale: no successful check in 48 hours/,
  );
  assert.match(radar, /Stale: no successful check in 7 days/, 'the radar shows it too');
  assert.match(
    radar,
    /Stale: no successful check in 48 hours/,
    "with each jurisdiction's own threshold",
  );
});

test('the radar links the calendar, feed and downloads, and the build carries them (feeds)', async () => {
  assert.match(radar, /href="\/radar.ics"/);
  assert.match(radar, /href="\/feed.xml"/);
  assert.match(radar, /href="\/events.csv"/);
  assert.match(radar, /href="\/events.parquet"/);
  assert.match(radar, /href="\/actions.csv"/);
  assert.match(radar, /href="\/actions.parquet"/);
  for (const file of [
    'radar.ics',
    'feed.xml',
    'events.csv',
    'events.parquet',
    'actions.csv',
    'actions.parquet',
    'radar/guilford.ics',
  ]) {
    assert.ok(existsSync(join(dist, file)), file);
  }
  assert.match(await readFile(join(dist, 'radar.ics'), 'utf8'), /^BEGIN:VCALENDAR\r\n/);
  assert.match(
    await readFile(join(dist, 'feed.xml'), 'utf8'),
    /^<\?xml version="1.0" encoding="utf-8"\?>\n<rss version="2.0"/,
  );
  for (const file of ['events.parquet', 'actions.parquet']) {
    assert.equal(
      (await readFile(join(dist, file))).subarray(0, 4).toString('latin1'),
      'PAR1',
      file,
    );
  }
});

test('published page addresses and files never disappear or move (docs/stability.md, #116)', () => {
  // Spelled out, not derived from the build: renaming a page or a file fails here. New ones may be added. A breaking
  // change updates this list, docs/stability.md and CHANGELOG.md together, 90 days after announcing it.
  const frozenPages = [
    'radar/index.html',
    'map/index.html',
    'about/index.html',
    'methodology/index.html',
    'items/fixture-guilford-boc-2026-11-05/index.html',
    'jurisdictions/guilford/index.html',
    'ahead/index.html', // ADR-0010
  ];
  const frozenFiles = [
    'radar.ics',
    'ahead.ics', // ADR-0010
    'ahead/guilford.ics',
    'ahead/decided-county.ics',
    'radar/guilford.ics',
    'feed.xml',
    'events.csv',
    'events.parquet',
    'actions.csv',
    'actions.parquet',
  ];
  assert.deepEqual(
    frozenPages.filter((name) => !names.includes(name)),
    [],
    'pages',
  );
  assert.deepEqual(
    frozenFiles.filter((file) => !existsSync(join(dist, file))),
    [],
    'files',
  );
  assert.match(
    page('jurisdictions/guilford/index.html'),
    /id="decision-fixture-guilford-moratorium-2026-11-05"/,
    'the #decision-{id} anchor',
  );
});

test('the about page links the stability promise, the data dictionary and the changelog on the site (#116)', () => {
  const about = page('about/index.html');
  assert.match(about, /href="\/data\/#stability"/);
  assert.match(about, /href="\/data\/#data-dictionary"/);
  assert.match(about, /href="\/changelog\/"/);
  assert.match(about, /announced at least 90 days ahead/);
});

test('/data/ and /changelog/ carry the repository documents, rewritten for the site (ADR-0009)', () => {
  const data = page('data/index.html');
  assert.match(data, /<h2 id="stability">Stability<\/h2>/);
  assert.match(data, /<h2 id="data-dictionary">Data dictionary<\/h2>/);
  assert.match(data, /<h3 id="vocabularies">Vocabularies<\/h3>/);
  assert.match(data, /at least <strong>90 days<\/strong>/);
  assert.match(data, /href="\/changelog\/"/);
  const changelog = page('changelog/index.html');
  assert.match(changelog, /<h1 id="changelog">Changelog<\/h1>/);
  assert.match(changelog, /href="\/data\/#stability"/);
  for (const [name, html] of [
    ['data', data],
    ['changelog', changelog],
  ]) {
    assert.doesNotMatch(html, /href="[^"]*\.md(?:#[^"]*)?"/, `${name}: a link to a Markdown file`);
    assert.doesNotMatch(html, /\((?:issue |PR )?#\d+\)|\bissue #\d+|\bSPEC\b|ADR-\d/, name);
    assert.doesNotMatch(html, /<!--/, `${name}: a developer comment`);
  }
});

test('no page links to the code repository or GitHub (ADR-0009)', () => {
  for (const { name, html } of pages) {
    assert.doesNotMatch(html, /github\.com/i, name);
  }
});

test('the report form posts to the Function, works without script, and states what happens to a report', () => {
  const form = page('report/index.html');
  assert.match(form, /<form class="report-form" method="post" action="\/api\/report"/);
  for (const value of ['correction', 'question', 'stop_fetching', 'other']) {
    assert.match(
      form,
      new RegExp(`<input type="radio" id="kind-${value}" name="kind" value="${value}"`),
      value,
    );
  }
  assert.match(form, /<textarea id="message" name="message"[^>]*required/);
  assert.match(form, /<input type="email" id="email" name="email"/);
  assert.doesNotMatch(form, /id="email"[^>]*required/, 'email is optional');
  assert.match(form, /<div class="trap" aria-hidden="true">[\s\S]*name="website" tabindex="-1"/);
  assert.match(form, /never published/);
  assert.match(page('report/thanks/index.html'), /within 48 hours/);
});

test('only the report endpoint and the inbox run as Functions; every other path stays static', async () => {
  const routes = JSON.parse(await readFile(join(dist, '_routes.json'), 'utf8'));
  assert.deepEqual(routes, {
    version: 1,
    include: ['/api/report', '/inbox', '/inbox/*'],
    exclude: [],
  });
});

test('every page advertises the feed for readers that discover it', () => {
  for (const file of [
    'radar/index.html',
    'about/index.html',
    'jurisdictions/guilford/index.html',
  ]) {
    assert.match(
      page(file),
      /<link rel="alternate" type="application\/rss\+xml" title="NC Data Center Ledger: Hearing Radar" href="\/feed.xml"/,
      file,
    );
  }
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
    assert.equal(hasScriptTag(html), false, name);
  }
});

test('the map page lists the radar items in a table that works without JavaScript (#22, WS-AC7)', () => {
  const rows = map.match(/<tr[\s>]/g).length - 1;
  assert.equal(rows, 2, 'the same 30-day window as /radar');
  assert.match(map, /<th scope="col">Site<\/th>/);
  assert.match(map, /<td>36\.1495° N, 79\.8711° W<\/td>/);
  assert.match(map, /<td>Not recorded<\/td>/);
  assert.match(map, /1 of 2 items have a recorded site/);
  assert.match(
    map,
    /<h1 data-map-state="sited">Map<\/h1>/,
    'publish reads this (tools/check_map_page.py)',
  );
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

test('a meeting page shows its decision: outcome, vote, date, label and sources (#25, WS-AC8)', () => {
  const meeting = page('items/fixture-guilford-boc-2026-11-05/index.html');
  assert.match(meeting, /<h2 id="decisions-heading">Decision<\/h2>/);
  assert.match(meeting, /Moratorium: Adopted with amendments/);
  assert.match(meeting, /<time datetime="2026-11-05">Thu, Nov 5, 2026<\/time>/);
  assert.match(meeting, /8 in favor, 1 against/);
  assert.match(meeting, /<code>4{64}<\/code>/, "the decision's own source");
  assert.match(
    meeting,
    /<time datetime="2026-11-06T08:00:00-05:00">Fri, Nov 6, 2026, 8:00 AM EST<\/time>/,
  );
  assert.match(
    meeting,
    /href="https:\/\/guilford.legistar.com\/fixture-action\?ID=7&amp;b=<3>"/,
    'attribute escaping',
  );
  assert.match(meeting, /&amp;b=&lt;3&gt;<span class="new-tab"/, 'text escaping');
  assert.doesNotMatch(meeting, /Meeting page/, 'no link from a meeting to itself');
  assert.ok(
    meeting.indexOf('Decision</h2>') < meeting.indexOf('<h2>Sources</h2>'),
    "the decision comes before the meeting's sources",
  );
});

test('meetings without a decision show none', () => {
  for (const name of [
    'items/fixture-guilford-boc-2026-10-15/index.html',
    'items/fixture-escaping-check/index.html',
  ]) {
    assert.doesNotMatch(page(name), /decisions-heading/, name);
  }
});

test('jurisdiction pages list their decisions with sourcing and link the meeting (#25)', () => {
  const guilford = page('jurisdictions/guilford/index.html');
  assert.match(guilford, /Decisions, newest first/);
  assert.match(guilford, /Moratorium: Adopted with amendments/);
  assert.match(guilford, /href="\/items\/fixture-guilford-boc-2026-11-05\/"[^>]*>Meeting page/);
  assert.match(guilford, /Confirmed \(official record\)/);
  assert.doesNotMatch(guilford, /Continued to a later meeting/, 'only its own decisions');
  assert.match(
    guilford,
    /<dt>Vote<\/dt>\s*<dd>Unanimous \(tally not yet recorded\)<\/dd>/,
    'a unanimous vote shows before the tally is posted',
  );

  const city = page('jurisdictions/fixture-city/index.html');
  assert.match(city, /Ordinance text amendment: Continued to a later meeting/);
  assert.match(city, /Reported \(news\)/);
  assert.match(
    city,
    /<code>5{64}<\/code>/,
    'a decision without a meeting page carries its sources here',
  );
  assert.doesNotMatch(city, /<dt>Vote<\/dt>/, 'no vote recorded, no vote shown');
  assert.doesNotMatch(city, /Meeting page/, 'no meeting to link');

  assert.doesNotMatch(page('jurisdictions/never-checked/index.html'), /decisions-heading/);
});

test('each jurisdiction page states its coverage tier in text (#115)', () => {
  assert.match(page('jurisdictions/guilford/index.html'), /Coverage: hearings watched\./);
  assert.match(page('jurisdictions/decided-county/index.html'), /Coverage: decisions only\./);
  assert.match(page('jurisdictions/registered-county/index.html'), /Coverage: listed only\./);
});

test('freshness, the calendar and the meeting table are for watched jurisdictions only (AC10, #115)', () => {
  for (const id of ['decided-county', 'registered-county']) {
    const html = page(`jurisdictions/${id}/index.html`);
    assert.doesNotMatch(html, /Last checked|Never checked|Stale:/, `${id}: no freshness`);
    assert.doesNotMatch(html, /href="\/radar\/[^"]*\.ics"/, `${id}: no radar calendar link`);
    assert.doesNotMatch(html, /<table/, `${id}: no meeting table`);
  }
  assert.match(page('jurisdictions/guilford/index.html'), /<table/);
  // Decisions-only jurisdictions get their first calendar from ADR-0010: dates the record states.
  assert.match(
    page('jurisdictions/decided-county/index.html'),
    /href="\/ahead\/decided-county\.ics"/,
  );
  assert.doesNotMatch(page('jurisdictions/registered-county/index.html'), /\.ics"/);

  const [watchedList, otherList] = radar.split('Other jurisdictions');
  assert.ok(otherList, 'the radar lists the jurisdictions that are not watched');
  assert.doesNotMatch(watchedList, /decided-county|registered-county/);
  assert.match(otherList, /href="\/jurisdictions\/decided-county\/"/);
  assert.match(otherList, /href="\/jurisdictions\/registered-county\/"/);
  assert.doesNotMatch(otherList, /Last checked|Never checked|Stale:/);
});

test("a jurisdiction's note shows as text with its sources, and only where there is one", () => {
  const decided = page('jurisdictions/decided-county/index.html');
  const note = decided.slice(decided.indexOf('id="note-heading"'));
  assert.match(note, /Note on the record/);
  assert.match(note, /Fixture court order: \u201C?&quot;?judgment for the plaintiff/);
  assert.match(note, /&lt;b&gt;not markup&lt;\/b&gt;/, 'the note is text, never HTML');
  assert.match(note, /Not a decision of Decided County\./);
  assert.match(note, /href="https:\/\/news\.example\.org\/fixture-order\.pdf"/);
  assert.match(note, /4c3f0e1d2b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0/);
  assert.ok(
    decided.indexOf('id="note-heading"') < decided.indexOf('id="decisions-heading"'),
    'the note comes before the decisions',
  );
  for (const id of ['guilford', 'fixture-city', 'registered-county']) {
    assert.doesNotMatch(page(`jurisdictions/${id}/index.html`), /note-heading/, `${id}: no note`);
  }
});

test('a decision renders every term its record states, and only those (#115)', () => {
  const guilford = page('jurisdictions/guilford/index.html');
  const moratorium = guilford.slice(
    guilford.indexOf('id="decision-fixture-guilford-moratorium-2026-11-05"'),
  );
  assert.match(moratorium, /<dt>Status<\/dt>\s*<dd>In effect<\/dd>/);
  assert.match(moratorium, /<dt>Ordinance or resolution number<\/dt>\s*<dd>2026-11<\/dd>/);
  assert.match(
    moratorium,
    /<dt>Threshold<\/dt>\s*<dd>\u201Celectrical power demand exceeding 10 megawatts &lt;script&gt;/,
  );
  assert.match(moratorium, /<dt>Covers<\/dt>/);
  assert.match(
    moratorium,
    /<dt>Effective<\/dt>\s*<dd><time datetime="2026-11-05">Thu, Nov 5, 2026<\/time><\/dd>/,
  );
  assert.match(
    moratorium,
    /<dt>Expires<\/dt>\s*<dd><time datetime="2028-05-05">Fri, May 5, 2028<\/time><\/dd>/,
  );
  assert.match(moratorium, /<dt>Ends earlier<\/dt>/);
  assert.match(moratorium, /<dt>Exemptions<\/dt>/);

  const meeting = page('items/fixture-guilford-boc-2026-11-05/index.html');
  assert.match(meeting, /<dt>Expires<\/dt>/, 'the meeting page shows the terms too');

  const city = page('jurisdictions/fixture-city/index.html');
  assert.doesNotMatch(
    city,
    /<dt>(Status|Threshold|Expires)<\/dt>/,
    'no terms recorded, none shown',
  );

  const decided = page('jurisdictions/decided-county/index.html');
  assert.match(decided, /Moratorium: Adopted/, 'a decisions-only jurisdiction lists its decisions');
  assert.match(decided, /<dt>Expires<\/dt>/);
  assert.doesNotMatch(decided, /<dt>Status<\/dt>/, 'one term, one row');
});

test('a superseded decision links the decision that replaced it (#115)', () => {
  const guilford = page('jurisdictions/guilford/index.html');
  const resolution = guilford.slice(
    guilford.indexOf('id="decision-fixture-guilford-resolution-2026-11-04"'),
  );
  assert.match(resolution, /<dt>Status<\/dt>\s*<dd>Replaced or extended by a later decision<\/dd>/);
  assert.match(
    resolution,
    /<dt>Replaced or extended by<\/dt>\s*<dd>\s*<a href="\/jurisdictions\/guilford\/#decision-fixture-guilford-moratorium-2026-11-05">/,
  );
  assert.match(
    guilford,
    /id="decision-fixture-guilford-moratorium-2026-11-05"/,
    'the anchor exists',
  );
});

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

test('Claret: CSS is inlined, the radar draws its window, decisions draw seats, sources draw glyphs (#133)', () => {
  for (const { name, html } of pages) {
    assert.doesNotMatch(html, /rel="stylesheet"/, `${name}: no stylesheet request`);
  }
  assert.match(radar, /<figure class="window" aria-hidden="true">/);
  assert.equal((radar.match(/<ol class="strip">[\s\S]*?<\/ol>/)[0].match(/<li/g) ?? []).length, 31);
  const meeting = page('items/fixture-guilford-boc-2026-11-05/index.html');
  assert.match(meeting, /8 in favor, 1 against<span class="seats" aria-hidden="true"/);
  assert.match(meeting, /<code>4{64}<\/code><span class="glyph" aria-hidden="true">/);
});

test('the home page shows the record at a glance, built from the export (#134)', () => {
  const home = page('index.html');
  assert.match(home, /<h1>Data-center hearings and decisions, on the record<\/h1>/);
  assert.match(home, /<a class="btn" href="\/radar\/">Open the Hearing Radar<\/a>/);
  for (const id of ['next', 'new', 'effect', 'latest', 'coverage', 'data']) {
    assert.match(home, new RegExp(`<h2 id="${id}-heading">`), id);
  }
  const between = (a, b) =>
    home.slice(home.indexOf(`id="${a}-heading"`), home.indexOf(`id="${b}-heading"`));
  const next = between('next', 'new');
  assert.match(next, /<figure class="window" aria-hidden="true">/);
  assert.match(next, /<li class="cancelled">[\s\S]*?Cancelled\./, 'a cancelled meeting says so');
  assert.match(next, /All 2 items in the next 30 days/);
  const effect = between('effect', 'latest');
  assert.match(
    effect,
    /href="\/jurisdictions\/guilford\/#decision-fixture-guilford-moratorium-2026-11-05"/,
  );
  assert.match(effect, /Ends <time datetime="2028-05-05">Fri, May 5, 2028<\/time>/);
  assert.match(effect, /&lt;script&gt;/, 'the threshold is escaped');
  const latest = between('latest', 'coverage');
  assert.equal((latest.match(/<li[ >]/g) ?? []).length, 3, 'three latest decisions');
  assert.match(latest, /8 in favor, 1 against<span class="seats"/);
  const coverage = between('coverage', 'data');
  assert.match(
    coverage,
    /Decisions only, hearings not watched: <a href="\/jurisdictions\/decided-county\/">/,
  );
  assert.match(coverage, /Listed only: <a href="\/jurisdictions\/registered-county\/">/);
  for (const feed of [
    '/feed.xml',
    '/events.csv',
    '/actions.csv',
    '/events.parquet',
    '/actions.parquet',
  ]) {
    assert.match(home, new RegExp(`href="${feed}"`), feed);
  }
});

test('the home page no longer carries "How to check the record" or the iCal links; /faq/ does', () => {
  const home = page('index.html');
  assert.doesNotMatch(home, /trust-heading|class="hash-example"|Check a hash yourself/);
  assert.doesNotMatch(home, /\.ics"/, 'no calendar link on the home page');
  assert.match(home, /<a href="\/faq\/">How to check the record<\/a>/);
  assert.doesNotMatch(
    home,
    /class="note-link"/,
    'no decision on the home page has a noted jurisdiction',
  );

  const faq = page('faq/index.html');
  for (const id of [
    'check-the-record',
    'check-a-hash',
    'labels',
    'coverage',
    'calendar',
    'follow',
    'mistake',
  ]) {
    assert.match(faq, new RegExp(`<h2 id="${id}">`), id);
  }
  assert.match(faq, /<figure class="hash-example"><span class="glyph" aria-hidden="true">/);
  assert.match(faq, /<code>4{64}<\/code>/, "the example hash is the latest decision's source");
  assert.match(faq, /sha256sum downloaded-file/);
  assert.match(
    faq,
    /<code>https:\/\/ncdcledger\.org\/radar\.ics<\/code>/,
    'the address to subscribe with',
  );
  assert.match(faq, /one-time copy that won(’|')t update/);
});

test('every page links the FAQ from its header and footer', () => {
  for (const { name, html } of pages.filter((p) => p.html.includes('<header>'))) {
    assert.match(
      html.slice(html.indexOf('<header>'), html.indexOf('</header>')),
      /href="\/faq\/"/,
      name,
    );
    assert.match(html.slice(html.indexOf('<footer')), /href="\/faq\/"/, name);
  }
});

test('a source tucks its SHA-256 under "More details", still in the page (AC2)', () => {
  const item = page('items/fixture-escaping-check/index.html');
  const sources = item.slice(item.indexOf('<ol class="sources">'));
  assert.equal(
    (sources.match(/<details class="more-details"><summary>More details<\/summary>/g) ?? []).length,
    2,
    'one per source',
  );
  assert.match(
    sources,
    /<details class="more-details"><summary>More details<\/summary><dl><dt>SHA-256<\/dt><dd><code>0{64}<\/code><span class="glyph"/,
  );
  assert.doesNotMatch(item, /<details class="more-details" open/, 'closed by default');
  const visible = sources.replace(/<details class="more-details">[\s\S]*?<\/details>/g, '');
  assert.doesNotMatch(visible, /SHA-256<\/dt>/, 'no SHA-256 row outside the disclosure');
  assert.match(visible, /<dt>Retrieved<\/dt>/, 'the retrieval time stays in view');
});

test('a decision on a jurisdiction with a note points to the note; others do not', () => {
  const decided = page('jurisdictions/decided-county/index.html');
  assert.match(
    decided,
    /<section aria-labelledby="note-heading" class="jurisdiction-note" id="note">/,
  );
  const decision = decided.slice(
    decided.indexOf('id="decision-fixture-decided-moratorium-2026-09-02"'),
  );
  assert.match(
    decision,
    /^[^]*?<\/h3><p class="note-pointer"><strong>Also on the record:<\/strong> a note about Decided County, with its\s+sources\. <a href="#note">Read the note<\/a><\/p>/,
    'right under the heading, before the terms',
  );
  for (const name of [
    'jurisdictions/guilford/index.html',
    'jurisdictions/fixture-city/index.html',
    'items/fixture-guilford-boc-2026-11-05/index.html',
  ]) {
    assert.doesNotMatch(page(name), /class="note-pointer"/, name);
  }
});

test("the home page lists what's new on the record, newest first, from every kind of record", () => {
  const home = page('index.html');
  const recent = home.slice(home.indexOf('id="new-heading"'), home.indexOf('id="effect-heading"'));
  const items = recent.match(/<li>[\s\S]*?<\/li>/g) ?? [];
  assert.equal(items.length, 5, 'five records');
  const dates = items.map((li) => li.match(/<time datetime="([\d-]+)">/)[1]);
  assert.deepEqual(dates, [...dates].sort().reverse(), 'newest first');
  for (const li of items) {
    assert.match(li, /<a class="item" href="\/(items|jurisdictions)\/[^"]+">/);
    assert.match(li, /<span class="muted">(Meeting|Decision|Not a decision of)/);
  }
  assert.match(recent, /Each change is also in the <a href="\/feed.xml">feed<\/a>/);
  assert.match(
    page('index.html'),
    /All 2 items in the next 30 days/,
    'a non-empty window keeps its link',
  );
});

test('every source link opens in a new tab and says so, visibly and in words', () => {
  let count = 0;
  for (const { name, html } of pages) {
    const sources = html.match(/<ol class="sources">[\s\S]*?<\/ol>/g) ?? [];
    for (const list of sources) {
      for (const [link] of list.matchAll(/<dt>URL<\/dt>\s*<dd>\s*<a [\s\S]*?<\/a>/g)) {
        count += 1;
        assert.match(link, /target="_blank" rel="external noopener noreferrer"/, name);
        assert.match(link, /<span class="new-tab" aria-hidden="true">↗<\/span>/, name);
        assert.match(
          link,
          /<span class="visually-hidden"> \(opens in a new tab\)<\/span><\/a>$/,
          name,
        );
      }
    }
  }
  assert.ok(count >= 5, `found ${count} source links`);
});

test('the home page links the radar', () => {
  assert.match(page('index.html'), /href="\/radar\/"/);
});

// /methodology/ (issue #117). The fixture log has two entries: Nov 24 (an issue and three records) and Nov 22 (page
// wording only, no issue).
const methodology = page('methodology/index.html');
const logHtml = methodology.slice(methodology.indexOf('id="log"'));

test('every page links the methodology page from its footer, and about links it too (#117)', () => {
  for (const { name, html } of pages) {
    const footer = html.slice(html.indexOf('<footer'));
    assert.match(footer, /<a href="\/methodology\/">Methodology and corrections<\/a>/, name);
  }
  assert.match(page('about/index.html'), /href="\/methodology\/#corrections"/);
});

test('the methodology page answers how items get in, the labels, provenance, coverage and citing (#117)', () => {
  for (const id of [
    'how-items-get-in',
    'labels',
    'provenance',
    'coverage',
    'publisher',
    'cite',
    'corrections',
  ]) {
    assert.match(methodology, new RegExp(`<h2 id="${id}"`), id);
  }
  assert.match(methodology, /Reported \(news\)/);
  assert.match(methodology, /Confirmed \(official record\)/);
  assert.match(
    methodology,
    /Saved by a person in a browser \(the source does not allow automated downloads\)/,
  );
  assert.match(methodology, /Coverage: decisions only\./);
  assert.match(methodology, /not evidence it did not happen/);
  assert.match(methodology, /at least every 6\s+hours/);
  assert.match(methodology, /within 48 hours/);
});

test('the worked citation names a real item, its source, retrieval time and SHA-256 (#117)', () => {
  const citation = methodology.slice(methodology.indexOf('class="citation"'));
  assert.match(citation, /https:\/\/ncdcledger.org\/items\/fixture-guilford-boc-2026-12-17\//);
  assert.match(citation, /retrieved Sun, Oct 25, 2026, 5:17 PM EDT, SHA-256 3{64}\./);
});

test('the corrections log opens with its date and lists entries newest first (#117)', () => {
  assert.match(logHtml, /<p>Log opened Nov 22, 2026\.<\/p>/);
  const vote = logHtml.indexOf('id="correction-2026-11-24-fixture-guilford-vote-count"');
  const wording = logHtml.indexOf('id="correction-2026-11-22-fixture-methodology-interval"');
  assert.ok(vote > 0 && wording > vote, 'Nov 24 before Nov 22');
  assert.match(logHtml, /<time datetime="2026-11-24">Tue, Nov 24, 2026<\/time>/);
  assert.match(logHtml, /<time datetime="2026-11-23">Mon, Nov 23, 2026<\/time>/);
});

test('a log entry names its report as plain text and links every record it changed (#117)', () => {
  const vote = logHtml.slice(
    0,
    logHtml.indexOf('id="correction-2026-11-22-fixture-methodology-interval"'),
  );
  assert.match(vote, /<\/time> as report R-131\./);
  assert.match(
    vote,
    /href="\/jurisdictions\/guilford\/#decision-fixture-guilford-moratorium-2026-11-05"/,
  );
  assert.match(vote, /href="\/items\/fixture-guilford-boc-2026-11-05\/"/);
  assert.match(vote, /href="\/jurisdictions\/guilford\/"/);
  assert.match(vote, /the &lt;minutes&gt; show 8-1 &amp; one absence/, 'summary text is escaped');
  const wording = logHtml.slice(
    logHtml.indexOf('id="correction-2026-11-22-fixture-methodology-interval"'),
  );
  assert.doesNotMatch(wording.slice(0, wording.indexOf('</li>')), /as report|Changed:/);
});

test('the methodology page, the about page and every footer send reports to the site form (ADR-0009)', () => {
  assert.match(methodology, /To report a mistake, use the <a href="\/report\/">report form<\/a>/);
  assert.match(page('about/index.html'), /href="\/report\/"/);
  for (const { name, html } of pages) {
    assert.match(html.slice(html.indexOf('<footer>')), /<a href="\/report\/">Report it<\/a>/, name);
  }
});

test("the disclosure is the owner's wording, not the placeholder (#117)", () => {
  // Fails on purpose until site/src/owner/disclosure.md is written from docs/wording-bank.md.
  assert.doesNotMatch(
    methodology,
    /OWNER-WORDING-REQUIRED/,
    'Write "Who publishes this" in site/src/owner/disclosure.md (templates: docs/wording-bank.md).',
  );
});

// ADR-0010 (issue #124). The fixture's D is Sun, Oct 25, 2026: the city's Nov 17 next step is in the radar's window,
// Guilford's May 5, 2028 and decided-county's Mar 2, 2027 expiries are later, and decided-county's Oct 1 next step has
// passed with nothing on its record that day.
test('/ahead/ lists the known dates by section, with anchors, flags, sources and no script', () => {
  const ahead = page('ahead/index.html');
  assert.match(ahead, /<h1>Coming up on the record<\/h1>/);
  assert.match(ahead, /Only dates stated in an archived document are listed\./);
  assert.match(ahead, /href="\/ahead\.ics"/);
  const section = (id) => {
    const start = ahead.indexOf(`id="${id}-heading"`);
    assert.ok(start > 0, id);
    const end = ahead.indexOf('<section', start);
    return ahead.slice(start, end === -1 ? undefined : end);
  };
  assert.match(section('next'), /id="fixture-city-text-amendment-2026-10-20-next"/);
  assert.match(section('next'), /Date set for the next step/);
  const later = section('later');
  assert.ok(
    later.indexOf('fixture-decided-moratorium-2026-09-02-expiry') <
      later.indexOf('fixture-guilford-moratorium-2026-11-05-expiry'),
    'soonest first',
  );
  assert.match(later, /&lt;script&gt;/, 'a threshold is escaped');
  const passed = section('passed');
  assert.match(passed, /id="fixture-decided-moratorium-2026-09-02-next"/);
  assert.match(passed, /<p class="flag">Date passed\. Nothing recorded for it yet\.<\/p>/);
  assert.doesNotMatch(ahead, /id="recorded-heading"/, 'no recorded dates in the fixture');
  assert.match(ahead, /SHA-256/, 'rows carry their sources');
  assert.ok(!hasScriptTag(ahead));
  assert.doesNotMatch(ahead, /deadline/i);
  // The dates show their own order, so no section says it (Nov 24, 2026).
  assert.doesNotMatch(ahead, /(Soonest|Newest) first/);
  assert.match(later, /id="later-heading">Later<\/h2>\s*<article/, 'Later has no intro line');
  assert.match(
    section('next'),
    /Through [A-Z][a-z]{2}, [A-Z][a-z]{2} \d{1,2}, \d{4}\.<\/p>/,
    'the 30-day section keeps its end date',
  );
});

test('the header links /ahead/ on every page, and the radar points to it', () => {
  for (const { name, html } of pages.filter((p) => p.html.includes('<header>'))) {
    assert.match(
      html.slice(html.indexOf('<header>'), html.indexOf('</header>')),
      /<a href="\/ahead\/">Coming up<\/a>/,
      name,
    );
  }
  assert.match(radar, /href="\/ahead\/">Coming up on the record<\/a>/);
  assert.match(radar, /Every item has been reviewed and\s+links to its official source\./);
});

test('a decision card shows its next-step date and flags a date passed with nothing recorded', () => {
  const decided = page('jurisdictions/decided-county/index.html');
  assert.match(
    decided,
    /<dt>Date set for the next step<\/dt>\s*<dd>\s*<time datetime="2026-10-01">/,
  );
  assert.match(
    decided,
    /<p class="flag">Date set for the next step, <time datetime="2026-10-01">Thu, Oct 1, 2026<\/time>: Date passed\. Nothing recorded for it yet\.\s*<a href="\/ahead\/#fixture-decided-moratorium-2026-09-02-next">/,
  );
  assert.doesNotMatch(page('jurisdictions/guilford/index.html'), /class="flag">[^<]*Date passed/);
});

test('a jurisdiction with an upcoming known date links /ahead/; FAQ and methodology explain dates ahead', () => {
  assert.match(page('jurisdictions/guilford/index.html'), /href="\/ahead\/guilford\.ics"/);
  assert.match(
    page('jurisdictions/guilford/index.html'),
    /<a href="\/ahead\/">Coming up on the record<\/a>/,
  );
  assert.match(page('faq/index.html'), /<h2 id="dates-ahead">/);
  assert.match(page('faq/index.html'), /<code>https:\/\/ncdcledger\.org\/ahead\.ics<\/code>/);
  assert.match(page('methodology/index.html'), /<h2 id="dates-ahead">Dates ahead<\/h2>/);
});

test('the home page names no known date while the radar window has items', () => {
  assert.doesNotMatch(page('index.html'), /class="known-ahead"/);
});

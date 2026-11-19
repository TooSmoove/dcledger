// Static locator maps (issue #132, item 3): each jurisdiction page draws where it is, as an SVG image with an
// accessible name and a visible credit, and no script. The fixture's guilford is a real ID, so it has an area in
// src/geo/nc-boundaries.json; the fixture-only jurisdictions don't, and show no locator.
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { test } from 'node:test';
import { hasScriptTag } from '../scripts/quality/static-checks.mjs';
import { RING_BELOW, locatorFor, statewideFor } from '../src/lib/locator.js';
import { page, pages } from './helpers/dist.mjs';

const boundaries = JSON.parse(
  await readFile(new URL('../src/geo/nc-boundaries.json', import.meta.url), 'utf8'),
);
const sample = {
  frame: { width: 1000, height: 380 },
  state: 'M0 0L10 0L10 10Z',
  counties: 'M1 1L2 2Z',
  source: { credit: 'U.S. Census Bureau, cartographic boundary files (2024)' },
  areas: {
    county: {
      kind: 'county',
      d: 'M100 100L200 100L200 200Z',
      bounds: [100, 100, 200, 200],
      census_name: 'Sample',
    },
    city: {
      kind: 'place',
      d: 'M500 50L510 50L510 58Z',
      bounds: [500, 50, 510, 58],
      census_name: 'Sample City',
      within: 'county',
    },
    town: { kind: 'place', d: 'M600 50L610 50L610 58Z', bounds: [600, 50, 610, 58] },
    bigcity: {
      kind: 'place',
      d: 'M700 50L760 50L760 98Z',
      bounds: [700, 50, 760, 98],
      within: 'county',
    },
    narrow: { kind: 'county', d: 'M300 100L320 100L320 160Z', bounds: [300, 100, 320, 160] },
    empty: { kind: 'county', d: '', bounds: [0, 0, 0, 0] },
  },
};

test('a locator names the jurisdiction, says what is drawn and credits the Census Bureau', () => {
  const locator = locatorFor(sample, 'county', 'Sample County');
  assert.equal(
    locator.label,
    'Locator map: Sample County highlighted within North Carolina, with county lines.',
  );
  assert.equal(
    locator.credit,
    'Boundaries: U.S. Census Bureau, cartographic boundary files (2024).',
  );
  assert.equal(locator.viewBox, '-6 -6 1012 392');
  assert.equal(locator.ring, null);
});

test('a city is named by its limits and ringed when it is too small to see at state scale', () => {
  const locator = locatorFor(sample, 'town', 'Sample Town');
  assert.match(
    locator.label,
    /^Locator map: Sample Town city limits highlighted within North Carolina/,
  );
  assert.ok(10 < RING_BELOW);
  assert.deepEqual(locator.ring, { cx: 605, cy: 54, r: 21 });
  assert.equal(locator.context, null, 'no county named for it, so none drawn');
});

test('a ringed city inside a ledger county shades that county behind it and names it', () => {
  const locator = locatorFor(sample, 'city', 'Sample City');
  assert.deepEqual(locator.ring, { cx: 505, cy: 54, r: 21 });
  assert.equal(locator.context, sample.areas.county.d);
  assert.equal(
    locator.label,
    'Locator map: Sample City city limits highlighted within Sample County, North Carolina, with county lines.',
  );
});

test('a city big enough to see gets neither a ring nor its county', () => {
  const locator = locatorFor(sample, 'bigcity', 'Big City');
  assert.equal(locator.ring, null);
  assert.equal(locator.context, null);
});

test('a long, narrow county is plain to see and gets no ring: a ring is for areas small both ways', () => {
  const [x0, y0, x1, y1] = sample.areas.narrow.bounds;
  assert.ok(x1 - x0 < RING_BELOW && y1 - y0 >= RING_BELOW);
  assert.equal(locatorFor(sample, 'narrow', 'Narrow County').ring, null);
});

test('the real boundaries: Vance County has no ring; Henderson is ringed, with Vance County behind it', () => {
  const vance = locatorFor(boundaries, 'vance', 'Vance County');
  assert.equal(vance.ring, null);
  assert.equal(vance.context, null);
  const henderson = locatorFor(boundaries, 'henderson', 'Henderson');
  assert.notEqual(henderson.ring, null);
  assert.equal(henderson.context, boundaries.areas.vance.d);
  assert.match(
    henderson.label,
    /Henderson city limits highlighted within Vance County, North Carolina/,
  );
  for (const id of ['greensboro', 'winston-salem']) {
    assert.equal(locatorFor(boundaries, id, id).context, null, id);
  }
});

test('no area, an empty area or an inherited key gives no locator', () => {
  assert.equal(locatorFor(sample, 'missing', 'Missing'), null);
  assert.equal(locatorFor(sample, 'empty', 'Empty'), null);
  assert.equal(locatorFor(sample, 'constructor', 'Constructor'), null);
});

test('the boundaries file states its public-domain Census source and draws only path data', () => {
  assert.equal(boundaries.source.publisher, 'U.S. Census Bureau');
  assert.match(boundaries.source.license, /public domain/);
  assert.match(
    boundaries.source.credit,
    /^U\.S\. Census Bureau, cartographic boundary files \(\d{4}\)$/,
  );
  const pathData = /^(M[-\d. ]+(L[-\d. ]+)*Z)*$/;
  assert.match(boundaries.state, pathData);
  assert.match(boundaries.counties, pathData);
  for (const [id, area] of Object.entries(boundaries.areas)) {
    assert.match(area.d, pathData, id);
    assert.ok(['county', 'place'].includes(area.kind), id);
  }
});

test('a jurisdiction page with an area draws its locator as an image with a name and a credit, and no script', () => {
  const html = page('jurisdictions/guilford/index.html');
  const figure = html.match(/<figure class="locator">([\s\S]*?)<\/figure>/);
  assert.ok(figure, 'no locator on the guilford page');
  const svg = figure[1].match(/<svg ([^>]*)>/)[1];
  assert.match(svg, /role="img"/);
  const titleId = svg.match(/aria-labelledby="([^"]+)"/)[1];
  assert.match(
    figure[1],
    new RegExp(
      `<title id="${titleId}">Locator map: Guilford County highlighted within North Carolina`,
    ),
  );
  assert.match(figure[1], /<path class="locator-area" d="M[^"]+"/);
  assert.match(
    figure[1],
    /<figcaption>Boundaries: U\.S\. Census Bureau, cartographic boundary files \(\d{4}\)\.<\/figcaption>/,
  );
  assert.equal(hasScriptTag(html), false);
});

test('a jurisdiction the boundaries file lacks shows no locator and no empty figure', () => {
  const html = page('jurisdictions/fixture-city/index.html');
  assert.doesNotMatch(html, /class="locator"/);
});

test('every jurisdiction page ships no script, with or without a locator (AC12)', () => {
  for (const { name, html } of pages.filter((p) => p.name.startsWith('jurisdictions/'))) {
    assert.equal(hasScriptTag(html), false, name);
  }
});

test('the statewide map draws counties before places and groups the legend by coverage tier', () => {
  const map = statewideFor(sample, [
    { id: 'city', name: 'Sample City', tier: 'decisions' },
    { id: 'county', name: 'Sample County', tier: 'watched' },
    { id: 'narrow', name: 'Narrow County', tier: 'decisions' },
  ]);
  assert.deepEqual(
    map.areas.map((a) => [a.id, a.style]),
    [
      ['county', 'solid'],
      ['narrow', 'hatched'],
      ['city', 'hatched'],
    ],
  );
  assert.deepEqual(
    map.groups.map((g) => [g.text, g.members.map((m) => m.name)]),
    [
      ['Hearings and decisions', ['Sample County']],
      ['Decisions only', ['Sample City', 'Narrow County']],
    ],
  );
  assert.deepEqual(map.rings, [{ id: 'city', cx: 505, cy: 54, r: 21 }], 'only the speck is ringed');
  assert.equal(
    map.label,
    'Map of North Carolina with county lines, shading the 3 places this ledger covers. The legend lists them.',
  );
  assert.equal(map.credit, 'Boundaries: U.S. Census Bureau, cartographic boundary files (2024).');
  assert.deepEqual(map.missing, []);
});

test('the statewide map leaves off a jurisdiction with no area and says so in missing', () => {
  const map = statewideFor(sample, [
    { id: 'missing', name: 'Missing', tier: 'watched' },
    { id: 'empty', name: 'Empty', tier: 'watched' },
    { id: 'constructor', name: 'Constructor', tier: 'watched' },
  ]);
  assert.deepEqual(map.areas, []);
  assert.deepEqual(map.groups, []);
  assert.deepEqual(map.missing, ['missing', 'empty', 'constructor']);
  assert.equal(map.label, 'Map of North Carolina with county lines.');
});

test('the statewide map refuses a coverage tier it does not know', () => {
  assert.throws(
    () => statewideFor(sample, [{ id: 'county', name: 'Sample County', tier: 'rumored' }]),
    /Unknown coverage tier "rumored"/,
  );
});

test('the real boundaries cover all nine ledger jurisdictions, and only Henderson is ringed', async () => {
  const ids = (await readdir(new URL('../../data/jurisdictions/', import.meta.url)))
    .filter((f) => f.endsWith('.yaml'))
    .map((f) => f.slice(0, -'.yaml'.length));
  const map = statewideFor(
    boundaries,
    ids.map((id) => ({ id, name: id, tier: 'watched' })),
  );
  assert.deepEqual(map.missing, []);
  assert.deepEqual(
    map.rings.map((r) => r.id),
    ['henderson'],
  );
});

test('the home page draws the statewide map as an image with a legend that links each jurisdiction, and no script', () => {
  const html = page('index.html');
  const figure = html.match(/<figure class="statewide">([\s\S]*?)<\/figure>/);
  assert.ok(figure, 'no statewide map on the home page');
  assert.match(figure[1], /<svg [^>]*role="img"[^>]*aria-labelledby="statewide-title"/);
  assert.match(
    figure[1],
    /<title id="statewide-title">Map of North Carolina with county lines, shading the 1 place this ledger covers\./,
    "only the fixture's guilford has a real area",
  );
  assert.match(
    figure[1],
    /<path class="statewide-area statewide-solid statewide-county"[^>]*data-jurisdiction="guilford"/,
  );
  assert.match(
    figure[1],
    /Hearings and decisions:<\/span>\s*<a href="\/jurisdictions\/guilford\/">Guilford County<\/a>/,
  );
  assert.match(
    figure[1],
    /Boundaries: U\.S\. Census Bureau, cartographic boundary files \(\d{4}\)\./,
  );
  assert.doesNotMatch(figure[1], /A ring marks/, 'no ring is drawn, so none is explained');
  assert.equal(hasScriptTag(html), false);
});

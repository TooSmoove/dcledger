// Static locator maps (issue #132, item 3): each jurisdiction page draws where it is, as an SVG image with an
// accessible name and a visible credit, and no script. The fixture's guilford is a real ID, so it has an area in
// src/geo/nc-boundaries.json; the fixture-only jurisdictions don't, and show no locator.
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { test } from 'node:test';
import { hasScriptTag } from '../scripts/quality/static-checks.mjs';
import { SMALL_BELOW, locatorFor, statewideFor } from '../src/lib/locator.js';
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
  assert.equal('ring' in locator, false, 'no locator draws a ring');
});

test('a city is named by its limits; a small one with no county named gets none drawn', () => {
  const locator = locatorFor(sample, 'town', 'Sample Town');
  assert.match(
    locator.label,
    /^Locator map: Sample Town city limits highlighted within North Carolina/,
  );
  assert.ok(10 < SMALL_BELOW);
  assert.equal(locator.context, null, 'no county named for it, so none drawn');
});

test('a small city inside a ledger county shades that county behind it and names it', () => {
  const locator = locatorFor(sample, 'city', 'Sample City');
  assert.equal(locator.context, sample.areas.county.d);
  assert.equal(
    locator.label,
    'Locator map: Sample City city limits highlighted within Sample County, North Carolina, with county lines.',
  );
});

test('a city big enough to see does not get its county drawn', () => {
  const locator = locatorFor(sample, 'bigcity', 'Big City');
  assert.equal(locator.context, null);
});

test('the real boundaries: Henderson is drawn with Vance County behind it; Vance is drawn alone', () => {
  const vance = locatorFor(boundaries, 'vance', 'Vance County');
  assert.equal(vance.context, null);
  const henderson = locatorFor(boundaries, 'henderson', 'Henderson');
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

test('the statewide map uses one color, full for watched and tint otherwise, counties before places', () => {
  const map = statewideFor(sample, [
    { id: 'city', name: 'Sample City', tier: 'decisions' },
    { id: 'county', name: 'Sample County', tier: 'watched' },
    { id: 'narrow', name: 'Narrow County', tier: 'decisions' },
    { id: 'town', name: 'Sample Town', tier: 'watched' },
  ]);
  assert.deepEqual(
    map.areas.map((a) => [a.id, a.fill]),
    [
      ['county', 'full'],
      ['narrow', 'tint'],
      ['city', 'tint'],
      ['town', 'full'],
    ],
  );
  assert.deepEqual(
    map.groups.map((g) => [g.text, g.fill, g.members.map((m) => m.name)]),
    [
      ['Hearings and decisions', 'full', ['Sample County', 'Sample Town']],
      ['Decisions only', 'tint', ['Sample City', 'Narrow County']],
    ],
  );
  assert.equal('rings' in map, false, 'the statewide map draws no rings');
  assert.equal(
    map.label,
    'Map of North Carolina with county lines, shading the 4 places this ledger covers. The legend lists them.',
  );
  assert.equal(map.credit, 'Boundaries: U.S. Census Bureau, cartographic boundary files (2024).');
  assert.deepEqual(map.missing, []);
});

test('a place with the same fill as a covered county around it is left to the county; a different one draws on top', () => {
  const inner = {
    ...sample,
    areas: {
      ...sample.areas,
      inside: { kind: 'place', d: 'M120 120L130 120L130 130Z', bounds: [120, 120, 130, 130] },
    },
  };
  const same = statewideFor(inner, [
    { id: 'county', name: 'Sample County', tier: 'watched' },
    { id: 'inside', name: 'Inside', tier: 'watched' },
  ]);
  assert.deepEqual(
    same.areas.map((a) => a.id),
    ['county'],
  );
  assert.deepEqual(
    same.groups[0].members.map((m) => m.id),
    ['county', 'inside'],
    'still in the legend',
  );
  assert.match(same.label, /shading the 2 places/);
  const different = statewideFor(inner, [
    { id: 'county', name: 'Sample County', tier: 'decisions' },
    { id: 'inside', name: 'Inside', tier: 'watched' },
  ]);
  assert.deepEqual(
    different.areas.map((a) => [a.id, a.fill]),
    [
      ['county', 'tint'],
      ['inside', 'full'],
    ],
  );
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

test('the real boundaries cover all nine ledger jurisdictions; Greensboro is left to Guilford, the other cities draw', async () => {
  const tiers = {
    davidson: 'decisions',
    forsyth: 'decisions',
    henderson: 'decisions',
  };
  const ids = (await readdir(new URL('../../data/jurisdictions/', import.meta.url)))
    .filter((f) => f.endsWith('.yaml'))
    .map((f) => f.slice(0, -'.yaml'.length));
  const map = statewideFor(
    boundaries,
    ids.map((id) => ({ id, name: id, tier: tiers[id] ?? 'watched' })),
  );
  assert.deepEqual(map.missing, []);
  const drawn = map.areas.map((a) => a.id);
  assert.equal(drawn.includes('greensboro'), false);
  assert.ok(drawn.includes('winston-salem') && drawn.includes('henderson'));
  assert.match(map.label, /shading the 9 places/);
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
    /<path class="statewide-area statewide-full"[^>]*data-jurisdiction="guilford"/,
  );
  assert.match(
    figure[1],
    /Hearings and decisions:<\/span>\s*<a href="\/jurisdictions\/guilford\/">Guilford County<\/a>/,
  );
  assert.match(
    figure[1],
    /<p class="statewide-credit">Boundaries: U\.S\. Census Bureau, cartographic boundary files \(\d{4}\)\.<\/p>/,
  );
  assert.doesNotMatch(figure[1], /<circle|<pattern|locator-ring/, 'no rings and no hatching');
  assert.equal(hasScriptTag(html), false);
});

test('no page draws a locator ring', () => {
  for (const { name, html } of pages) {
    assert.doesNotMatch(html, /locator-ring/, name);
  }
});

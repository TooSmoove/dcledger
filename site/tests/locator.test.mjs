// Static locator maps (issue #132, item 3): each jurisdiction page draws where it is, as an SVG image with an
// accessible name and a visible credit, and no script. The fixture's guilford is a real ID, so it has an area in
// src/geo/nc-boundaries.json; the fixture-only jurisdictions don't, and show no locator.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { hasScriptTag } from '../scripts/quality/static-checks.mjs';
import { RING_BELOW, locatorFor } from '../src/lib/locator.js';
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
    county: { kind: 'county', d: 'M100 100L200 100L200 200Z', bounds: [100, 100, 200, 200] },
    city: { kind: 'place', d: 'M500 50L510 50L510 58Z', bounds: [500, 50, 510, 58] },
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
  const locator = locatorFor(sample, 'city', 'Sample City');
  assert.match(locator.label, /^Locator map: Sample City city limits highlighted/);
  assert.ok(10 < RING_BELOW);
  assert.deepEqual(locator.ring, { cx: 505, cy: 54, r: 21 });
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

import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ATTRIBUTION,
  NC_BOUNDS,
  basemapAssets,
  coordinatesText,
  initialBounds,
  mapStyle,
} from '../src/lib/basemap.js';

test('no configured basemap means no map', () => {
  assert.equal(basemapAssets(undefined), null);
  assert.equal(basemapAssets(''), null);
  assert.equal(basemapAssets('  '), null);
});

test('fonts, sprite and tiles all come from the basemap bucket', () => {
  assert.deepEqual(basemapAssets('https://pub-0123.r2.dev/basemap/nc.pmtiles'), {
    tiles: 'pmtiles://https://pub-0123.r2.dev/basemap/nc.pmtiles',
    glyphs: 'https://pub-0123.r2.dev/basemap/fonts/{fontstack}/{range}.pbf',
    sprite: 'https://pub-0123.r2.dev/basemap/sprites/v4/light',
  });
});

test('plain http is only for local previews', () => {
  assert.equal(
    basemapAssets('http://127.0.0.1:8801/basemap/nc.pmtiles').glyphs,
    'http://127.0.0.1:8801/basemap/fonts/{fontstack}/{range}.pbf',
  );
  assert.ok(basemapAssets('http://localhost/nc.pmtiles'));
  assert.throws(() => basemapAssets('http://example.org/nc.pmtiles'), /plain https URL/);
});

test('anything but a plain https .pmtiles URL fails the build', () => {
  for (const bad of [
    'not a url',
    'https://example.org/basemap/nc.mbtiles',
    'https://example.org/nc.pmtiles?token=1',
    'https://example.org/nc.pmtiles#x',
    'https://user:pass@example.org/nc.pmtiles',
    'ftp://example.org/nc.pmtiles',
  ]) {
    assert.throws(() => basemapAssets(bad), /PUBLIC_BASEMAP_URL/, bad);
  }
});

test('the style is version 8 with one Protomaps source carrying the OSM attribution', () => {
  const assets = basemapAssets('https://basemap.example.org/basemap/nc.pmtiles');
  const layers = [{ id: 'earth', type: 'fill', source: 'protomaps', 'source-layer': 'earth' }];
  const style = mapStyle(assets, layers);

  assert.equal(style.version, 8);
  assert.equal(style.glyphs, assets.glyphs);
  assert.equal(style.sprite, assets.sprite);
  assert.deepEqual(Object.keys(style.sources), ['protomaps']);
  assert.equal(
    style.sources.protomaps.url,
    'pmtiles://https://basemap.example.org/basemap/nc.pmtiles',
  );
  assert.equal(style.sources.protomaps.attribution, ATTRIBUTION);
  assert.match(ATTRIBUTION, /© OpenStreetMap contributors/);
  assert.equal(style.layers, layers);
});

test('the map opens on the markers, or on North Carolina when there are none', () => {
  assert.deepEqual(initialBounds([]), [
    [NC_BOUNDS[0][0], NC_BOUNDS[0][1]],
    [NC_BOUNDS[1][0], NC_BOUNDS[1][1]],
  ]);
  assert.deepEqual(
    initialBounds([
      { lat: 36.0726, lon: -79.792 },
      { lat: 35.9557, lon: -80.0053 },
    ]),
    [
      [-80.0053, 35.9557],
      [-79.792, 36.0726],
    ],
  );
});

test('coordinates read as degrees with hemispheres', () => {
  assert.equal(coordinatesText({ lat: 36.0726, lon: -79.792 }), '36.0726° N, 79.7920° W');
});

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import {
  SUPPORTED_SCHEMA_VERSION,
  actionsFrom,
  eventsFrom,
  parseSiteData,
} from '../src/lib/site-data.js';

const fixture = await readFile(new URL('./fixtures/events.json', import.meta.url), 'utf8');

test('the fixture uses the supported schema version', () => {
  assert.equal(parseSiteData(fixture).schema_version, SUPPORTED_SCHEMA_VERSION);
});

test('events are read from the events array', () => {
  assert.deepEqual(
    eventsFrom(fixture).map((event) => event.id),
    [
      'fixture-guilford-boc-2026-10-15',
      'fixture-guilford-boc-2026-11-05',
      'fixture-escaping-check',
      'fixture-guilford-boc-2026-12-17',
    ],
  );
});

test('an unsupported schema version is rejected with a clear message', () => {
  const future = JSON.stringify({ ...JSON.parse(fixture), schema_version: 2 });
  assert.throws(() => parseSiteData(future), /schema_version 2 is not supported/);
});

test('a file without a schema version is rejected', () => {
  assert.throws(() => parseSiteData('{"events": []}'), /schema_version undefined/);
});

test('actions are read from the actions array', () => {
  assert.deepEqual(
    actionsFrom(fixture).map((action) => action.id),
    [
      'fixture-guilford-moratorium-2026-11-05',
      'fixture-city-text-amendment-2026-10-20',
      'fixture-guilford-resolution-2026-11-04',
      'fixture-decided-moratorium-2026-09-02',
    ],
  );
});

test('every fixture document says how it was retrieved, and two were saved by hand (#113)', () => {
  const { events, actions } = parseSiteData(fixture);
  const retrievals = [...events, ...actions].flatMap((item) =>
    item.documents.map((document) => document.retrieval),
  );
  assert.equal(retrievals.length, 9);
  assert.deepEqual([...new Set(retrievals)].sort(), ['fetched', 'manual']);
  assert.equal(retrievals.filter((retrieval) => retrieval === 'manual').length, 2);
});

test('a schema 1 file written before actions existed has none', () => {
  const { actions: _ignored, ...older } = JSON.parse(fixture);
  assert.deepEqual(actionsFrom(JSON.stringify(older)), []);
});

test('every fixture jurisdiction states a coverage tier, and all three tiers appear (#115)', () => {
  const tiers = parseSiteData(fixture).jurisdictions.map((j) => j.coverage_tier);
  assert.deepEqual([...new Set(tiers)].sort(), ['decisions', 'registered', 'watched']);
});

test('every fixture decision carries each term key, null when not stated (#115)', () => {
  const keys = [
    'instrument_number',
    'threshold',
    'scope',
    'effective_on',
    'expires_on',
    'early_termination',
    'exemptions',
    'successor_id',
    'status',
  ];
  for (const action of parseSiteData(fixture).actions) {
    assert.deepEqual(Object.keys(action).slice(-keys.length), keys, action.id);
  }
});

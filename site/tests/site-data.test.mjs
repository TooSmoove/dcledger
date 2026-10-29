import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { SUPPORTED_SCHEMA_VERSION, eventsFrom, parseSiteData } from '../src/lib/site-data.js';

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

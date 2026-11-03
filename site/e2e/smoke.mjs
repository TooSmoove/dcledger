// The site half of the end-to-end smoke test (issue #24, ADR-0006). ci's e2e job runs
// tests/DcLedger.Cli.Tests/EndToEndSmokeTests.cs with DCL_E2E_OUT=out, so recorded Legistar responses go through
// ingest, the review issue's YAML, validate and export into ../out/; then `npm run build` and `npm run test:e2e`.
// Not named *.test.mjs, so `npm test` (which runs over the fixtures) leaves it out.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

const dist = new URL('../dist/', import.meta.url);
const out = new URL('../../out/', import.meta.url);

// The record's ID from the review issue, and the title the test writes as the reviewer (EndToEndSmokeTests).
const id = 'guilford-board-of-commissioners-2026-10-15';
const title = 'Public hearing on a data center rezoning (end-to-end test)';

const read = (base, path) => readFile(new URL(path, base), 'utf8');
const radar = await read(dist, 'radar/index.html');

test('the reviewed event is on /radar with its Eastern time (WS-AC4)', () => {
  assert.ok(
    radar.includes(`<a class="item" href="/items/${id}/">${title}</a>`),
    `/radar has no link to ${id}`,
  );
  assert.match(
    radar,
    /<time datetime="2026-10-15T17:30:00-04:00">Thu, Oct 15, 2026, 5:30 PM EDT<\/time>/,
  );
});

test('its item page is built', async () => {
  assert.ok((await read(dist, `items/${id}/index.html`)).includes(title));
});

test('the deployable carries the exported feeds unchanged', async () => {
  for (const file of ['radar.ics', 'radar/guilford.ics', 'events.csv']) {
    assert.equal(await read(dist, file), await read(out, `public/${file}`), file);
  }
});

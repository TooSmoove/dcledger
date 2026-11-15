// The corrections log's contract with `dcl export` and the methodology page's computed wording (issue #117).
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import {
  SUPPORTED_CORRECTIONS_VERSION,
  issueHref,
  newestFirst,
  parseCorrections,
  recordHref,
} from '../src/lib/corrections.js';
import { citationText, logOpenedText, shortDateText } from '../src/lib/methodology.js';

const fixture = await readFile(new URL('./fixtures/corrections.json', import.meta.url), 'utf8');
const entry = {
  id: '2026-11-24-fixture-vote-count',
  reported_on: '2026-11-23',
  corrected_on: '2026-11-24',
  record_ids: [],
  summary: 'The vote was recorded as 9-0; the minutes show 8-1.',
  issue: null,
};
const log = (corrections) =>
  JSON.stringify({ schema_version: 1, opened_on: '2026-11-22', corrections });

test('the fixture parses with the schema version the exporter writes', () => {
  const parsed = parseCorrections(fixture);
  assert.equal(parsed.schema_version, SUPPORTED_CORRECTIONS_VERSION);
  assert.equal(parsed.opened_on, '2026-11-22');
  assert.equal(parsed.corrections.length, 2);
});

test('an unknown schema version fails the build', () => {
  assert.throws(
    () =>
      parseCorrections(
        JSON.stringify({ schema_version: 2, opened_on: '2026-11-22', corrections: [] }),
      ),
    /schema_version 2 is not supported/,
  );
});

test('malformed entries fail the build instead of rendering a partial log', () => {
  for (const [change, message] of [
    [{ id: 'Not A Slug' }, /id must be a slug/],
    [{ corrected_on: 'Nov 24' }, /corrected_on must be a date/],
    [{ record_ids: 'guilford' }, /record_ids must be a list/],
    [{ summary: ' ' }, /summary is required/],
    [{ issue: 0 }, /issue must be a positive whole number or null/],
  ]) {
    assert.throws(() => parseCorrections(log([{ ...entry, ...change }])), message);
  }
  assert.throws(
    () => parseCorrections(JSON.stringify({ schema_version: 1, opened_on: null, corrections: [] })),
    /opened_on must be a date/,
  );
});

test('an empty log parses', () => {
  assert.deepEqual(parseCorrections(log([])).corrections, []);
});

test('entries list newest correction first, then by ID', () => {
  const a = { ...entry, id: 'a', corrected_on: '2026-11-24' };
  const b = { ...entry, id: 'b', corrected_on: '2026-11-24' };
  const c = { ...entry, id: 'c', corrected_on: '2026-11-22' };
  assert.deepEqual(
    newestFirst([c, b, a]).map((e) => e.id),
    ['a', 'b', 'c'],
  );
});

test('a corrected record links to where it is shown, and an unknown one stops the build', () => {
  const records = {
    events: new Set(['fixture-boc-2026-11-05']),
    jurisdictions: new Set(['guilford']),
    actions: new Map([['fixture-moratorium-2026-11-05', 'guilford']]),
  };
  assert.equal(recordHref('fixture-boc-2026-11-05', records), '/items/fixture-boc-2026-11-05/');
  assert.equal(recordHref('guilford', records), '/jurisdictions/guilford/');
  assert.equal(
    recordHref('fixture-moratorium-2026-11-05', records),
    '/jurisdictions/guilford/#decision-fixture-moratorium-2026-11-05',
  );
  assert.throws(() => recordHref('missing', records), /'missing'/);
});

test('issue links point at the repository', () => {
  assert.equal(issueHref(131), 'https://github.com/TooSmoove/dcledger/issues/131');
});

test('the log opens with a date without its weekday', () => {
  assert.equal(shortDateText('2026-11-22'), 'Nov 22, 2026');
  assert.equal(logOpenedText('2026-11-22'), 'Log opened Nov 22, 2026.');
});

test('a citation carries the page address, source, retrieval time and hash', () => {
  assert.equal(
    citationText({
      siteUrl: 'https://ncdcledger.org',
      id: 'guilford-boc-2026-11-05',
      title: 'Public hearing: data center text amendment',
      jurisdiction: 'Guilford County',
      startsText: 'Thu, Nov 5, 2026, 5:30 PM EST',
      document: {
        url: 'https://guilford.legistar.com/View.ashx?M=A&ID=1',
        retrievedText: 'Wed, Oct 28, 2026, 8:47 AM EDT',
        sha256: 'a'.repeat(64),
      },
    }),
    'NC Data Center Ledger, "Public hearing: data center text amendment," Guilford County, ' +
      'Thu, Nov 5, 2026, 5:30 PM EST. https://ncdcledger.org/items/guilford-boc-2026-11-05/ ' +
      '(accessed [the date you viewed it]). Source: https://guilford.legistar.com/View.ashx?M=A&ID=1, ' +
      `retrieved Wed, Oct 28, 2026, 8:47 AM EDT, SHA-256 ${'a'.repeat(64)}.`,
  );
});

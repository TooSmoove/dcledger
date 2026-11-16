// The report form's endpoint (ADR-0009, docs/reports.md): field checks, the trap field, the hourly limit, the
// same-origin rule, and that nothing is stored unless everything checks out.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { handleReport } from '../functions/api/report.js';
import {
  KINDS,
  LIMITS,
  cleanText,
  hashAddress,
  isWebAddress,
  parseReport,
  reference,
  storeReport,
} from '../functions/_lib/report.js';
import { testD1 } from './helpers/d1.mjs';

const ORIGIN = 'https://ncdcledger.org';
const NOW = new Date('2026-11-16T14:00:00Z');
const env = () => ({ REPORTS: testD1(), REPORT_HASH_KEY: 'test-key' });

function post(fields, headers = {}) {
  const body = new URLSearchParams(fields).toString();
  return new Request(`${ORIGIN}/api/report`, {
    method: 'POST',
    body,
    headers: {
      Origin: ORIGIN,
      'Content-Type': 'application/x-www-form-urlencoded',
      'CF-Connecting-IP': '203.0.113.7',
      ...headers,
    },
  });
}

const valid = {
  kind: 'correction',
  item: 'https://ncdcledger.org/items/guilford-boc-2026-11-05/',
  message: 'The vote was 8-1, not 9-0.',
  source: 'https://www.guilfordcountync.gov/minutes.pdf',
  email: 'reader@example.org',
  website: '',
};

const rows = (e) => e.REPORTS.sqlite.prepare('SELECT * FROM reports').all();

test('cleanText drops control characters, normalizes line ends and trims', () => {
  assert.equal(cleanText('  a\r\nb\rc\u0000\u0007 d\t e  '), 'a\nb\ncd\t e');
  assert.equal(cleanText(null), '');
});

test('only http and https addresses count as web addresses', () => {
  assert.ok(isWebAddress('https://example.org/x'));
  assert.ok(isWebAddress('http://example.org'));
  assert.ok(!isWebAddress('javascript:alert(1)'));
  assert.ok(!isWebAddress('data:text/html,hi'));
  assert.ok(!isWebAddress('example.org'));
});

test('a complete report parses, and empty optional fields become null', () => {
  assert.deepEqual(parseReport(new URLSearchParams(valid)).report, {
    kind: 'correction',
    item: valid.item,
    message: valid.message,
    source: valid.source,
    email: valid.email,
  });
  const minimal = parseReport(new URLSearchParams({ kind: 'question', message: 'Hi?' })).report;
  assert.deepEqual(minimal, {
    kind: 'question',
    item: null,
    message: 'Hi?',
    source: null,
    email: null,
  });
});

test('each bad field gets its own plain-words error', () => {
  const cases = [
    [{ kind: 'nope' }, /Choose what your report is about/],
    [{ message: '   ' }, /Tell us what is wrong/],
    [{ message: 'x'.repeat(LIMITS.message + 1) }, /under 5000 characters/],
    [{ item: 'x'.repeat(LIMITS.item + 1) }, /page or item under/],
    [{ source: 'javascript:alert(1)' }, /must be a web address/],
    [{ email: 'not an email' }, /Check the email address/],
    [{ email: 'a@b' }, /Check the email address/],
  ];
  for (const [change, message] of cases) {
    const { errors } = parseReport(new URLSearchParams({ ...valid, ...change }));
    assert.ok(
      errors?.some((e) => message.test(e)),
      `${JSON.stringify(change)} → ${errors}`,
    );
  }
});

test('the form offers exactly the kinds the table accepts', () => {
  assert.deepEqual(Object.keys(KINDS), ['correction', 'question', 'stop_fetching', 'other']);
  const e = env();
  for (const kind of Object.keys(KINDS)) {
    e.REPORTS.sqlite
      .prepare(
        "INSERT INTO reports (created_utc, kind, message) VALUES ('2026-11-16T00:00:00Z', ?, 'm')",
      )
      .run(kind);
  }
  assert.throws(() =>
    e.REPORTS.sqlite
      .prepare(
        "INSERT INTO reports (created_utc, kind, message) VALUES ('2026-11-16T00:00:00Z', 'x', 'm')",
      )
      .run(),
  );
});

test('a valid report is stored and the reader is sent to the thanks page', async () => {
  const e = env();
  const response = await handleReport(post(valid), e, NOW);
  assert.equal(response.status, 303);
  assert.equal(response.headers.get('Location'), '/report/thanks/');
  assert.equal(response.headers.get('X-Robots-Tag'), 'noindex');
  const [row] = rows(e);
  assert.equal(row.kind, 'correction');
  assert.equal(row.message, valid.message);
  assert.equal(row.email, valid.email);
  assert.equal(row.status, 'new');
  assert.equal(row.created_utc, '2026-11-16T14:00:00Z');
  assert.equal(row.ip_hash, await hashAddress('test-key', '203.0.113.7'), 'stores only the hash');
  assert.ok(!JSON.stringify(row).includes('203.0.113.7'), 'never the address itself');
});

test('a filled trap field looks like success but stores nothing', async () => {
  const e = env();
  const response = await handleReport(post({ ...valid, website: 'https://spam.example' }), e, NOW);
  assert.equal(response.status, 303);
  assert.equal(rows(e).length, 0);
});

test('a report with mistakes is refused with what to fix, escaped, and nothing is stored', async () => {
  const e = env();
  const response = await handleReport(post({ ...valid, kind: '<b>x</b>', message: '' }), e, NOW);
  assert.equal(response.status, 400);
  const html = await response.text();
  assert.match(html, /Choose what your report is about/);
  assert.match(html, /Tell us what is wrong/);
  assert.doesNotMatch(html, /<b>x<\/b>/);
  assert.equal(rows(e).length, 0);
});

test('a post from another site, or without an Origin, is refused before anything is read', async () => {
  const e = env();
  assert.equal(
    (await handleReport(post(valid, { Origin: 'https://evil.example' }), e, NOW)).status,
    403,
  );
  const noOrigin = new Request(`${ORIGIN}/api/report`, { method: 'POST' });
  assert.equal((await handleReport(noOrigin, e, NOW)).status, 403);
  assert.equal(rows(e).length, 0);
});

test('without its bindings the form says it is unavailable instead of failing open', async () => {
  assert.equal((await handleReport(post(valid), {}, NOW)).status, 503);
  assert.equal((await handleReport(post(valid), { REPORTS: testD1() }, NOW)).status, 503);
});

test('a body that is too large or not form-encoded is refused', async () => {
  const e = env();
  const big = await handleReport(post({ ...valid, message: 'x'.repeat(LIMITS.bodyBytes) }), e, NOW);
  assert.equal(big.status, 413);
  const json = await handleReport(post(valid, { 'Content-Type': 'application/json' }), e, NOW);
  assert.equal(json.status, 415);
  assert.equal(rows(e).length, 0);
});

test('one sender gets 5 reports an hour; another sender is unaffected', async () => {
  const e = env();
  for (let i = 0; i < LIMITS.perHour; i++) {
    assert.equal((await handleReport(post(valid), e, NOW)).status, 303);
  }
  assert.equal((await handleReport(post(valid), e, NOW)).status, 429);
  const other = await handleReport(post(valid, { 'CF-Connecting-IP': '198.51.100.1' }), e, NOW);
  assert.equal(other.status, 303);
  const later = new Date(NOW.getTime() + 61 * 60_000);
  assert.equal((await handleReport(post(valid), e, later)).status, 303, 'the limit is per hour');
});

test('sender hashes are forgotten after a day', async () => {
  const db = testD1();
  const report = parseReport(new URLSearchParams(valid)).report;
  await storeReport(db, report, 'old-hash', '2026-11-14T10:00:00Z');
  await storeReport(db, report, 'new-hash', '2026-11-16T10:00:00Z');
  const hashes = db.sqlite.prepare('SELECT ip_hash FROM reports ORDER BY id').all();
  assert.deepEqual(
    hashes.map((r) => r.ip_hash),
    [null, 'new-hash'],
  );
});

test('a report is referred to as R-<number>', () => {
  assert.equal(reference(7), 'R-7');
});

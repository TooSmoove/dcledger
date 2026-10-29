import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  WINDOW_DAYS,
  byStart,
  easternIso,
  easternText,
  freshnessText,
  inWindow,
  labelText,
  statusText,
} from '../src/lib/radar.js';

test('Eastern text carries the zone and crosses the daylight-saving change', () => {
  assert.equal(easternText('2026-10-15T21:30:00Z'), 'Thu, Oct 15, 2026, 5:30 PM EDT');
  assert.equal(easternText('2026-11-05T22:30:00Z'), 'Thu, Nov 5, 2026, 5:30 PM EST');
});

test('Eastern ISO text matches what the exporter writes as starts_local', () => {
  assert.equal(easternIso('2026-10-15T21:30:00Z'), '2026-10-15T17:30:00-04:00');
  assert.equal(easternIso('2026-11-05T22:30:00Z'), '2026-11-05T17:30:00-05:00');
  assert.equal(easternIso('2026-11-01T05:30:00Z'), '2026-11-01T01:30:00-04:00');
  assert.equal(easternIso('2026-11-01T06:30:00Z'), '2026-11-01T01:30:00-05:00');
});

test('only UTC instants are accepted', () => {
  assert.throws(() => easternText('2026-11-05T17:30:00-05:00'), /Expected a UTC instant/);
  assert.throws(
    () => inWindow({ starts_utc: 'soon' }, '2026-10-25T21:00:00Z'),
    /Expected a UTC instant/,
  );
});

test('the window is [D, D + 30 days], inclusive at both ends', () => {
  const d = '2026-10-25T21:00:00Z';
  assert.equal(WINDOW_DAYS, 30);
  assert.equal(inWindow({ starts_utc: '2026-10-25T20:59:59Z' }, d), false);
  assert.equal(inWindow({ starts_utc: '2026-10-25T21:00:00Z' }, d), true);
  assert.equal(inWindow({ starts_utc: '2026-11-24T21:00:00Z' }, d), true);
  assert.equal(inWindow({ starts_utc: '2026-11-24T21:00:01Z' }, d), false);
});

test('events sort by start, then ID, as raw events or collection entries', () => {
  const raw = byStart([
    { id: 'b', starts_utc: '2026-11-05T22:30:00Z' },
    { id: 'c', starts_utc: '2026-10-29T22:00:00Z' },
    { id: 'a', starts_utc: '2026-11-05T22:30:00Z' },
  ]);
  assert.deepEqual(
    raw.map((e) => e.id),
    ['c', 'a', 'b'],
  );
  const entries = byStart([
    { id: 'b', data: { starts_utc: '2026-11-05T22:30:00Z' } },
    { id: 'a', data: { starts_utc: '2026-10-29T22:00:00Z' } },
  ]);
  assert.deepEqual(
    entries.map((e) => e.id),
    ['a', 'b'],
  );
});

test('status and label are words, not colors', () => {
  assert.equal(statusText('cancelled'), 'Cancelled');
  assert.equal(statusText('scheduled'), 'Scheduled');
  assert.equal(labelText('confirmed_official'), 'Confirmed (official record)');
  assert.equal(labelText('reported_news'), 'Reported (news)');
});

test('freshness shows last checked in Eastern and a text stale flag', () => {
  assert.equal(
    freshnessText({ last_checked_utc: '2026-10-25T20:17:00Z', stale: false }),
    'Last checked Sun, Oct 25, 2026, 4:17 PM EDT',
  );
  assert.equal(
    freshnessText({ last_checked_utc: '2026-10-22T14:00:00Z', stale: true }),
    'Last checked Thu, Oct 22, 2026, 10:00 AM EDT ⚠ Stale: no successful check in 48 hours',
  );
  assert.equal(
    freshnessText({ last_checked_utc: null, stale: true }),
    'Never checked ⚠ Stale: no successful check in 48 hours',
  );
});

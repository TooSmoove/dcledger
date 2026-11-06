import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  WINDOW_DAYS,
  actionTypeText,
  byDecisionNewestFirst,
  byStart,
  dateText,
  easternIso,
  easternText,
  freshnessText,
  inWindow,
  labelText,
  outcomeText,
  statusText,
  votesText,
  voteText,
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

test('every action type and outcome token the exporter writes has fixed words (#25)', () => {
  assert.equal(actionTypeText('moratorium'), 'Moratorium');
  assert.equal(actionTypeText('rezoning'), 'Rezoning');
  assert.equal(actionTypeText('text_amendment'), 'Ordinance text amendment');
  assert.equal(actionTypeText('resolution'), 'Resolution');
  assert.equal(outcomeText('adopted'), 'Adopted');
  assert.equal(outcomeText('adopted_amended'), 'Adopted with amendments');
  assert.equal(outcomeText('failed'), 'Failed');
  assert.equal(outcomeText('continued'), 'Continued to a later meeting');
  assert.equal(outcomeText('tabled'), 'Tabled');
});

test('an unknown token fails the build instead of rendering blank', () => {
  assert.throws(() => outcomeText('approved'), /Unknown outcome "approved"/);
  assert.throws(() => actionTypeText('toString'), /Unknown action type "toString"/);
});

test('votes read as ayes and noes, and a missing vote stays missing', () => {
  assert.equal(votesText('8-1'), '8 in favor, 1 against');
  assert.equal(votesText('9-0'), '9 in favor, 0 against');
  assert.equal(votesText(null), null);
  assert.throws(() => votesText('unanimous'), /Expected votes like "9-0"/);
});

test('a unanimous vote reads as such, with or without the tally', () => {
  assert.equal(voteText(null, true), 'Unanimous (tally not yet recorded)');
  assert.equal(voteText('9-0', true), '9 in favor, 0 against (unanimous)');
  assert.equal(voteText('8-1', false), '8 in favor, 1 against');
  assert.equal(voteText(null, false), null);
});

test('decision dates are calendar dates with no zone shift', () => {
  assert.equal(dateText('2026-11-05'), 'Thu, Nov 5, 2026');
  assert.equal(dateText('2026-03-08'), 'Sun, Mar 8, 2026', 'the day daylight saving starts');
  assert.throws(() => dateText('2026-11-05T17:30:00-05:00'), /Expected a date/);
});

test('decisions sort newest first, then by ID', () => {
  const entry = (id, decided) => ({ id, data: { decided_on: decided } });
  assert.deepEqual(
    byDecisionNewestFirst([
      entry('b', '2026-10-20'),
      entry('c', '2026-11-05'),
      entry('a', '2026-11-05'),
    ]).map((e) => e.id),
    ['a', 'c', 'b'],
  );
});

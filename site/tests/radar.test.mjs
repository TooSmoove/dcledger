import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  WINDOW_DAYS,
  actionStatusText,
  actionTypeText,
  byDecisionNewestFirst,
  byStart,
  coverageText,
  dateText,
  easternIso,
  easternText,
  freshnessText,
  glyphCells,
  inWindow,
  isWatched,
  labelText,
  outcomeText,
  retrievalText,
  seatList,
  staleAfterUtc,
  statusText,
  termRows,
  votesText,
  voteText,
  windowDays,
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

test('a copy a person saved says so; a fetched one says nothing; unknown tokens throw (#113)', () => {
  assert.equal(
    retrievalText('manual'),
    'Saved by a person in a browser (the source does not allow automated downloads)',
  );
  assert.equal(retrievalText('fetched'), null);
  assert.throws(() => retrievalText('browser'), /Unknown retrieval "browser"/);
  assert.throws(() => retrievalText(undefined), /Unknown retrieval/);
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
    freshnessText({
      last_checked_utc: '2026-10-22T14:00:00Z',
      stale: true,
      stale_after_hours: 168,
    }),
    'Last checked Thu, Oct 22, 2026, 10:00 AM EDT ⚠ Stale: no successful check in 7 days',
  );
  assert.equal(
    freshnessText({ last_checked_utc: null, stale: true }),
    'Never checked ⚠ Stale: no successful check in 48 hours',
  );
});

test('the stale deadline is the last check plus the threshold, across a DST change (AC10, #103)', () => {
  assert.equal(staleAfterUtc('2026-10-25T20:17:00Z', 48), '2026-10-27T20:17:00Z');
  assert.equal(staleAfterUtc('2026-11-09T01:10:00Z', 168), '2026-11-16T01:10:00Z');
  assert.equal(
    easternText(staleAfterUtc('2026-10-30T22:24:00Z', 48)),
    'Sun, Nov 1, 2026, 5:24 PM EST',
    'elapsed hours, not wall-clock hours, across the Nov 1 fall-back',
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

test('each coverage tier is one sentence, and only watched carries the freshness promise (#115)', () => {
  assert.match(coverageText('watched'), /^Coverage: hearings watched\./);
  assert.match(coverageText('decisions'), /^Coverage: decisions only\./);
  assert.match(coverageText('registered'), /^Coverage: listed only\./);
  assert.equal(isWatched('watched'), true);
  assert.equal(isWatched('decisions'), false);
  assert.equal(isWatched('registered'), false);
  assert.throws(() => coverageText('full'), /Unknown coverage tier "full"/);
  assert.throws(() => isWatched('constructor'), /Unknown coverage tier "constructor"/);
});

test('every action status token has fixed words, matching the feed (#115)', () => {
  assert.equal(actionStatusText('in_effect'), 'In effect');
  assert.equal(actionStatusText('expired'), 'Expired');
  assert.equal(actionStatusText('superseded'), 'Replaced or extended by a later decision');
  assert.equal(actionStatusText('repealed'), 'Repealed');
  assert.throws(() => actionStatusText('active'), /Unknown action status "active"/);
});

test('terms render in a fixed order, text quoted as written, dates as calendar dates (#115)', () => {
  const rows = termRows({
    instrument_number: '26-01',
    threshold: 'electrical power demand exceeding 10 megawatts',
    scope: 'data center facilities',
    effective_on: '2026-11-05',
    expires_on: '2027-02-14',
    early_termination: 'upon adoption of text amendments',
    exemptions: 'complete applications',
    successor_id: 'not-a-row',
    status: 'in_effect',
  });
  assert.deepEqual(rows, [
    { label: 'Status', text: 'In effect' },
    { label: 'Ordinance or resolution number', text: '26-01' },
    { label: 'Threshold', text: '\u201Celectrical power demand exceeding 10 megawatts\u201D' },
    { label: 'Covers', text: '\u201Cdata center facilities\u201D' },
    { label: 'Effective', text: 'Thu, Nov 5, 2026', date: '2026-11-05' },
    { label: 'Expires', text: 'Sun, Feb 14, 2027', date: '2027-02-14' },
    { label: 'Ends earlier', text: '\u201Cupon adoption of text amendments\u201D' },
    { label: 'Exemptions', text: '\u201Ccomplete applications\u201D' },
  ]);
});

test('a decision without terms has no term rows, and one term gives one row (#115)', () => {
  assert.deepEqual(termRows({}), []);
  assert.deepEqual(termRows({ threshold: null, expires_on: '2027-02-14', status: undefined }), [
    { label: 'Expires', text: 'Sun, Feb 14, 2027', date: '2027-02-14' },
  ]);
  assert.throws(() => termRows({ status: 'active' }), /Unknown action status/);
});

test('seats come only from a recorded tally, never from the size of the board (#133)', () => {
  assert.deepEqual(seatList('2-1'), ['aye', 'aye', 'no']);
  assert.deepEqual(seatList('0-0'), []);
  assert.deepEqual(seatList(null), [], 'unanimous with no tally: no seats');
  assert.throws(() => seatList('nine'), /Expected votes/);
});

test('the hash glyph is the 64 hex digits as shades 0 to 3 (#133)', () => {
  const cells = glyphCells('0123456789abcdef'.repeat(4));
  assert.equal(cells.length, 64);
  assert.deepEqual(cells.slice(0, 16), [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3]);
  assert.throws(() => glyphCells('ABC'), /64 lowercase hex/);
});

test('the window strip runs 31 Eastern days from the export and counts items per day (#133)', () => {
  // 01:30 UTC on Nov 16 is still Nov 15 in Eastern time.
  const days = windowDays('2026-11-16T01:30:00Z', ['2026-11-17T22:30:00Z', '2026-11-17T23:00:00Z']);
  assert.equal(days.length, 31);
  assert.deepEqual(
    { iso: days[0].iso, today: days[0].today, weekday: days[0].weekday, month: days[0].month },
    { iso: '2026-11-15', today: true, weekday: 'S', month: 'Nov' },
  );
  assert.equal(days[2].items, 2, 'two items on Nov 17, Eastern');
  assert.equal(days.find((d) => d.iso === '2026-12-01').month, 'Dec');
  assert.equal(days[30].iso, '2026-12-15');
  assert.ok(days[0].weekend && days[6].weekend && !days[1].weekend);
});

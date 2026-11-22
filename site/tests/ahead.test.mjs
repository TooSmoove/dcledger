import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
  COVERAGE_TEXT,
  PASSED_UNRECORDED_TEXT,
  aheadSections,
  anchorOf,
  kindText,
  knownDateName,
  knownDates,
  soonestUpcoming,
  stateAt,
} from '../src/lib/ahead.js';

// The table .NET's KnownDateRulesTests and KnownDateWordsTests read too (ADR-0010).
const shared = JSON.parse(
  readFileSync(new URL('../../tests/fixtures/known-dates/cases.json', import.meta.url), 'utf8'),
);

for (const c of shared.cases) {
  test(`state: ${c.name}`, () => {
    assert.equal(
      stateAt({
        kind: c.kind,
        date: c.date,
        today: c.today,
        status: c.status,
        successorId: c.successor_id,
        somethingOnDate: c.something_on_date,
      }),
      c.state,
    );
  });
}

test('names, flag and coverage match the shared fixture', () => {
  for (const n of shared.names) {
    assert.equal(knownDateName(n.kind, n.action_type, n.jurisdiction), n.name);
  }
  assert.equal(PASSED_UNRECORDED_TEXT, shared.flag);
  assert.equal(COVERAGE_TEXT, shared.coverage);
  assert.throws(() => knownDateName('later', 'moratorium', 'X'), /Unknown known-date kind "later"/);
});

test('kind words and anchors', () => {
  assert.equal(kindText('expiry'), 'Expiry date');
  assert.equal(kindText('next'), 'Date set for the next step');
  assert.equal(
    anchorOf('davidson-moratorium-2026-09-28', 'next'),
    'davidson-moratorium-2026-09-28-next',
  );
});

const action = (id, data) => ({
  id,
  data: {
    jurisdiction_id: 'county',
    decided_on: '2026-05-04',
    expires_on: null,
    next_on: null,
    status: null,
    successor_id: null,
    ...data,
  },
});

// D = Tue, Dec 1, 2026 (10:00 Eastern), as in .NET's ModelWithKnownDates.
const GENERATED = '2026-12-01T15:00:00Z';
const actions = [
  action('nov5', {
    decided_on: '2026-11-05',
    expires_on: '2027-01-08',
    next_on: '2026-12-03',
    status: 'in_effect',
  }),
  action('may4', { expires_on: '2026-11-04', status: 'superseded', successor_id: 'nov5' }),
  action('oct1', { decided_on: '2026-10-01', next_on: '2026-10-15' }),
  action('oct20', { jurisdiction_id: 'city', decided_on: '2026-10-20', next_on: '2026-11-17' }),
];
const events = [
  { data: { jurisdiction_id: 'county', starts_utc: '2026-10-15T21:30:00Z' } },
  { data: { jurisdiction_id: 'city', starts_utc: '2026-11-03T23:00:00Z' } },
];

test('known dates come in date order with the same states as .NET', () => {
  assert.deepEqual(
    knownDates(actions, events, GENERATED).map((k) => [k.action.id, k.kind, k.state]),
    [
      ['oct1', 'next', 'recorded'],
      ['may4', 'expiry', 'ended'],
      ['oct20', 'next', 'passed_unrecorded'],
      ['nov5', 'next', 'upcoming'],
      ['nov5', 'expiry', 'upcoming'],
    ],
  );
});

test('a meeting counts for a next step by its Eastern date', () => {
  // 11:30 pm Eastern on Nov 17 is Nov 18 in UTC.
  const late = [{ data: { jurisdiction_id: 'city', starts_utc: '2026-11-18T04:30:00Z' } }];
  const oct20 = knownDates([actions[3]], late, GENERATED);
  assert.equal(oct20[0].state, 'recorded');
});

test('sections split upcoming at the radar window and keep recorded dates 90 days', () => {
  const s = aheadSections(knownDates(actions, events, GENERATED), GENERATED);
  assert.deepEqual(
    s.next.map((k) => k.anchor),
    ['nov5-next'],
  );
  assert.deepEqual(
    s.later.map((k) => k.anchor),
    ['nov5-expiry'],
  );
  assert.deepEqual(
    s.passed.map((k) => k.anchor),
    ['oct20-next'],
  );
  assert.deepEqual(
    s.recorded.map((k) => k.anchor),
    ['oct1-next'],
  );
  assert.equal(s.windowEnd, '2026-12-31');

  const muchLater = aheadSections(
    knownDates(actions, events, '2027-01-14T15:00:00Z'),
    '2027-01-14T15:00:00Z',
  );
  assert.deepEqual(muchLater.recorded, [], 'Oct 15 is more than 90 days before Jan 14');
});

test('the last day of the window is in "Next 30 days"', () => {
  const edge = [
    action('edge', { next_on: '2026-12-31' }),
    action('over', { next_on: '2027-01-01' }),
  ];
  const s = aheadSections(knownDates(edge, [], GENERATED), GENERATED);
  assert.deepEqual(
    s.next.map((k) => k.action.id),
    ['edge'],
  );
  assert.deepEqual(
    s.later.map((k) => k.action.id),
    ['over'],
  );
});

test('the soonest upcoming date, or null', () => {
  const dates = knownDates(actions, events, GENERATED);
  assert.equal(soonestUpcoming(dates)?.anchor, 'nov5-next');
  assert.equal(soonestUpcoming(dates.filter((k) => k.state !== 'upcoming')), null);
});

test('a decision without dates states none', () => {
  assert.deepEqual(knownDates([action('none', {})], [], GENERATED), []);
});

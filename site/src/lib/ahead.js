// Known dates for /ahead/, the home page and decision cards (ADR-0010). Plain JS so node:test can import it directly;
// nothing here runs in the browser. Mirrors DcLedger.Domain.KnownDateRules and the fixed words in
// DcLedger.Publishing.RecordText; both sides are pinned by tests/fixtures/known-dates/cases.json.
import { WINDOW_DAYS, actionTypeText, easternIso } from './radar.js';

/** How long a date whose outcome is on the record stays on /ahead/ after it (ADR-0010 Decision 3). */
export const RECORDED_KEEP_DAYS = 90;

/** The flag for a date that has passed with nothing recorded (docs/wording-bank.md). */
export const PASSED_UNRECORDED_TEXT = 'Date passed. Nothing recorded for it yet.';

/** The line every list of known dates carries. */
export const COVERAGE_TEXT =
  'Only dates stated in an archived document are listed. A body can act on a matter before or after these dates.';

/**
 * The state of a known date at `today` (ADR-0010 Decision 3), in the ADR's order: upcoming (today or later, whatever
 * the status), ended (a passed expiry whose decision is superseded or repealed), recorded, else passed_unrecorded.
 * @param {{ kind: 'expiry' | 'next', date: string, today: string, status: string | null, successorId: string | null, somethingOnDate: boolean }} input
 * @returns {'upcoming' | 'ended' | 'recorded' | 'passed_unrecorded'}
 */
export function stateAt({ kind, date, today, status, successorId, somethingOnDate }) {
  if (date >= today) return 'upcoming';
  if (kind === 'expiry' && (status === 'superseded' || status === 'repealed')) return 'ended';
  const recorded =
    kind === 'expiry' ? status === 'expired' || successorId !== null : somethingOnDate;
  return recorded ? 'recorded' : 'passed_unrecorded';
}

/**
 * The fixed name of a known date, as the calendars and the feed use it: "Moratorium expiry date: Greensboro" or
 * "Date set for the next step: Davidson County, moratorium".
 * @param {'expiry' | 'next'} kind
 * @param {string} actionType
 * @param {string} jurisdictionName
 */
export function knownDateName(kind, actionType, jurisdictionName) {
  const type = actionTypeText(actionType);
  if (kind === 'expiry') return `${type} expiry date: ${jurisdictionName}`;
  if (kind === 'next')
    return `Date set for the next step: ${jurisdictionName}, ${type.toLowerCase()}`;
  throw new Error(`Unknown known-date kind ${JSON.stringify(kind)}.`);
}

/**
 * The kind in words, for a row's label.
 * @param {'expiry' | 'next'} kind
 */
export function kindText(kind) {
  if (kind === 'expiry') return 'Expiry date';
  if (kind === 'next') return 'Date set for the next step';
  throw new Error(`Unknown known-date kind ${JSON.stringify(kind)}.`);
}

/**
 * The row anchor on /ahead/: `{action id}-{kind}`.
 * @param {string} actionId
 * @param {'expiry' | 'next'} kind
 */
export function anchorOf(actionId, kind) {
  return `${actionId}-${kind}`;
}

/**
 * Every known date the actions state, with its state at the export's Eastern date, ordered by date, then kind, then
 * action ID (as KnownDates.From in .NET). A next step is recorded when the same jurisdiction has an event on that
 * Eastern date or a decision taken on it.
 * @template {{ id: string, data: { jurisdiction_id: string, decided_on: string, expires_on: string | null, next_on: string | null, status: string | null, successor_id: string | null } }} A
 * @param {readonly A[]} actions
 * @param {readonly { data: { jurisdiction_id: string, starts_utc: string } }[]} events
 * @param {string} generatedUtc
 * @returns {{ kind: 'expiry' | 'next', date: string, state: string, anchor: string, action: A }[]}
 */
export function knownDates(actions, events, generatedUtc) {
  const today = easternIso(generatedUtc).slice(0, 10);
  const onRecord = new Set([
    ...events.map((e) => `${e.data.jurisdiction_id} ${easternIso(e.data.starts_utc).slice(0, 10)}`),
    ...actions.map((a) => `${a.data.jurisdiction_id} ${a.data.decided_on}`),
  ]);
  const kinds = /** @type {const} */ (['expiry', 'next']);
  const dates = [];
  for (const action of actions) {
    for (const kind of kinds) {
      const date = kind === 'expiry' ? action.data.expires_on : action.data.next_on;
      if (date === null || date === undefined) continue;
      const state = stateAt({
        kind,
        date,
        today,
        status: action.data.status,
        successorId: action.data.successor_id,
        somethingOnDate: onRecord.has(`${action.data.jurisdiction_id} ${date}`),
      });
      dates.push({ kind, date, state, anchor: anchorOf(action.id, kind), action });
    }
  }
  return dates.sort(
    (a, b) =>
      a.date.localeCompare(b.date) ||
      kinds.indexOf(a.kind) - kinds.indexOf(b.kind) ||
      a.action.id.localeCompare(b.action.id),
  );
}

/** Adds `days` to a YYYY-MM-DD date. */
function addDays(isoDate, days) {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/**
 * The /ahead/ page's sections (ADR-0010 Decision 6): upcoming dates within the radar's window (the export's Eastern
 * date and the 30 days after it), soonest first; later upcoming dates; dates passed with nothing recorded, newest
 * first; and dates whose outcome is on the record, newest first, for 90 days after the date. Ended dates are left out.
 * @template {{ date: string, state: string }} K
 * @param {readonly K[]} dates as knownDates returns them
 * @param {string} generatedUtc
 */
export function aheadSections(dates, generatedUtc) {
  const today = easternIso(generatedUtc).slice(0, 10);
  const windowEnd = addDays(today, WINDOW_DAYS);
  const keepFrom = addDays(today, -RECORDED_KEEP_DAYS);
  const upcoming = dates.filter((k) => k.state === 'upcoming');
  return {
    next: upcoming.filter((k) => k.date <= windowEnd),
    later: upcoming.filter((k) => k.date > windowEnd),
    passed: dates.filter((k) => k.state === 'passed_unrecorded').reverse(),
    recorded: dates.filter((k) => k.state === 'recorded' && k.date >= keepFrom).reverse(),
    windowEnd,
  };
}

/**
 * The soonest upcoming known date, for the home page when the radar's window is empty; null when there is none.
 * @template {{ state: string }} K
 * @param {readonly K[]} dates as knownDates returns them
 * @returns {K | null}
 */
export function soonestUpcoming(dates) {
  return dates.find((k) => k.state === 'upcoming') ?? null;
}

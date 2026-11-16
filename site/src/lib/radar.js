// Build-time rules for the Hearing Radar (SPEC AC1, AC2, AC10). Plain JS so node:test can import it directly.
// Nothing here runs in the browser: the site ships no script.

/** The radar lists items starting within this many days of the export time (SPEC AC1). */
export const WINDOW_DAYS = 30;

const DAY_MS = 24 * 60 * 60 * 1000;
const ZONE = 'America/New_York';

/**
 * Eastern wall-clock text with its zone, for example "Thu, Nov 5, 2026, 5:30 PM EST" (SPEC AC2, AC10).
 * @param {string} utc an ISO 8601 UTC instant
 */
export function easternText(utc) {
  return new Intl.DateTimeFormat('en-US', {
    timeZone: ZONE,
    weekday: 'short',
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(parseUtc(utc));
}

/**
 * The same instant as an ISO 8601 string with its Eastern offset, for the `datetime` attribute and feeds, for
 * example "2026-11-05T17:30:00-05:00".
 * @param {string} utc
 */
export function easternIso(utc) {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-US', {
      timeZone: ZONE,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      timeZoneName: 'longOffset',
    })
      .formatToParts(parseUtc(utc))
      .map((part) => [part.type, part.value]),
  );
  const offset = parts.timeZoneName === 'GMT' ? '+00:00' : parts.timeZoneName.slice(3);
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}${offset}`;
}

/**
 * True when the event starts in [generated, generated + 30 days] (SPEC AC1). Cancelled meetings stay listed so
 * subscribers see the cancellation (AC4).
 * @param {{ starts_utc: string }} event
 * @param {string} generatedUtc events.json's generated_utc: "D" for the window, so a build is reproducible
 */
export function inWindow(event, generatedUtc) {
  const start = parseUtc(event.starts_utc).getTime();
  const from = parseUtc(generatedUtc).getTime();
  return start >= from && start <= from + WINDOW_DAYS * DAY_MS;
}

/**
 * Entries sorted by start time, then ID, so ties are stable. Works on collection entries (`entry.data`) and on raw
 * events alike.
 * @template {{ id: string, data?: { starts_utc: string }, starts_utc?: string }} T
 * @param {readonly T[]} entries
 * @returns {T[]}
 */
export function byStart(entries) {
  const start = (/** @type {T} */ entry) => entry.data?.starts_utc ?? entry.starts_utc ?? '';
  return [...entries].sort((a, b) => start(a).localeCompare(start(b)) || a.id.localeCompare(b.id));
}

/** Visible status text (SPEC AC4: "Cancelled" is text, never only a color or icon). */
export function statusText(status) {
  return status === 'cancelled' ? 'Cancelled' : 'Scheduled';
}

/** How an item is sourced (SPEC AC6), as shown to readers. */
export function labelText(label) {
  return label === 'confirmed_official' ? 'Confirmed (official record)' : 'Reported (news)';
}

/** What readers are told about a copy a person saved; the feed and calendars say the same (RetrievalNote.cs). */
export const MANUAL_RETRIEVAL_TEXT =
  'Saved by a person in a browser (the source does not allow automated downloads)';

/**
 * How a source's copy was obtained, when that needs saying (issue #113): the sentence above for `manual`, null for
 * `fetched`. Throws on a token the site doesn't know, so a new one can't pass as fetched.
 * @param {string} retrieval
 * @returns {string | null}
 */
export function retrievalText(retrieval) {
  if (retrieval === 'manual') {
    return MANUAL_RETRIEVAL_TEXT;
  }
  if (retrieval === 'fetched') {
    return null;
  }
  throw new Error(`Unknown retrieval ${JSON.stringify(retrieval)}.`);
}

const ACTION_TYPES = {
  moratorium: 'Moratorium',
  rezoning: 'Rezoning',
  text_amendment: 'Ordinance text amendment',
  resolution: 'Resolution',
};

const OUTCOMES = {
  adopted: 'Adopted',
  adopted_amended: 'Adopted with amendments',
  failed: 'Failed',
  continued: 'Continued to a later meeting',
  tabled: 'Tabled',
};

/** What kind of decision, in words (issue #25). Throws on a token the site doesn't know, so a new one can't render blank. */
export function actionTypeText(actionType) {
  return lookup(ACTION_TYPES, actionType, 'action type');
}

/** The recorded outcome, in plain words; the same wording for every jurisdiction (SPEC §E Neutrality). */
export function outcomeText(outcome) {
  return lookup(OUTCOMES, outcome, 'outcome');
}

/** "8 in favor, 1 against" from "8-1", or null when no vote was recorded. */
export function votesText(votes) {
  if (votes === null) {
    return null;
  }
  const match = /^(\d{1,2})-(\d{1,2})$/.exec(votes);
  if (!match) {
    throw new Error(`Expected votes like "9-0", got ${JSON.stringify(votes)}.`);
  }
  return `${match[1]} in favor, ${match[2]} against`;
}

/**
 * The vote line for a decision: the tally, marked unanimous when it was; "Unanimous (tally not yet recorded)" when
 * coverage reported a unanimous vote before the platform posted the count; null when nothing is known.
 */
export function voteText(votes, unanimous) {
  const tally = votesText(votes);
  if (!unanimous) {
    return tally;
  }
  return tally === null ? 'Unanimous (tally not yet recorded)' : `${tally} (unanimous)`;
}

/**
 * A calendar date such as "Thu, Nov 5, 2026". Decisions are dated, not timed, so there is no zone to show.
 * @param {string} isoDate YYYY-MM-DD
 */
export function dateText(isoDate) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(isoDate)) {
    throw new Error(`Expected a date like 2026-11-05, got ${JSON.stringify(isoDate)}.`);
  }
  return new Intl.DateTimeFormat('en-US', {
    timeZone: 'UTC',
    weekday: 'short',
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(`${isoDate}T12:00:00Z`));
}

/**
 * Entries sorted newest decision first, then ID, so ties are stable.
 * @template {{ id: string, data: { decided_on: string } }} T
 * @param {readonly T[]} entries
 * @returns {T[]}
 */
export function byDecisionNewestFirst(entries) {
  return [...entries].sort(
    (a, b) => b.data.decided_on.localeCompare(a.data.decided_on) || a.id.localeCompare(b.id),
  );
}

/**
 * What a jurisdiction's freshness line says (SPEC AC10): "last checked" in Eastern, and a text flag after 48 hours.
 * @param {{ last_checked_utc: string | null, stale: boolean }} jurisdiction
 */
export function freshnessText(jurisdiction) {
  const checked =
    jurisdiction.last_checked_utc === null
      ? 'Never checked'
      : `Last checked ${easternText(jurisdiction.last_checked_utc)}`;
  const window = staleWindowText(jurisdiction.stale_after_hours ?? 48);
  return jurisdiction.stale ? `${checked} ⚠ Stale: no successful check in ${window}` : checked;
}

/**
 * The stale threshold in words: "48 hours" for an automated jurisdiction, "7 days" for one checked by hand (SPEC
 * AC10, issue #27). Whole days past two days read as days.
 * @param {number} hours
 */
export function staleWindowText(hours) {
  return hours > 48 && hours % 24 === 0 ? `${hours / 24} days` : `${hours} hours`;
}

/**
 * The instant after which a jurisdiction counts as stale if nothing checks it again: last check plus its threshold
 * (SPEC AC10). The stale flag itself is decided at build time, so a build that stops being replaced (a failing
 * publish, issue #103) can't flag itself; printing this deadline lets a reader judge freshness against their own
 * clock without any script.
 * @param {string} lastCheckedUtc an ISO 8601 UTC instant
 * @param {number} hours the jurisdiction's stale_after_hours
 * @returns {string} an ISO 8601 UTC instant, for example "2026-10-27T20:17:00Z"
 */
export function staleAfterUtc(lastCheckedUtc, hours) {
  return new Date(parseUtc(lastCheckedUtc).getTime() + hours * 60 * 60 * 1000)
    .toISOString()
    .replace('.000Z', 'Z');
}

const COVERAGE_TIERS = {
  watched:
    'Coverage: hearings watched. Upcoming meetings and hearings are on the Hearing Radar, and decisions are recorded.',
  decisions:
    'Coverage: decisions only. Decisions are recorded after they are made. Upcoming meetings and hearings are not watched, so there is no calendar and no "last checked" date.',
  registered:
    'Coverage: listed only. Meetings, hearings and decisions are not recorded for this jurisdiction.',
};

/**
 * How closely a jurisdiction is covered, in one sentence (issue #115). Throws on a tier the site doesn't know.
 * @param {string} tier
 */
export function coverageText(tier) {
  return lookup(COVERAGE_TIERS, tier, 'coverage tier');
}

/**
 * Whether the freshness promise (SPEC AC10) is made: only for a watched jurisdiction (issue #115). Mirrors
 * DcLedger.Domain.Freshness.AppliesTo.
 * @param {string} tier
 */
export function isWatched(tier) {
  coverageText(tier);
  return tier === 'watched';
}

const ACTION_STATUSES = {
  in_effect: 'In effect',
  expired: 'Expired',
  superseded: 'Replaced or extended by a later decision',
  repealed: 'Repealed',
};

/** Where an adopted decision stands, in words (issue #115). Same wording as the feed (RssFeedWriter.StatusText). */
export function actionStatusText(status) {
  return lookup(ACTION_STATUSES, status, 'action status');
}

/**
 * The terms a decision's record states (issue #115), in display order, each as a definition-list row. Text terms are
 * quoted because they are copied from the document as written; dates are calendar dates. The labels match the feed
 * (RssFeedWriter.TermLines). A term the record leaves out (null or undefined) has no row.
 * @param {{ instrument_number?: string | null, threshold?: string | null, scope?: string | null,
 *   effective_on?: string | null, expires_on?: string | null, early_termination?: string | null,
 *   exemptions?: string | null, status?: string | null }} terms
 * @returns {{ label: string, text: string, date?: string }[]}
 */
export function termRows(terms) {
  const rows = [];
  const quoted = (text) => `\u201C${text}\u201D`;
  const present = (value) => value !== null && value !== undefined;
  if (present(terms.status)) rows.push({ label: 'Status', text: actionStatusText(terms.status) });
  if (present(terms.instrument_number)) {
    rows.push({ label: 'Ordinance or resolution number', text: terms.instrument_number });
  }
  if (present(terms.threshold)) rows.push({ label: 'Threshold', text: quoted(terms.threshold) });
  if (present(terms.scope)) rows.push({ label: 'Covers', text: quoted(terms.scope) });
  if (present(terms.effective_on)) {
    rows.push({ label: 'Effective', text: dateText(terms.effective_on), date: terms.effective_on });
  }
  if (present(terms.expires_on)) {
    rows.push({ label: 'Expires', text: dateText(terms.expires_on), date: terms.expires_on });
  }
  if (present(terms.early_termination)) {
    rows.push({ label: 'Ends earlier', text: quoted(terms.early_termination) });
  }
  if (present(terms.exemptions)) rows.push({ label: 'Exemptions', text: quoted(terms.exemptions) });
  return rows;
}

/**
 * @param {Record<string, string>} table
 * @param {string} token
 * @param {string} what
 */
function lookup(table, token, what) {
  if (!Object.hasOwn(table, token)) {
    throw new Error(`Unknown ${what} ${JSON.stringify(token)}.`);
  }
  return table[token];
}

/** @param {string} utc */
function parseUtc(utc) {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/.test(utc)) {
    throw new Error(
      `Expected a UTC instant like 2026-11-05T22:30:00Z, got ${JSON.stringify(utc)}.`,
    );
  }
  return new Date(utc);
}

/**
 * Seats for a recorded tally (issue #133): "8-1" gives eight "aye" then one "no". No tally, no seats: a vote
 * reported unanimous without a count is never drawn from the size of the board.
 * @param {string | null} votes
 * @returns {('aye' | 'no')[]}
 */
export function seatList(votes) {
  if (votes === null || votes === undefined) {
    return [];
  }
  const match = /^(\d{1,2})-(\d{1,2})$/.exec(votes);
  if (!match) {
    throw new Error(`Expected votes like "9-0", got ${JSON.stringify(votes)}.`);
  }
  return [...Array(Number(match[1])).fill('aye'), ...Array(Number(match[2])).fill('no')];
}

/**
 * The hash glyph's cells (issue #133): each of the 64 hex digits as a shade from 0 to 3 (its value divided by 4).
 * @param {string} sha256 64 lowercase hex digits
 * @returns {number[]}
 */
export function glyphCells(sha256) {
  if (!/^[0-9a-f]{64}$/.test(sha256)) {
    throw new Error(
      `Expected a SHA-256 of 64 lowercase hex digits, got ${JSON.stringify(sha256)}.`,
    );
  }
  return [...sha256].map((digit) => Math.floor(parseInt(digit, 16) / 4));
}

/**
 * The radar window's days in Eastern time (issue #133): the export's Eastern date and the 30 days after it, each with
 * its weekday initial, day of month, a month label on the first day and on each 1st, and how many items start on it.
 * @param {string} generatedUtc
 * @param {string[]} startsUtc the start of each item in the window
 */
export function windowDays(generatedUtc, startsUtc) {
  const dateOf = (utc) => easternIso(utc).slice(0, 10);
  const counts = new Map();
  for (const start of startsUtc) {
    counts.set(dateOf(start), (counts.get(dateOf(start)) ?? 0) + 1);
  }
  const [y, m, d] = dateOf(generatedUtc).split('-').map(Number);
  const days = [];
  for (let i = 0; i <= WINDOW_DAYS; i++) {
    const date = new Date(Date.UTC(y, m - 1, d + i));
    const iso = date.toISOString().slice(0, 10);
    const weekday = date.getUTCDay();
    days.push({
      iso,
      day: date.getUTCDate(),
      weekday: 'SMTWTFS'[weekday],
      weekend: weekday === 0 || weekday === 6,
      today: i === 0,
      month:
        i === 0 || date.getUTCDate() === 1
          ? new Intl.DateTimeFormat('en-US', { month: 'short', timeZone: 'UTC' }).format(date)
          : null,
      items: counts.get(iso) ?? 0,
    });
  }
  return days;
}

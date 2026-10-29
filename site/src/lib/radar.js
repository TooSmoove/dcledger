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

/**
 * What a jurisdiction's freshness line says (SPEC AC10): "last checked" in Eastern, and a text flag after 48 hours.
 * @param {{ last_checked_utc: string | null, stale: boolean }} jurisdiction
 */
export function freshnessText(jurisdiction) {
  const checked =
    jurisdiction.last_checked_utc === null
      ? 'Never checked'
      : `Last checked ${easternText(jurisdiction.last_checked_utc)}`;
  return jurisdiction.stale ? `${checked} ⚠ Stale: no successful check in 48 hours` : checked;
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

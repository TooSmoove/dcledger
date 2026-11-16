// The corrections log on /methodology/ (issue #117). Contract with `dcl export`'s CorrectionsExporter: plain JS so
// node:test can import it directly, and checked by hand rather than with a content collection, because the file is
// one log, not a set of pages.
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

/** The only corrections.json schema version this site understands. Bump together with the exporter. */
export const SUPPORTED_CORRECTIONS_VERSION = 1;

/** Where `dcl export` writes the log, relative to the site/ folder (ADR-0001). */
export const CORRECTIONS_FILE = '../out/site-data/corrections.json';

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/**
 * @typedef {{ id: string, reported_on: string, corrected_on: string, record_ids: string[], summary: string, report: number | null }} Correction
 * @typedef {{ schema_version: number, opened_on: string, corrections: Correction[] }} CorrectionLog
 */

/**
 * Parses corrections.json and rejects anything the page wasn't built for, so a malformed log fails the build instead
 * of rendering a partial one.
 * @param {string} text
 * @returns {CorrectionLog}
 */
export function parseCorrections(text) {
  const data = JSON.parse(text);
  if (data?.schema_version !== SUPPORTED_CORRECTIONS_VERSION) {
    throw new Error(
      `corrections.json schema_version ${String(data?.schema_version)} is not supported; ` +
        `this site expects ${SUPPORTED_CORRECTIONS_VERSION}.`,
    );
  }
  if (typeof data.opened_on !== 'string' || !DATE.test(data.opened_on)) {
    throw new Error(
      `corrections.json opened_on must be a date, got ${JSON.stringify(data.opened_on)}.`,
    );
  }
  if (!Array.isArray(data.corrections)) {
    throw new Error('corrections.json corrections must be a list.');
  }
  data.corrections.forEach(checkEntry);
  return data;
}

/** @param {unknown} entry @param {number} index */
function checkEntry(entry, index) {
  const where = `corrections.json entry ${index}`;
  const e = /** @type {Record<string, unknown>} */ (entry ?? {});
  if (typeof e.id !== 'string' || !SLUG.test(e.id)) {
    throw new Error(`${where}: id must be a slug.`);
  }
  for (const key of ['reported_on', 'corrected_on']) {
    if (typeof e[key] !== 'string' || !DATE.test(/** @type {string} */ (e[key]))) {
      throw new Error(`${where} (${e.id}): ${key} must be a date.`);
    }
  }
  if (!Array.isArray(e.record_ids) || !e.record_ids.every((id) => typeof id === 'string')) {
    throw new Error(`${where} (${e.id}): record_ids must be a list of IDs.`);
  }
  if (typeof e.summary !== 'string' || e.summary.trim() === '') {
    throw new Error(`${where} (${e.id}): summary is required.`);
  }
  if (e.report !== null && !(Number.isInteger(e.report) && /** @type {number} */ (e.report) > 0)) {
    throw new Error(`${where} (${e.id}): report must be a positive whole number or null.`);
  }
}

/** Reads and parses the exported log; resolved against site/, like the content collections' file loader. */
export async function readCorrections() {
  return parseCorrections(await readFile(resolve(CORRECTIONS_FILE), 'utf8'));
}

/**
 * Entries newest correction first, then ID, so ties are stable. The exporter writes them oldest first.
 * @param {readonly Correction[]} corrections
 * @returns {Correction[]}
 */
export function newestFirst(corrections) {
  return [...corrections].sort(
    (a, b) => b.corrected_on.localeCompare(a.corrected_on) || a.id.localeCompare(b.id),
  );
}

/**
 * Where a corrected record is shown: an event's item page, a jurisdiction's page, or the decision on its jurisdiction's
 * page. The exporter only writes IDs the quality gate resolved, so an unknown ID means the site data and the log
 * disagree, and the build stops.
 * @param {string} id
 * @param {{ events: Set<string>, jurisdictions: Set<string>, actions: Map<string, string> }} records
 *   actions maps a decision's ID to its jurisdiction's ID
 * @returns {string}
 */
export function recordHref(id, records) {
  if (records.events.has(id)) {
    return `/items/${id}/`;
  }
  if (records.jurisdictions.has(id)) {
    return `/jurisdictions/${id}/`;
  }
  const jurisdiction = records.actions.get(id);
  if (jurisdiction !== undefined) {
    return `/jurisdictions/${jurisdiction}/#decision-${id}`;
  }
  throw new Error(
    `Correction names '${id}', which isn't an event, jurisdiction or decision in the site data.`,
  );
}

/**
 * How the log names the form report a correction came from, for example R-131: the reference the reporter sees in a
 * reply. Plain text, not a link; reports themselves are private.
 * @param {number} report
 */
export function reportReference(report) {
  return `R-${report}`;
}

// Contract with `dcl export` (docs/adr/0004-contracts.md). Plain JS so node:test can import it directly.

/** The only events.json schema version this site understands. Bump together with the exporter. */
export const SUPPORTED_SCHEMA_VERSION = 1;

/** Where `dcl export` writes site data, relative to the site/ folder (ADR-0001). */
export const SITE_DATA_FILE = '../out/site-data/events.json';

/**
 * Parses events.json and rejects any schema version the site wasn't built for.
 * @param {string} text
 * @returns {{ schema_version: number, generated_utc: string, jurisdictions: Record<string, unknown>[], events: Record<string, unknown>[] }}
 */
export function parseSiteData(text) {
  const data = JSON.parse(text);
  if (data?.schema_version !== SUPPORTED_SCHEMA_VERSION) {
    throw new Error(
      `events.json schema_version ${String(data?.schema_version)} is not supported; ` +
        `this site expects ${SUPPORTED_SCHEMA_VERSION}.`,
    );
  }
  return data;
}

/** @param {string} text */
export const eventsFrom = (text) => parseSiteData(text).events;

/** @param {string} text */
export const jurisdictionsFrom = (text) => parseSiteData(text).jurisdictions;

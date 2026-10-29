// Reads the top-level fields of events.json that the content collections don't carry (ADR-0004).
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { SITE_DATA_FILE, parseSiteData } from './site-data.js';

/** events.json's generated_utc: "D" for the 30-day window, and the "as of" time shown on the radar. */
export async function generatedUtc() {
  // Resolved against the project root (site/), the same way Astro's file() loader resolves SITE_DATA_FILE.
  const text = await readFile(resolve(SITE_DATA_FILE), 'utf8');
  return parseSiteData(text).generated_utc;
}

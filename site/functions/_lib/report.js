// Validating and storing a report from the site's form (/report/). Plain JS so node:test imports it directly. Everything
// a visitor sends is untrusted (ADR-0003): it is checked here, stored as text, and escaped wherever it is shown.

/** What a report is about. Mirrors the radio buttons on /report/ and the CHECK in site/db/reports-schema.sql. */
export const KINDS = Object.freeze({
  correction: 'Something on the site is wrong',
  question: 'A question',
  stop_fetching: 'Stop the fetcher visiting a site',
  other: 'Something else',
});

/** Where a report stands in the inbox. Mirrors the CHECK in site/db/reports-schema.sql. */
export const STATUSES = Object.freeze({
  new: 'New',
  waiting: 'Waiting on a record',
  corrected: 'Corrected',
  stands: 'Record stands (replied)',
  replied: 'Answered',
  closed: 'Closed (spam or not actionable)',
});

/** Statuses that still need something from the maintainer; the inbox shows these by default. */
export const OPEN_STATUSES = Object.freeze(['new', 'waiting']);

export const LIMITS = Object.freeze({
  bodyBytes: 20_000,
  item: 300,
  message: 5000,
  source: 1000,
  email: 254,
  note: 2000,
  perHour: 5,
});

/** The form's hidden trap field. People never see it; simple bots fill it in. */
export const HONEYPOT = 'website';

// Deliberately loose: one @, no spaces, a dot in the domain. The real check is whether a reply arrives.
const EMAIL = /^[^\s@<>"',;]+@[^\s@<>"',;]+\.[^\s@<>"',;]+$/;

/** Removes control characters except tab and newline, and turns CRLF and CR into LF. */
export function cleanText(value) {
  return String(value ?? '')
    .replaceAll('\r\n', '\n')
    .replaceAll('\r', '\n')
    .replace(/[\u0000-\u0008\u000b-\u001f\u007f-\u009f\u2028\u2029]/g, '')
    .trim();
}

/** True for an absolute http or https address. */
export function isWebAddress(value) {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' || url.protocol === 'http:';
  } catch {
    return false;
  }
}

/**
 * Checks a submitted form. Returns `{ spam: true }` when the trap field is filled, `{ errors }` listing what to fix in
 * plain words, or `{ report }` ready to store.
 * @param {URLSearchParams} form
 */
export function parseReport(form) {
  if (cleanText(form.get(HONEYPOT)) !== '') {
    return { spam: true };
  }
  const kind = cleanText(form.get('kind'));
  const item = cleanText(form.get('item'));
  const message = cleanText(form.get('message'));
  const source = cleanText(form.get('source'));
  const email = cleanText(form.get('email'));

  const errors = [];
  if (!Object.hasOwn(KINDS, kind)) {
    errors.push('Choose what your report is about.');
  }
  if (message === '') {
    errors.push('Tell us what is wrong or what you need.');
  } else if (message.length > LIMITS.message) {
    errors.push(`Keep the message under ${LIMITS.message} characters.`);
  }
  if (item.length > LIMITS.item) {
    errors.push(`Keep the page or item under ${LIMITS.item} characters.`);
  }
  if (source !== '' && (source.length > LIMITS.source || !isWebAddress(source))) {
    errors.push('The record link must be a web address starting with http:// or https://.');
  }
  if (email !== '' && (email.length > LIMITS.email || !EMAIL.test(email))) {
    errors.push('Check the email address, or leave it empty.');
  }
  if (errors.length > 0) {
    return { errors };
  }
  return {
    report: {
      kind,
      item: item || null,
      message,
      source: source || null,
      email: email || null,
    },
  };
}

/** HMAC-SHA256 of the visitor's IP address, hex. Only the hash is stored, and only for a day (rate limiting). */
export async function hashAddress(key, address) {
  const encoder = new TextEncoder();
  const cryptoKey = await crypto.subtle.importKey(
    'raw',
    encoder.encode(key),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const signature = await crypto.subtle.sign('HMAC', cryptoKey, encoder.encode(address));
  return [...new Uint8Array(signature)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

/** An ISO 8601 UTC string `hours` before `now`, in the table's format. */
export function hoursBefore(now, hours) {
  return new Date(new Date(now).getTime() - hours * 3_600_000)
    .toISOString()
    .replace(/\.\d{3}Z$/, 'Z');
}

/**
 * Stores a report unless this address sent too many in the last hour. Returns `{ id }` or `{ limited: true }`.
 * Also forgets the address hash on reports older than a day, so nothing ties a report to a visitor for long.
 * @param {D1Like} db
 * @param {{ kind: string, item: string | null, message: string, source: string | null, email: string | null }} report
 * @param {string} ipHash
 * @param {string} now ISO 8601 UTC
 * @typedef {{ prepare(sql: string): { bind(...values: unknown[]): { first(): Promise<any>, run(): Promise<any>, all(): Promise<{ results: any[] }> } } }} D1Like
 */
export async function storeReport(db, report, ipHash, now) {
  const recent = await db
    .prepare('SELECT COUNT(*) AS n FROM reports WHERE ip_hash = ?1 AND created_utc > ?2')
    .bind(ipHash, hoursBefore(now, 1))
    .first();
  if ((recent?.n ?? 0) >= LIMITS.perHour) {
    return { limited: true };
  }
  const inserted = await db
    .prepare(
      'INSERT INTO reports (created_utc, kind, item, message, source_url, email, ip_hash) ' +
        'VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7) RETURNING id',
    )
    .bind(now, report.kind, report.item, report.message, report.source, report.email, ipHash)
    .first();
  await db
    .prepare('UPDATE reports SET ip_hash = NULL WHERE ip_hash IS NOT NULL AND created_utc < ?1')
    .bind(hoursBefore(now, 24))
    .run();
  return { id: inserted.id };
}

/** The reference a reporter sees in a reply, and the corrections log cites: R-7. */
export function reference(id) {
  return `R-${id}`;
}

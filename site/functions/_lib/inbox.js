// The private inbox page (/inbox/): reports from the form, newest first, with a reply link and a status form. Plain JS
// so node:test imports it directly. Every value from a report is escaped; links are only made from addresses that
// passed the form's checks and are re-checked here.
import { escapeHtml } from './http.js';
import {
  KINDS,
  LIMITS,
  OPEN_STATUSES,
  STATUSES,
  cleanText,
  isWebAddress,
  reference,
} from './report.js';

/** The 48-hour promise on /methodology/#corrections, from when a report arrives. */
export const RESPONSE_HOURS = 48;

const EASTERN = new Intl.DateTimeFormat('en-US', {
  timeZone: 'America/New_York',
  weekday: 'short',
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
  timeZoneName: 'short',
});

/** A stored UTC time in Eastern, with its zone, for example "Mon, Nov 16, 2026, 9:05 AM EST". */
export function easternTime(utc) {
  return EASTERN.format(new Date(utc));
}

/** When a report has to be answered by. */
export function dueUtc(createdUtc) {
  return new Date(new Date(createdUtc).getTime() + RESPONSE_HOURS * 3_600_000)
    .toISOString()
    .replace(/\.\d{3}Z$/, 'Z');
}

/**
 * A mailto: link that starts a reply to the reporter from the maintainer's own mail program, quoting the report.
 * The site's domain sends no mail (SPEC §H), so replies come from wherever the maintainer writes them.
 */
export function replyHref(report) {
  const quoted = report.message
    .slice(0, 1500)
    .split('\n')
    .map((line) => `> ${line}`)
    .join('\n');
  const subject = `Your report ${reference(report.id)} to NC Data Center Ledger`;
  const body = `\n\n---\nOn ${easternTime(report.created_utc)} you wrote:\n${quoted}\n`;
  return (
    `mailto:${encodeURIComponent(report.email)}` +
    `?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`
  );
}

function link(address) {
  return isWebAddress(address)
    ? `<a href="${escapeHtml(address)}" rel="noreferrer noopener">${escapeHtml(address)}</a>`
    : escapeHtml(address);
}

function statusOptions(current) {
  return Object.entries(STATUSES)
    .map(
      ([value, text]) =>
        `<option value="${value}"${value === current ? ' selected' : ''}>${escapeHtml(text)}</option>`,
    )
    .join('');
}

function reportHtml(report, nowUtc) {
  const ref = reference(report.id);
  const due = dueUtc(report.created_utc);
  const rows = [
    ['About', escapeHtml(KINDS[report.kind] ?? report.kind)],
    ['Received', escapeHtml(easternTime(report.created_utc))],
    ['Status', escapeHtml(STATUSES[report.status] ?? report.status)],
  ];
  if (report.status === 'new') {
    rows.push([
      'Answer by',
      `<span class="due">${escapeHtml(easternTime(due))}${due < nowUtc ? ' (overdue)' : ''}</span>`,
    ]);
  }
  if (report.item) rows.push(['Page or item', link(report.item)]);
  if (report.source_url) rows.push(['Record', link(report.source_url)]);
  rows.push([
    'Reply',
    report.email
      ? `<a href="${escapeHtml(replyHref(report))}">Email ${escapeHtml(report.email)}</a>`
      : 'No email given: the answer is the corrections log.',
  ]);
  if (report.updated_utc) rows.push(['Updated', escapeHtml(easternTime(report.updated_utc))]);
  const dl = rows.map(([dt, dd]) => `<dt>${dt}</dt><dd>${dd}</dd>`).join('');
  return `<article class="report" id="r-${report.id}">
<h2>${ref}</h2>
<dl>${dl}</dl>
<pre>${escapeHtml(report.message)}</pre>
<form method="post" action="/inbox/">
<input type="hidden" name="id" value="${report.id}">
<label for="status-${report.id}">Status</label>
<select id="status-${report.id}" name="status">${statusOptions(report.status)}</select>
<label for="note-${report.id}">Private note (what you found, what you replied)</label>
<textarea id="note-${report.id}" name="note" rows="3" maxlength="${LIMITS.note}">${escapeHtml(report.note ?? '')}</textarea>
<p><button type="submit">Save ${ref}</button></p>
</form>
</article>`;
}

/**
 * The inbox body.
 * @param {any[]} reports rows from the reports table
 * @param {{ showAll: boolean, nowUtc: string, email?: string }} view
 */
export function inboxHtml(reports, view) {
  const heading = view.showAll ? 'All reports' : 'Open reports';
  const toggle = view.showAll
    ? '<a href="/inbox/">Show open reports only</a>'
    : '<a href="/inbox/?show=all">Show all reports</a>';
  const who = view.email ? ` Signed in as ${escapeHtml(view.email)}.` : '';
  const list =
    reports.length === 0
      ? '<p>No reports to show.</p>'
      : reports.map((r) => reportHtml(r, view.nowUtc)).join('\n');
  return `<h1>${heading}</h1>
<p class="muted">Reports from the form at /report/. Private: only people Cloudflare Access lets in can see this page.${who}</p>
<p>${toggle}</p>
${list}`;
}

/** The SQL and parameters that list reports: open ones by default, or every one, newest first, at most 200. */
export function listQuery(showAll) {
  if (showAll) {
    return { sql: 'SELECT * FROM reports ORDER BY id DESC LIMIT 200', params: [] };
  }
  const marks = OPEN_STATUSES.map((_, i) => `?${i + 1}`).join(', ');
  return {
    sql: `SELECT * FROM reports WHERE status IN (${marks}) ORDER BY id DESC LIMIT 200`,
    params: [...OPEN_STATUSES],
  };
}

/**
 * Checks a status update from the inbox form.
 * @param {URLSearchParams} form
 * @returns {{ id: number, status: string, note: string | null } | null}
 */
export function parseUpdate(form) {
  const id = Number(form.get('id'));
  const status = cleanText(form.get('status'));
  const note = cleanText(form.get('note'));
  if (
    !Number.isSafeInteger(id) ||
    id < 1 ||
    !Object.hasOwn(STATUSES, status) ||
    note.length > LIMITS.note
  ) {
    return null;
  }
  return { id, status, note: note || null };
}

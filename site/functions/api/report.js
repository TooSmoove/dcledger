// POST /api/report: the site's report form (/report/) posts here. Works without JavaScript: on success it redirects to
// /report/thanks/, on a mistake it answers with a page saying what to fix. Bindings (Pages project settings):
//   REPORTS          D1 database with site/db/reports-schema.sql applied
//   REPORT_HASH_KEY  secret used to hash sender IP addresses for the hourly limit
import { escapeHtml, htmlResponse, readForm, sameOrigin, seeOther, utcNow } from '../_lib/http.js';
import { LIMITS, hashAddress, parseReport, storeReport } from '../_lib/report.js';

const BACK =
  '<p><a href="/report/">Back to the form</a>. Your browser&#39;s back button keeps what you typed.</p>';

/** @param {{ request: Request, env: Record<string, any> }} context */
export async function onRequestPost({ request, env }) {
  return handleReport(request, env, new Date());
}

/**
 * The handler, with the clock passed in so tests can fix it.
 * @param {Request} request
 * @param {Record<string, any>} env
 * @param {Date} now
 */
export async function handleReport(request, env, now) {
  if (!sameOrigin(request)) {
    return htmlResponse(
      403,
      'Not sent',
      '<h1>Not sent</h1><p>Reports can only be sent from the form on this site.</p>' + BACK,
    );
  }
  if (!env.REPORTS || !env.REPORT_HASH_KEY) {
    return htmlResponse(
      503,
      'Form unavailable',
      '<h1>The form is unavailable</h1><p>Please try again later.</p>',
    );
  }
  const read = await readForm(request, LIMITS.bodyBytes);
  if (read.error === 'unsupported') {
    return htmlResponse(
      415,
      'Not sent',
      '<h1>Not sent</h1><p>Reports can only be sent from the form on this site.</p>' + BACK,
    );
  }
  if (read.error) {
    return htmlResponse(
      413,
      'Not sent',
      '<h1>Not sent</h1><p>The report was too long to accept.</p>' + BACK,
    );
  }
  const parsed = parseReport(read.form);
  if (parsed.spam) {
    // Look like success, so a bot learns nothing.
    return seeOther('/report/thanks/');
  }
  if (parsed.errors) {
    const items = parsed.errors.map((e) => `<li>${escapeHtml(e)}</li>`).join('');
    return htmlResponse(
      400,
      'Check your report',
      `<h1>Check your report</h1><ul>${items}</ul>${BACK}`,
    );
  }
  const address = request.headers.get('CF-Connecting-IP') ?? 'unknown';
  const ipHash = await hashAddress(env.REPORT_HASH_KEY, address);
  const stored = await storeReport(env.REPORTS, parsed.report, ipHash, utcNow(now));
  if (stored.limited) {
    return htmlResponse(
      429,
      'Too many reports',
      '<h1>Too many reports</h1><p>This connection sent several reports in the last hour. Please try again later.</p>',
    );
  }
  return seeOther('/report/thanks/');
}

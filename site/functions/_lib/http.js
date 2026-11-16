// Shared response helpers for the Pages Functions (/api/report and /inbox/). Plain JS so node:test imports it
// directly. _headers doesn't apply to a Function's response, so every response sets its own headers here.

/** Headers every Function response carries: noindex like the rest of the site (AC11), never cached, and locked down. */
export const BASE_HEADERS = Object.freeze({
  'X-Robots-Tag': 'noindex',
  'Cache-Control': 'no-store',
  'Referrer-Policy': 'no-referrer',
  'X-Content-Type-Options': 'nosniff',
  'Content-Security-Policy':
    "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'",
});

/** Escapes text for HTML element content and double-quoted attribute values. */
export function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/** A complete HTML response. `body` must already be escaped. */
export function htmlResponse(status, title, body) {
  const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>${escapeHtml(title)} · NC Data Center Ledger</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif;line-height:1.5}
body{margin:0 auto;padding:1rem 1rem 3rem;max-width:68rem}
a:focus-visible,button:focus-visible,select:focus-visible,textarea:focus-visible{outline:3px solid currentColor;outline-offset:2px}
.muted{opacity:.8}
.report{border-top:1px solid #8888;padding:1rem 0}
.report pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit;margin:.5rem 0}
.due{font-weight:600}
dl{display:grid;grid-template-columns:max-content 1fr;gap:.25rem 1rem}dt{font-weight:600}dd{margin:0;overflow-wrap:anywhere}
label{display:block;font-weight:600;margin-top:.5rem}textarea{width:100%;max-width:40rem}
</style>
</head>
<body>
<main>
${body}
</main>
</body>
</html>
`;
  return new Response(html, {
    status,
    headers: { ...BASE_HEADERS, 'Content-Type': 'text/html; charset=utf-8' },
  });
}

/** A 303 to a path on this site, so a refresh doesn't resubmit the form. */
export function seeOther(location) {
  return new Response(null, { status: 303, headers: { ...BASE_HEADERS, Location: location } });
}

/**
 * True when a POST came from a page on this site. Browsers send Origin on every form POST, so a missing or foreign
 * Origin is a cross-site or scripted submission.
 * @param {Request} request
 */
export function sameOrigin(request) {
  const origin = request.headers.get('Origin');
  return origin !== null && origin === new URL(request.url).origin;
}

/** Now as an ISO 8601 UTC string without milliseconds, the format the reports table stores and compares. */
export function utcNow(now = new Date()) {
  return now.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

/** Reads a form-encoded body, refusing anything larger than `maxBytes` before parsing. */
export async function readForm(request, maxBytes) {
  const type = request.headers.get('Content-Type') ?? '';
  if (!type.toLowerCase().startsWith('application/x-www-form-urlencoded')) {
    return { error: 'unsupported' };
  }
  const declared = Number(request.headers.get('Content-Length') ?? '0');
  if (declared > maxBytes) {
    return { error: 'too_large' };
  }
  const text = await request.text();
  if (new TextEncoder().encode(text).length > maxBytes) {
    return { error: 'too_large' };
  }
  return { form: new URLSearchParams(text) };
}

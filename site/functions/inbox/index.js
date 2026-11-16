// /inbox/: the maintainer's private list of reports from the form. Cloudflare Access must protect /inbox* (see
// docs/reports.md); this Function checks the Access token as well and refuses everything when it can't.
// Bindings (Pages project settings): REPORTS (D1), ACCESS_TEAM_DOMAIN, ACCESS_AUD.
import { verifyAccessToken } from '../_lib/access.js';
import { htmlResponse, readForm, sameOrigin, seeOther, utcNow } from '../_lib/http.js';
import { inboxHtml, listQuery, parseUpdate } from '../_lib/inbox.js';
import { LIMITS } from '../_lib/report.js';

async function signedIn(request, env, options) {
  if (!env.REPORTS) {
    return {
      response: htmlResponse(
        503,
        'Inbox unavailable',
        '<h1>Inbox unavailable</h1><p>REPORTS is not bound.</p>',
      ),
    };
  }
  const claims = await verifyAccessToken(
    request.headers.get('Cf-Access-Jwt-Assertion'),
    env,
    options,
  );
  if (!claims) {
    return { response: htmlResponse(403, 'Not allowed', '<h1>Not allowed</h1>') };
  }
  return { claims };
}

/** @param {{ request: Request, env: Record<string, any> }} context */
export async function onRequestGet({ request, env }) {
  return handleInboxGet(request, env);
}

/** @param {{ request: Request, env: Record<string, any> }} context */
export async function onRequestPost({ request, env }) {
  return handleInboxPost(request, env);
}

/**
 * @param {Request} request
 * @param {Record<string, any>} env
 * @param {{ fetcher?: typeof fetch, now?: Date }} [options]
 */
export async function handleInboxGet(request, env, options = {}) {
  const auth = await signedIn(request, env, options);
  if (auth.response) return auth.response;
  const showAll = new URL(request.url).searchParams.get('show') === 'all';
  const { sql, params } = listQuery(showAll);
  const { results } = await env.REPORTS.prepare(sql)
    .bind(...params)
    .all();
  const body = inboxHtml(results ?? [], {
    showAll,
    nowUtc: utcNow(options.now),
    email: typeof auth.claims.email === 'string' ? auth.claims.email : undefined,
  });
  return htmlResponse(200, 'Inbox', body);
}

/**
 * @param {Request} request
 * @param {Record<string, any>} env
 * @param {{ fetcher?: typeof fetch, now?: Date }} [options]
 */
export async function handleInboxPost(request, env, options = {}) {
  const auth = await signedIn(request, env, options);
  if (auth.response) return auth.response;
  if (!sameOrigin(request)) {
    return htmlResponse(403, 'Not allowed', '<h1>Not allowed</h1>');
  }
  const read = await readForm(request, LIMITS.bodyBytes);
  const update = read.form ? parseUpdate(read.form) : null;
  if (!update) {
    return htmlResponse(
      400,
      'Not saved',
      '<h1>Not saved</h1><p><a href="/inbox/">Back to the inbox</a></p>',
    );
  }
  await env.REPORTS.prepare(
    'UPDATE reports SET status = ?1, note = ?2, updated_utc = ?3 WHERE id = ?4',
  )
    .bind(update.status, update.note, utcNow(options.now), update.id)
    .run();
  return seeOther(`/inbox/#r-${update.id}`);
}

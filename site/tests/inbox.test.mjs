// The private inbox (ADR-0009): it refuses anyone without a valid Access token, escapes every reader-supplied value,
// shows the 48-hour deadline, builds a reply link, and saves only valid status updates from this site.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { handleInboxGet, handleInboxPost } from '../functions/inbox/index.js';
import { resetKeyCache } from '../functions/_lib/access.js';
import { dueUtc, inboxHtml, listQuery, parseUpdate, replyHref } from '../functions/_lib/inbox.js';
import { testD1 } from './helpers/d1.mjs';

const ORIGIN = 'https://ncdcledger.org';
const TEAM = 'https://ledger.cloudflareaccess.com';
const AUD = 'aud-tag-123';
const NOW = new Date('2026-11-16T14:00:00Z');

const pair = await crypto.subtle.generateKey(
  {
    name: 'RSASSA-PKCS1-v1_5',
    modulusLength: 2048,
    publicExponent: new Uint8Array([1, 0, 1]),
    hash: 'SHA-256',
  },
  true,
  ['sign', 'verify'],
);
const jwk = { ...(await crypto.subtle.exportKey('jwk', pair.publicKey)), kid: 'k1' };
const fetcher = async () => new Response(JSON.stringify({ keys: [jwk] }));
const b64url = (v) => Buffer.from(typeof v === 'string' ? v : v).toString('base64url');
async function accessToken() {
  const head = b64url(JSON.stringify({ alg: 'RS256', kid: 'k1' }));
  const body = b64url(
    JSON.stringify({
      iss: TEAM,
      aud: [AUD],
      exp: Math.floor(NOW / 1000) + 600,
      email: 'me@example.org',
    }),
  );
  const sig = await crypto.subtle.sign(
    'RSASSA-PKCS1-v1_5',
    pair.privateKey,
    new TextEncoder().encode(`${head}.${body}`),
  );
  return `${head}.${body}.${b64url(new Uint8Array(sig))}`;
}

function setup() {
  resetKeyCache();
  const REPORTS = testD1();
  const insert = REPORTS.sqlite.prepare(
    'INSERT INTO reports (created_utc, kind, item, message, source_url, email, status) VALUES (?, ?, ?, ?, ?, ?, ?)',
  );
  insert.run(
    '2026-11-14T13:00:00Z',
    'correction',
    'javascript:alert(1)',
    'The <script>alert(1)</script> vote\nwas 8-1 & not 9-0.',
    'https://example.gov/minutes.pdf',
    'reader@example.org',
    'new',
  );
  insert.run(
    '2026-11-16T09:00:00Z',
    'question',
    null,
    'When is the next hearing?',
    null,
    null,
    'new',
  );
  insert.run('2026-11-10T09:00:00Z', 'other', null, 'Old and settled', null, null, 'corrected');
  return { REPORTS, ACCESS_TEAM_DOMAIN: TEAM, ACCESS_AUD: AUD };
}
const options = { fetcher, now: NOW };

test('the inbox refuses a request without a valid Access token', async () => {
  const env = setup();
  const response = await handleInboxGet(new Request(`${ORIGIN}/inbox/`), env, options);
  assert.equal(response.status, 403);
  assert.doesNotMatch(await response.text(), /reader@example\.org/);
  const forged = new Request(`${ORIGIN}/inbox/`, {
    headers: { 'Cf-Access-Jwt-Assertion': 'a.b.c' },
  });
  assert.equal((await handleInboxGet(forged, env, options)).status, 403);
});

test('signed in, the inbox lists open reports newest first, escaped, with deadlines', async () => {
  const env = setup();
  const request = new Request(`${ORIGIN}/inbox/`, {
    headers: { 'Cf-Access-Jwt-Assertion': await accessToken() },
  });
  const response = await handleInboxGet(request, env, options);
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('X-Robots-Tag'), 'noindex');
  assert.equal(response.headers.get('Cache-Control'), 'no-store');
  const html = await response.text();
  assert.ok(html.indexOf('id="r-2"') < html.indexOf('id="r-1"'), 'newest first');
  assert.doesNotMatch(html, /id="r-3"/, 'settled reports are hidden by default');
  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /The &lt;script&gt;alert\(1\)&lt;\/script&gt; vote\nwas 8-1 &amp; not 9-0\./);
  assert.doesNotMatch(html, /href="javascript:/, 'only web addresses become links');
  assert.match(html, /href="https:\/\/example\.gov\/minutes\.pdf" rel="noreferrer noopener"/);
  assert.match(html, /\(overdue\)/, 'R-1 arrived more than 48 hours ago');
  assert.match(html, /No email given/);
  assert.match(html, /Signed in as me@example\.org/);
});

test('?show=all lists settled reports too', async () => {
  const env = setup();
  const request = new Request(`${ORIGIN}/inbox/?show=all`, {
    headers: { 'Cf-Access-Jwt-Assertion': await accessToken() },
  });
  assert.match(await (await handleInboxGet(request, env, options)).text(), /id="r-3"/);
});

test('a status update from the inbox is saved; one from elsewhere or invalid is not', async () => {
  const env = setup();
  const headers = {
    'Cf-Access-Jwt-Assertion': await accessToken(),
    'Content-Type': 'application/x-www-form-urlencoded',
    Origin: ORIGIN,
  };
  const body = new URLSearchParams({
    id: '1',
    status: 'corrected',
    note: 'Fixed the vote.',
  }).toString();
  const ok = await handleInboxPost(
    new Request(`${ORIGIN}/inbox/`, { method: 'POST', headers, body }),
    env,
    options,
  );
  assert.equal(ok.status, 303);
  assert.equal(ok.headers.get('Location'), '/inbox/#r-1');
  const row = env.REPORTS.sqlite
    .prepare('SELECT status, note, updated_utc FROM reports WHERE id = 1')
    .get();
  assert.deepEqual(
    { ...row },
    { status: 'corrected', note: 'Fixed the vote.', updated_utc: '2026-11-16T14:00:00Z' },
  );

  const foreign = { ...headers, Origin: 'https://evil.example' };
  const bad = await handleInboxPost(
    new Request(`${ORIGIN}/inbox/`, {
      method: 'POST',
      headers: foreign,
      body: body.replace('corrected', 'closed'),
    }),
    env,
    options,
  );
  assert.equal(bad.status, 403);
  const invalid = new URLSearchParams({ id: '1', status: 'deleted' }).toString();
  const refused = await handleInboxPost(
    new Request(`${ORIGIN}/inbox/`, { method: 'POST', headers, body: invalid }),
    env,
    options,
  );
  assert.equal(refused.status, 400);
  assert.equal(
    env.REPORTS.sqlite.prepare('SELECT status FROM reports WHERE id = 1').get().status,
    'corrected',
  );
});

test('an update without Access is refused before anything is read', async () => {
  const env = setup();
  const body = new URLSearchParams({ id: '1', status: 'closed' }).toString();
  const response = await handleInboxPost(
    new Request(`${ORIGIN}/inbox/`, {
      method: 'POST',
      headers: { Origin: ORIGIN, 'Content-Type': 'application/x-www-form-urlencoded' },
      body,
    }),
    env,
    options,
  );
  assert.equal(response.status, 403);
  assert.equal(
    env.REPORTS.sqlite.prepare('SELECT status FROM reports WHERE id = 1').get().status,
    'new',
  );
});

test('the reply link quotes the report and names it in the subject', () => {
  const href = replyHref({
    id: 7,
    email: 'reader+x@example.org',
    created_utc: '2026-11-16T14:00:00Z',
    message: 'Line one\nLine two',
  });
  assert.ok(href.startsWith('mailto:reader%2Bx%40example.org?subject='));
  const params = new URLSearchParams(href.slice(href.indexOf('?') + 1));
  assert.equal(params.get('subject'), 'Your report R-7 to NC Data Center Ledger');
  assert.match(params.get('body'), /you wrote:\n> Line one\n> Line two/);
  assert.match(params.get('body'), /Nov 16, 2026, 9:00 AM EST/);
});

test('deadlines are 48 hours after a report arrives', () => {
  assert.equal(dueUtc('2026-11-16T14:00:00Z'), '2026-11-18T14:00:00Z');
});

test('status updates accept only known statuses and real report numbers', () => {
  assert.deepEqual(parseUpdate(new URLSearchParams({ id: '3', status: 'stands', note: ' ' })), {
    id: 3,
    status: 'stands',
    note: null,
  });
  for (const form of [
    { id: '0', status: 'new' },
    { id: 'x', status: 'new' },
    { id: '1.5', status: 'new' },
    { id: '1', status: 'gone' },
    { id: '1', status: 'new', note: 'x'.repeat(2001) },
  ]) {
    assert.equal(parseUpdate(new URLSearchParams(form)), null, JSON.stringify(form));
  }
});

test('the default list shows only open reports', () => {
  assert.deepEqual(listQuery(false).params, ['new', 'waiting']);
  assert.deepEqual(listQuery(true).params, []);
  assert.match(
    inboxHtml([], { showAll: false, nowUtc: '2026-11-16T14:00:00Z' }),
    /No reports to show/,
  );
});

// The Cloudflare Access check in front of /inbox/ (ADR-0009): only a token signed by the team's key, for this
// application, unexpired, gets through; anything else, including missing settings, fails closed.
import assert from 'node:assert/strict';
import { beforeEach, test } from 'node:test';
import { resetKeyCache, teamOrigin, verifyAccessToken } from '../functions/_lib/access.js';

const TEAM = 'https://ledger.cloudflareaccess.com';
const AUD = 'aud-tag-123';
const NOW = new Date('2026-11-16T14:00:00Z');
const nowSeconds = Math.floor(NOW.getTime() / 1000);
const env = { ACCESS_TEAM_DOMAIN: TEAM, ACCESS_AUD: AUD };

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
const otherPair = await crypto.subtle.generateKey(
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

const b64url = (bytes) => Buffer.from(bytes).toString('base64url');
const json = (value) => b64url(new TextEncoder().encode(JSON.stringify(value)));

async function token(claims = {}, { kid = 'k1', alg = 'RS256', key = pair.privateKey } = {}) {
  const head = json({ alg, kid, typ: 'JWT' });
  const body = json({
    iss: TEAM,
    aud: [AUD],
    exp: nowSeconds + 600,
    email: 'me@example.org',
    ...claims,
  });
  const signature = await crypto.subtle.sign(
    'RSASSA-PKCS1-v1_5',
    key,
    new TextEncoder().encode(`${head}.${body}`),
  );
  return `${head}.${body}.${b64url(new Uint8Array(signature))}`;
}

let fetched = [];
const fetcher = async (url) => {
  fetched.push(url);
  return new Response(JSON.stringify({ keys: [jwk] }), { status: 200 });
};
const verify = (t, e = env) => verifyAccessToken(t, e, { fetcher, now: NOW });

beforeEach(() => {
  fetched = [];
  resetKeyCache();
});

test('a valid token passes and its claims come back', async () => {
  const claims = await verify(await token());
  assert.equal(claims?.email, 'me@example.org');
  assert.deepEqual(fetched, [`${TEAM}/cdn-cgi/access/certs`]);
});

test('signing keys are fetched once and reused', async () => {
  await verify(await token());
  await verify(await token());
  assert.equal(fetched.length, 1);
});

test('a token for another application, issuer, or past its expiry is refused', async () => {
  assert.equal(await verify(await token({ aud: ['other'] })), null);
  assert.equal(await verify(await token({ iss: 'https://evil.cloudflareaccess.com' })), null);
  assert.equal(await verify(await token({ exp: nowSeconds - 1 })), null);
  assert.equal(await verify(await token({ nbf: nowSeconds + 3600 })), null);
});

test('a forged, tampered, unsigned or unknown-key token is refused', async () => {
  assert.equal(await verify(await token({}, { key: otherPair.privateKey })), null, 'wrong key');
  const good = await token();
  const [h, , s] = good.split('.');
  assert.equal(
    await verify(
      `${h}.${json({ iss: TEAM, aud: [AUD], exp: nowSeconds + 600, email: 'x@y.z' })}.${s}`,
    ),
    null,
  );
  assert.equal(await verify(await token({}, { alg: 'none' })), null);
  assert.equal(await verify(await token({}, { kid: 'k2' })), null);
  assert.equal(await verify('not.a.jwt.at.all'), null);
  assert.equal(await verify('###.###.###'), null);
});

test('no token, or missing settings, fails closed', async () => {
  assert.equal(await verify(null), null);
  const t = await token();
  assert.equal(await verify(t, { ACCESS_AUD: AUD }), null);
  assert.equal(await verify(t, { ACCESS_TEAM_DOMAIN: TEAM }), null);
  assert.equal(
    await verify(t, { ACCESS_TEAM_DOMAIN: 'http://ledger.cloudflareaccess.com', ACCESS_AUD: AUD }),
    null,
  );
  assert.deepEqual(fetched, [], 'nothing fetched without settings');
});

test('the team domain may be given with or without https://', () => {
  assert.equal(teamOrigin('ledger.cloudflareaccess.com'), TEAM);
  assert.equal(teamOrigin(`${TEAM}/`), TEAM);
  assert.equal(teamOrigin('http://x.example'), null);
  assert.equal(teamOrigin(''), null);
});

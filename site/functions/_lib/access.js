// Checks the Cloudflare Access token on requests to /inbox/. Cloudflare Access already blocks anyone it hasn't let in;
// this second check means a misconfigured or removed Access policy fails closed instead of exposing reports.
// Settings (Pages project): ACCESS_TEAM_DOMAIN, e.g. https://example.cloudflareaccess.com, and ACCESS_AUD, the
// application's Audience (AUD) tag.

const KEY_CACHE_MS = 60 * 60 * 1000;
/** @type {{ url: string, at: number, keys: any[] } | null} */
let cachedKeys = null;

/** Clears the signing-key cache (tests). */
export function resetKeyCache() {
  cachedKeys = null;
}

function base64UrlBytes(part) {
  const base64 = part.replaceAll('-', '+').replaceAll('_', '/');
  const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

function base64UrlJson(part) {
  return JSON.parse(new TextDecoder().decode(base64UrlBytes(part)));
}

/** The team domain as an origin with no trailing slash, or null when it isn't an https address. */
export function teamOrigin(domain) {
  if (typeof domain !== 'string' || domain === '') {
    return null;
  }
  const withScheme = domain.includes('://') ? domain : `https://${domain}`;
  try {
    const url = new URL(withScheme);
    return url.protocol === 'https:' ? url.origin : null;
  } catch {
    return null;
  }
}

async function signingKeys(origin, fetcher, nowMs) {
  const url = `${origin}/cdn-cgi/access/certs`;
  if (cachedKeys && cachedKeys.url === url && nowMs - cachedKeys.at < KEY_CACHE_MS) {
    return cachedKeys.keys;
  }
  const response = await fetcher(url);
  if (!response.ok) {
    throw new Error(`Access certs: HTTP ${response.status}`);
  }
  const body = await response.json();
  const keys = Array.isArray(body?.keys) ? body.keys : [];
  cachedKeys = { url, at: nowMs, keys };
  return keys;
}

/**
 * Verifies an Access JWT: RS256 signature by one of the team's keys, issuer, audience, and expiry. Returns the token's
 * claims, or null for anything that doesn't check out.
 * @param {string | null} token the Cf-Access-Jwt-Assertion header
 * @param {{ ACCESS_TEAM_DOMAIN?: string, ACCESS_AUD?: string }} env
 * @param {{ fetcher?: typeof fetch, now?: Date }} [options]
 */
export async function verifyAccessToken(token, env, options = {}) {
  const fetcher = options.fetcher ?? fetch;
  const nowMs = (options.now ?? new Date()).getTime();
  const origin = teamOrigin(env.ACCESS_TEAM_DOMAIN);
  const audience = env.ACCESS_AUD;
  if (!token || !origin || !audience) {
    return null;
  }
  const parts = token.split('.');
  if (parts.length !== 3) {
    return null;
  }
  let header;
  let claims;
  try {
    header = base64UrlJson(parts[0]);
    claims = base64UrlJson(parts[1]);
  } catch {
    return null;
  }
  if (header?.alg !== 'RS256' || typeof header.kid !== 'string') {
    return null;
  }
  const keys = await signingKeys(origin, fetcher, nowMs);
  const jwk = keys.find((k) => k.kid === header.kid);
  if (!jwk) {
    return null;
  }
  const key = await crypto.subtle.importKey(
    'jwk',
    { kty: jwk.kty, n: jwk.n, e: jwk.e, alg: 'RS256', ext: true },
    { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' },
    false,
    ['verify'],
  );
  const signed = new TextEncoder().encode(`${parts[0]}.${parts[1]}`);
  let valid;
  try {
    valid = await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, base64UrlBytes(parts[2]), signed);
  } catch {
    return null;
  }
  if (!valid) {
    return null;
  }
  const nowSeconds = Math.floor(nowMs / 1000);
  const audiences = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
  if (
    claims.iss !== origin ||
    !audiences.includes(audience) ||
    typeof claims.exp !== 'number' ||
    claims.exp <= nowSeconds ||
    (typeof claims.nbf === 'number' && claims.nbf > nowSeconds + 60)
  ) {
    return null;
  }
  return claims;
}

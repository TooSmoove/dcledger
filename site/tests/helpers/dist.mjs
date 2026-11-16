// The built site, read once for assertions (run `npm run fixture && npm run build` first).
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const dist = fileURLToPath(new URL('../../dist/', import.meta.url));

if (!existsSync(dist)) {
  throw new Error('site/dist not found: run `npm run fixture && npm run build` before `npm test`.');
}

/** Absolute paths of files under root whose names end in extension. */
export async function filesUnder(root, extension) {
  const entries = await readdir(root, { recursive: true, withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.endsWith(extension))
    .map((entry) => join(entry.parentPath, entry.name));
}

/** Every built page as { name, html }, name relative to dist/ with forward slashes. */
export const pages = await Promise.all(
  (await filesUnder(dist, '.html')).map(async (path) => ({
    name: path.slice(dist.length).replaceAll('\\', '/'),
    html: await readFile(path, 'utf8'),
  })),
);

/** The HTML of one built page; fails the test when it wasn't built. */
export function page(name) {
  const found = pages.find((p) => p.name === name);
  assert.ok(found, `${name} (built: ${pages.map((p) => p.name).join(', ')})`);
  return found.html;
}

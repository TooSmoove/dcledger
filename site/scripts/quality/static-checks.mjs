// Static quality gates (SPEC AC12, issue #33), shared by tests/dist.test.mjs and tests/quality.test.mjs so the check
// that guards the build is the same one its canary fixtures prove can fail.
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';

/**
 * Ways to put raw HTML on a page. Ledger text (titles, bodies, case numbers) comes from reviewers and, before review,
 * from source platforms; Astro escapes it everywhere except through these. None is allowed in src/.
 */
export const HTML_SINKS = [
  { name: 'set:html', pattern: /\bset:html\b/ },
  { name: 'innerHTML', pattern: /\.innerHTML\s*\+?=/ },
  { name: 'outerHTML', pattern: /\.outerHTML\s*\+?=/ },
  { name: 'insertAdjacentHTML', pattern: /\binsertAdjacentHTML\s*\(/ },
  { name: 'document.write', pattern: /\bdocument\.write(ln)?\s*\(/ },
];

/** Every raw-HTML sink in text, as { line, sink }, 1-based lines. */
export function htmlSinks(text) {
  const found = [];
  text.split('\n').forEach((content, index) => {
    for (const { name, pattern } of HTML_SINKS) {
      if (pattern.test(content)) {
        found.push({ line: index + 1, sink: name });
      }
    }
  });
  return found;
}

/** True when an HTML page contains a script element of any kind (AC12: only /map may). */
export function hasScriptTag(html) {
  return /<script\b/i.test(html);
}

/** Files under dir whose names end in one of extensions, relative with forward slashes, sorted. */
export async function filesUnder(dir, extensions) {
  const found = [];
  for (const entry of await readdir(dir, { recursive: true, withFileTypes: true })) {
    if (entry.isFile() && extensions.some((extension) => entry.name.endsWith(extension))) {
      found.push(relative(dir, join(entry.parentPath, entry.name)).split(sep).join('/'));
    }
  }
  return found.sort();
}

/** Every raw-HTML sink in the source files under dir, as { file, line, sink }. */
export async function sinksUnder(dir) {
  const found = [];
  for (const file of await filesUnder(dir, ['.astro', '.js', '.mjs', '.ts'])) {
    for (const sink of htmlSinks(await readFile(join(dir, file), 'utf8'))) {
      found.push({ file, ...sink });
    }
  }
  return found;
}

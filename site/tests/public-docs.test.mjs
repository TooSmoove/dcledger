// Repository documents published on the site (/data/, /changelog/) must never send a reader to the repository
// (ADR-0009): links are rewritten to site addresses, issue references are left out, and anything else stops the build.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  PUBLIC_DOCS,
  assertPublicText,
  publicDocsPlugin,
  repoPath,
  siteHref,
  withoutIssueReferences,
} from '../src/lib/public-docs.js';

const repo = new URL('../../', import.meta.url);

test('links between published documents become site addresses', () => {
  assert.equal(siteHref('docs/stability.md', 'CHANGELOG.md'), '/data/#stability');
  assert.equal(siteHref('docs/data-dictionary.md', 'CHANGELOG.md'), '/data/#data-dictionary');
  assert.equal(siteHref('../CHANGELOG.md', 'docs/stability.md'), '/changelog/');
  assert.equal(
    siteHref('data-dictionary.md#vocabularies', 'docs/stability.md'),
    '/data/#vocabularies',
  );
  assert.equal(siteHref('stability.md', 'docs/data-dictionary.md'), '/data/#stability');
  assert.equal(siteHref('#columns', 'docs/data-dictionary.md'), '#columns');
  assert.equal(siteHref('https://semver.org/', 'CHANGELOG.md'), 'https://semver.org/');
});

test('a link to GitHub or to an unpublished repository file stops the build', () => {
  assert.throws(
    () => siteHref('https://github.com/TooSmoove/dcledger/issues/1', 'CHANGELOG.md'),
    /GitHub/,
  );
  assert.throws(() => siteHref('http://www.github.com/x', 'CHANGELOG.md'), /GitHub/);
  assert.throws(() => siteHref('docs/SPEC.md', 'CHANGELOG.md'), /isn't published/);
  assert.throws(() => siteHref('../data/README.md', 'docs/stability.md'), /isn't published/);
});

test('issue references are left out; other internals stop the build', () => {
  assert.equal(
    withoutIssueReferences('Stability promise, held by tests (#116).'),
    'Stability promise, held by tests.',
  );
  assert.equal(withoutIssueReferences('**Nov 15, 2026 (issue #113):**'), '**Nov 15, 2026:**');
  assert.equal(withoutIssueReferences('the first release (PR #35)'), 'the first release');
  for (const text of [
    'see SPEC §E',
    'per ADR-0004',
    'on github.com',
    'fixed in issue #12',
    'see #12',
  ]) {
    assert.throws(() => assertPublicText(text, 'x.md'), /refers to the code repository/, text);
  }
  assert.doesNotThrow(() =>
    assertPublicText('the #decision-{id} anchor; Ampersand &#39; entity', 'x.md'),
  );
});

test('the plugin runs only for published documents', () => {
  const other = pathToFileURL(fileURLToPath(new URL('site/src/owner/disclosure.md', repo)));
  assert.equal(publicDocsPlugin({ fileURL: other }), null);
  assert.equal(publicDocsPlugin({ fileURL: undefined }), null);
  for (const doc of Object.keys(PUBLIC_DOCS)) {
    const plugin = publicDocsPlugin({ fileURL: new URL(doc, repo) });
    assert.equal(plugin?.name, 'public-docs', doc);
  }
  assert.equal(repoPath('/elsewhere/file.md'), null);
});

test('every published document exists and passes its own checks as written today', async () => {
  for (const doc of Object.keys(PUBLIC_DOCS)) {
    const text = await readFile(new URL(doc, repo), 'utf8');
    for (const [, url] of text.matchAll(/\]\(([^)\s]+)\)/g)) {
      assert.doesNotThrow(() => siteHref(url, doc), `${doc}: ${url}`);
    }
  }
});

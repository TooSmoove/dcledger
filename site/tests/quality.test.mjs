// The static quality gates and their canaries (SPEC AC12, issue #33). Each gate is run on a fixture that breaks it
// on purpose, so a gate that stops detecting anything fails here instead of passing silently.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { hasScriptTag, htmlSinks, sinksUnder } from '../scripts/quality/static-checks.mjs';

const src = fileURLToPath(new URL('../src/', import.meta.url));
const canary = (name) =>
  readFile(new URL(`fixtures/quality/violations/${name}`, import.meta.url), 'utf8');

test('no component or client script writes raw HTML (set:html and friends are banned, AC12)', async () => {
  const found = await sinksUnder(src);
  assert.deepEqual(
    found,
    [],
    found.map(({ file, line, sink }) => `src/${file}:${line} uses ${sink}`).join('\n'),
  );
});

test('the raw-HTML ban catches every planted sink', async () => {
  const found = htmlSinks(await canary('unsafe-component.astro.txt')).map(({ sink }) => sink);
  assert.deepEqual(found, ['set:html', 'innerHTML', 'insertAdjacentHTML', 'document.write']);
});

test('the raw-HTML ban leaves escaped output and text assignment alone', () => {
  assert.deepEqual(
    htmlSinks('<p>{item.title}</p>\nnode.textContent = value;\nconst html = "set:htmlish";'),
    [],
  );
});

test('the no-script check catches a planted script tag', async () => {
  assert.equal(hasScriptTag(await canary('script-page.html.txt')), true);
  assert.equal(hasScriptTag('<p>No scripts, just a <code>script</code> word.</p>'), false);
});

test('Lighthouse still asserts mobile performance and accessibility of at least 0.9 on /radar and /map (AC12)', async () => {
  const config = JSON.parse(
    await readFile(new URL('../lighthouserc.json', import.meta.url), 'utf8'),
  ).ci;
  assert.deepEqual(config.collect.url.toSorted(), [
    'http://localhost/map/index.html',
    'http://localhost/radar/index.html',
  ]);
  assert.equal(
    config.collect.settings.formFactor ?? 'mobile',
    'mobile',
    'Lighthouse defaults to mobile',
  );
  for (const category of ['performance', 'accessibility']) {
    const [level, options] = config.assert.assertions[`categories:${category}`];
    assert.equal(level, 'error', category);
    assert.ok(options.minScore >= 0.9, `${category} minScore ${options.minScore}`);
  }
});

// Copies the test fixtures to where `dcl export` would write them (../out/site-data/events.json and ../out/public/),
// so the site builds without the .NET toolchain. Never used by the publish workflow.
import { cp, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const out = new URL('../../out/', import.meta.url);
await mkdir(new URL('site-data/', out), { recursive: true });
await cp(
  new URL('../tests/fixtures/events.json', import.meta.url),
  new URL('site-data/events.json', out),
);
await cp(new URL('../tests/fixtures/public/', import.meta.url), new URL('public/', out), {
  recursive: true,
  force: true,
});
console.log(`Copied fixtures to ${fileURLToPath(out)}`);

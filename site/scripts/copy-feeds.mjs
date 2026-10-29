// Copies the files `dcl export` wrote under ../out/public/ (radar.ics, radar/*.ics, events.csv) into dist/, so dist
// is the complete deployable and /radar can link to them. Runs after `astro build`. Fails when the export is missing:
// a site without its feeds must never deploy.
import { cp, stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const source = fileURLToPath(new URL('../../out/public/', import.meta.url));
const target = fileURLToPath(new URL('../dist/', import.meta.url));

for (const required of ['radar.ics', 'events.csv']) {
  await stat(new URL(required, `file://${source}`)).catch(() => {
    throw new Error(`${source}${required} is missing: run dcl export (or npm run fixture) first.`);
  });
}

await cp(source, target, { recursive: true, force: true });
console.log(`Copied feeds from ${source} to ${target}`);

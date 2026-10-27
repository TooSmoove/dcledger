// Copies the test fixture to where `dcl export` would write site data, so the site builds
// without the .NET toolchain. Never used by the publish workflow.
import { copyFile, mkdir } from 'node:fs/promises';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const source = fileURLToPath(new URL('../tests/fixtures/events.json', import.meta.url));
const target = fileURLToPath(new URL('../../out/site-data/events.json', import.meta.url));

await mkdir(dirname(target), { recursive: true });
await copyFile(source, target);
console.log(`Copied fixture to ${target}`);

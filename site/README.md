# site

Static Astro site for the Hearing Radar. It reads only `../out/site-data/events.json`, which `dcl export` writes (ADR-0001, ADR-0004). It never reads `data/` directly.

Requires Node ≥ 22.23.4 (see `/.nvmrc`).

```sh
npm ci
npm run fixture       # copy tests/fixtures/events.json to ../out/site-data/ (no .NET needed)
npm run build         # static output in dist/
npm test              # node:test over the built site and the data contract
npm run check         # astro check (types)
npm run format:check  # prettier
npm run dev           # local preview at http://localhost:4321
```

Astro collects anonymous CLI usage telemetry by default. To opt out on your machine: `npx astro telemetry disable` (CI sets `ASTRO_TELEMETRY_DISABLED=1`).

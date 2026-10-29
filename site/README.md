# site

Static Astro site for the Hearing Radar. It reads only what `dcl export` writes (ADR-0001, ADR-0004): `../out/site-data/events.json` for its pages, and `../out/public/` (calendars and CSV), which the build copies into `dist/`. It never reads `data/` directly.

Pages: `/radar` (items in the next 30 days, as of the export), `/items/{id}` (full provenance), `/jurisdictions/{id}` (freshness and every item), `/about`, `404`. `public/_headers` adds `X-Robots-Tag: noindex` on Cloudflare Pages (AC11).

Requires Node ≥ 22.23.4 (see `/.nvmrc`).

```sh
npm ci
npm run fixture       # copy tests/fixtures/ to ../out/ (no .NET needed)
npm run build         # static output in dist/, feeds copied from ../out/public/
npm test              # node:test over the built site and the data contract
npm run check         # astro check (types)
npm run format:check  # prettier
npm run dev           # local preview at http://localhost:4321
```

Astro collects anonymous CLI usage telemetry by default. To opt out on your machine: `npx astro telemetry disable` (CI sets `ASTRO_TELEMETRY_DISABLED=1`).

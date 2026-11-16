# site

Static Astro site for the Hearing Radar. It reads only what `dcl export` writes (ADR-0001, ADR-0004): `../out/site-data/events.json` for its pages, and `../out/public/` (calendars and CSV), which the build copies into `dist/`. It never reads `data/` directly.

Pages: `/radar` (items in the next 30 days, as of the export), `/map` (the same items, a table first and a map on demand), `/items/{id}` (full provenance), `/jurisdictions/{id}` (freshness, a locator map and every item), `/about`, `404`. `public/_headers` adds `X-Robots-Tag: noindex` on Cloudflare Pages (AC11), and long-lived caching for the hashed files in `/_astro/`.

Requires Node ≥ 22.23.4 (see `/.nvmrc`).

```sh
npm ci
npm run fixture       # copy tests/fixtures/ to ../out/ (no .NET needed)
npm run build         # static output in dist/, feeds copied from ../out/public/
npm test              # node:test over the built site and the data contract
npm run test:e2e      # the end-to-end smoke test's site half (after the .NET half wrote ../out/, see below)
npm run check         # astro check (types)
npm run lighthouse    # Lighthouse mobile on /map and /radar; fails below 90 (run after build)
npm run a11y          # axe on every page; fails on any critical or serious issue (run after build; needs Chrome)
npm run format:check  # prettier
npm run dev           # local preview at http://localhost:4321
```

## Quality gates (AC12)

ci fails the build when any of these breaks. Each one is checked against a fixture that breaks it on purpose
(`tests/fixtures/quality/violations/`), so a gate that silently stops detecting fails too.

| Gate                                                                                                                                             | Where                                      | Canary                                                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| axe: zero critical or serious issues on every page of `dist/`, in light and dark, at 390 px and 1280 px (WCAG 2.2 A/AA plus best practices)      | `npm run a11y` (`scripts/quality/axe.mjs`) | `violations/index.html`: axe must report all four planted rules before `dist/` is audited      |
| No page but `/map` ships a `<script>`                                                                                                            | `tests/dist.test.mjs`                      | `violations/script-page.html.txt` (`tests/quality.test.mjs`)                                   |
| No raw HTML from components or client scripts: `set:html`, `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write` are banned in `src/` | `tests/quality.test.mjs`                   | `violations/unsafe-component.astro.txt`                                                        |
| Lighthouse mobile performance and accessibility ≥ 0.9 on `/radar` and `/map`                                                                     | `npm run lighthouse`                       | `tests/quality.test.mjs` fails if `lighthouserc.json` loosens either threshold or drops a page |

`npm run a11y` uses `CHROME_PATH` when set, otherwise Playwright's `chrome` channel (Google Chrome, as on GitHub's
runners). Ledger text reaches pages only through Astro's escaping; if a page ever needs real markup from data, render
it from structured fields instead of lifting the ban.

## The end-to-end smoke test

`tests/DcLedger.Cli.Tests/EndToEndSmokeTests.cs` runs recorded Legistar responses through `dcl ingest` (in-memory
bucket and review queue), pastes the review issue's YAML with a neutral title, then runs `dcl validate` and
`dcl export`. `e2e/smoke.mjs` then checks the built site: the event is on `/radar` and the feeds in `dist/` are the
exported ones. ci's e2e job runs both halves; to run them locally from the repository root:

```sh
DCL_E2E_OUT=out dotnet run --project tests/DcLedger.Cli.Tests -- -class DcLedger.Cli.Tests.EndToEndSmokeTests
cd site && npm run build && npm run test:e2e
```

Astro collects anonymous CLI usage telemetry by default. To opt out on your machine: `npx astro telemetry disable` (CI sets `ASTRO_TELEMETRY_DISABLED=1`).

## Imagery and the font (issue #132)

- **Locator maps** (`src/components/Locator.astro`, `src/lib/locator.js`): a static SVG per jurisdiction from
  `src/geo/nc-boundaries.json`, which `tools/build_locators.py` builds from Census boundaries and which is committed.
  A jurisdiction it lacks gets no locator; `tools/test_build_locators.py` fails until the file covers every
  jurisdiction in `data/`. After rebuilding it, update its hash in `tools/license-policy.json`.
- **First-page pictures** (`src/lib/thumbnails.js`, in `Sources.astro`): shown for a document whose `kind` is
  `minutes` or `legislation` and whose `thumbnail` is set; the files come from the export (`out/public/thumbnails/`).
  How a picture is made and reviewed is in `data/README.md`.
- **The font** (`src/fonts/`): EB Garamond, one subset WOFF2 built by `tools/subset_font.py`, declared in
  `claret.css` and preloaded by `Base.astro`. `tests/font.test.mjs` holds it to one file, 60 KB, this origin only.

## The map

`/map` is the only page with a script: a small loader that, when the user clicks "Show map", imports MapLibre GL JS and
pmtiles (about 456 KB gzip) and draws one marker per item that has a `location`. The basemap comes from
`PUBLIC_BASEMAP_URL`, the `nc.pmtiles` file in the public bucket; its fonts and sprite sit beside it (`basemap.yml`).
Without that variable the page shows the table and says the map isn't available, and `npm test` checks that mode.
To build and test the map's code path, set it to any https `.pmtiles` URL, for example
`PUBLIC_BASEMAP_URL=https://basemap.example.org/basemap/nc.pmtiles` (ci does). Plain `http://` works only for
`localhost`, for local previews.

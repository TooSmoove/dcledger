# Spike S18: map page Lighthouse with on-demand MapLibre (#3)

**Question (#3):** with the table rendered first and MapLibre + pmtiles loaded through dynamic `import()` only when the
user opens the map, does the map page score ≥ 90 on Lighthouse mobile (performance and accessibility)?

**Answer: yes, 100 and 100.** Measured on the real map page from #22 rather than a throwaway in `spikes/astro-map/`, so
the check stays in ci (`npm run lighthouse`, failing below 0.9) instead of being deleted.

## Method

- Site built from the test fixture with a stand-in basemap URL (the map's code path is built; nothing is fetched until
  the button is clicked).
- `@lhci/cli` 0.15.1 (Lighthouse 12.6.1), mobile preset (simulated throttling, Moto G Power-class device),
  `staticDistDir`, 3 runs per page, median run asserted: `site/lighthouserc.json`.
- Local run: Chromium from Playwright, Oct 29, 2026. ci runs the same on `ubuntu-24.04` with its preinstalled Chrome
  and keeps the reports as the `lighthouse` artifact.

## Results (local, 3 runs each)

| Page | Performance | Accessibility | FCP | LCP | TBT | CLS | Bytes on first load |
|---|---|---|---|---|---|---|---|
| `/map/` | 100, 100, 100 | 100, 100, 100 | 0.72–0.87 s | 0.72–0.99 s | 0–16 ms | 0 | 4,518 |
| `/radar/` | 100, 100, 100 | 100, 100, 100 | 0.65–0.66 s | 0.76 s | 0–5 ms | 0 | 2,612 |

The map page's first load is the HTML and a 2.7 KB module script (1.4 KB gzip) that wires the button. MapLibre's CSS
is not in the first load: Astro hoists CSS imported by a page script into a render-blocking `<link>`, so the map module
imports it as a URL and adds it when the map opens.

## What opening the map costs

Fetched only after "Show map" is clicked:

| File | Raw | gzip |
|---|---|---|
| Map module (MapLibre GL JS 6.16.0 + pmtiles 4.5.0) | 1,096 KB | 295 KB |
| MapLibre web worker | 519 KB | 147 KB |
| MapLibre CSS | 71 KB | 10 KB |
| `map-style.json` (Protomaps light, built at build time) | 66 KB | 4 KB |

About 456 KB gzip in total, then tiles, glyphs and the sprite from the basemap bucket. Spike S14's 289 KB gzip covers
the module alone; MapLibre 6 ships its worker as a separate file, which the bundler can't find by itself, so
`map-view.ts` imports it as a URL and calls `setWorkerUrl`.

The opened state was checked in headless Chromium against a small synthetic PMTiles archive served locally (the
Protomaps build host isn't reachable from the authoring sandbox): the map loads, makes no third-party request, shows
"© OpenStreetMap contributors · Protomaps", and its marker is a link reachable with Tab. Lighthouse wasn't run on the
opened state; axe on both states belongs to #33.

## Decisions

- Keep the table-first page with MapLibre on demand; no static image fallback is needed.
- ci asserts performance and accessibility ≥ 0.9 on `/map/` and `/radar/` on every pull request (AC12). #33 extends
  it with axe and the other quality gates.

// The basemap's MapLibre style, built once at build time so the browser downloads a static JSON file (only after the
// user opens the map) instead of the style library. Fonts, sprite and tiles all come from the basemap bucket.
import { layers, namedFlavor } from '@protomaps/basemaps';
import { FLAVOR, LANGUAGE, basemapAssets, mapStyle } from '../lib/basemap.js';

export function GET(): Response {
  const assets = basemapAssets(import.meta.env.PUBLIC_BASEMAP_URL);
  // Without a configured basemap the map page offers no map, so this valid but empty style is never requested.
  const style = assets
    ? mapStyle(assets, layers('protomaps', namedFlavor(FLAVOR), { lang: LANGUAGE }))
    : { version: 8, sources: {}, layers: [] };
  return new Response(JSON.stringify(style), {
    headers: { 'Content-Type': 'application/json' },
  });
}

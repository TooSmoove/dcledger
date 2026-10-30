// Basemap configuration and the map style (issue #22). Plain JS so node:test can import it directly. Used at build time
// (the style JSON endpoint and the map page); nothing here runs in the browser.

/** North Carolina's bounding box, the one the basemap is cut to (basemap.yml NC_BBOX): [[west, south], [east, north]]. */
export const NC_BOUNDS = /** @type {const} */ ([
  [-84.33, 33.84],
  [-75.46, 36.59],
]);

/** Required by the basemap's ODbL licence (SPEC WS-AC7), plus the tile schema's credit. */
export const ATTRIBUTION =
  '<a href="https://www.openstreetmap.org/copyright">© OpenStreetMap contributors</a> · ' +
  '<a href="https://protomaps.com">Protomaps</a>';

/** The Protomaps flavor and label language; the bucket hosts this flavor's sprite (basemap.yml). */
export const FLAVOR = 'light';
export const LANGUAGE = 'en';

/**
 * Where the basemap's files are, from the configured PMTiles URL (repository variable BASEMAP_URL, given to the build as
 * PUBLIC_BASEMAP_URL). Fonts and sprites sit beside nc.pmtiles in the same public bucket, so the map never calls a
 * third party. Returns null when no basemap is configured; throws when the URL isn't an https .pmtiles file (plain
 * http is accepted only for localhost, for local previews).
 * @param {string | undefined} url
 * @returns {{ tiles: string, glyphs: string, sprite: string } | null}
 */
export function basemapAssets(url) {
  if (url === undefined || url.trim() === '') {
    return null;
  }

  let parsed;
  try {
    parsed = new URL(url);
  } catch {
    throw new Error(`PUBLIC_BASEMAP_URL isn't a URL: ${JSON.stringify(url)}.`);
  }

  const local = parsed.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(parsed.hostname);
  if (
    (parsed.protocol !== 'https:' && !local) ||
    !parsed.pathname.endsWith('.pmtiles') ||
    parsed.search !== '' ||
    parsed.hash !== '' ||
    parsed.username !== '' ||
    parsed.password !== ''
  ) {
    throw new Error(
      `PUBLIC_BASEMAP_URL must be a plain https URL of a .pmtiles file (http only on localhost), got ${JSON.stringify(url)}.`,
    );
  }

  const base = new URL('.', parsed).href.replace(/\/$/, '');
  return {
    tiles: `pmtiles://${parsed.href}`,
    glyphs: `${base}/fonts/{fontstack}/{range}.pbf`,
    sprite: `${base}/sprites/v4/${FLAVOR}`,
  };
}

/**
 * A MapLibre style (version 8) for the basemap, with the given Protomaps layers.
 * @param {{ tiles: string, glyphs: string, sprite: string }} assets
 * @param {unknown[]} layers from @protomaps/basemaps, for the source named "protomaps"
 */
export function mapStyle(assets, layers) {
  return {
    version: 8,
    glyphs: assets.glyphs,
    sprite: assets.sprite,
    sources: {
      protomaps: { type: 'vector', url: assets.tiles, attribution: ATTRIBUTION },
    },
    layers,
  };
}

/**
 * The view to open on: the markers' bounding box, or all of North Carolina when there are none.
 * @param {readonly { lat: number, lon: number }[]} points
 * @returns {[[number, number], [number, number]]}
 */
export function initialBounds(points) {
  if (points.length === 0) {
    return [
      [NC_BOUNDS[0][0], NC_BOUNDS[0][1]],
      [NC_BOUNDS[1][0], NC_BOUNDS[1][1]],
    ];
  }

  const lons = points.map((p) => p.lon);
  const lats = points.map((p) => p.lat);
  return [
    [Math.min(...lons), Math.min(...lats)],
    [Math.max(...lons), Math.max(...lats)],
  ];
}

/**
 * Coordinates as readers see them in the table, for example "36.0726° N, 79.7920° W".
 * @param {{ lat: number, lon: number }} location
 */
export function coordinatesText(location) {
  const lat = `${Math.abs(location.lat).toFixed(4)}° ${location.lat >= 0 ? 'N' : 'S'}`;
  const lon = `${Math.abs(location.lon).toFixed(4)}° ${location.lon >= 0 ? 'E' : 'W'}`;
  return `${lat}, ${lon}`;
}

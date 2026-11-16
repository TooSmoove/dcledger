// Static locator maps (issue #132, item 3): where a jurisdiction sits in North Carolina, drawn at build time from the
// Census boundaries in src/geo/nc-boundaries.json (tools/build_locators.py). Plain JS so node:test can import it.
// Nothing here runs in the browser: the page gets a plain SVG and no script (AC12).

/** Areas narrower or shorter than this, in frame units (about 0.8 km each), also get a ring so they can be found. */
export const RING_BELOW = 30;
const RING_RADIUS = 16;
const PADDING = 6;

/**
 * Everything the locator component draws for one jurisdiction, or null when the boundaries file has no area for it
 * (a jurisdiction added since the file was built; tools/test_build_locators.py flags that in ci).
 * @param {{ frame: { width: number, height: number }, state: string, counties: string, source: { credit: string },
 *   areas: Record<string, { kind: string, d: string, bounds: number[] }> }} boundaries
 * @param {string} id the jurisdiction's ID
 * @param {string} name the jurisdiction's name, as the ledger writes it
 */
export function locatorFor(boundaries, id, name) {
  const area = Object.hasOwn(boundaries.areas, id) ? boundaries.areas[id] : undefined;
  if (area === undefined || area.d === '') {
    return null;
  }
  const { width, height } = boundaries.frame;
  const [x0, y0, x1, y1] = area.bounds;
  const small = x1 - x0 < RING_BELOW || y1 - y0 < RING_BELOW;
  return {
    viewBox: `${-PADDING} ${-PADDING} ${width + 2 * PADDING} ${height + 2 * PADDING}`,
    state: boundaries.state,
    counties: boundaries.counties,
    area: area.d,
    ring: small
      ? {
          cx: round((x0 + x1) / 2),
          cy: round((y0 + y1) / 2),
          r: RING_RADIUS + round(Math.max(x1 - x0, y1 - y0) / 2),
        }
      : null,
    label: `Locator map: ${area.kind === 'place' ? `${name} city limits` : name} highlighted within North Carolina, with county lines.`,
    credit: `Boundaries: ${boundaries.source.credit}.`,
  };
}

/** @param {number} value */
function round(value) {
  return Math.round(value * 10) / 10;
}

// Static locator maps (issue #132, item 3): where a jurisdiction sits in North Carolina, drawn at build time from the
// Census boundaries in src/geo/nc-boundaries.json (tools/build_locators.py). Plain JS so node:test can import it.
// Nothing here runs in the browser: the page gets a plain SVG and no script (AC12).

/**
 * Areas smaller than this in both directions, in frame units (about 0.8 km each), also get a ring so they can be found.
 * Both, not either: a long, narrow county such as Vance is plain to see at state scale and needs no ring.
 */
export const RING_BELOW = 30;
const RING_RADIUS = 16;
const PADDING = 6;

/**
 * Everything the locator component draws for one jurisdiction, or null when the boundaries file has no area for it
 * (a jurisdiction added since the file was built; tools/test_build_locators.py flags that in ci).
 * A ringed place that the boundaries file says lies within a ledger county (`within`, tools/build_locators.py) also
 * gets that county, shaded lightly behind it, so the reader sees where the speck is.
 * @param {{ frame: { width: number, height: number }, state: string, counties: string, source: { credit: string },
 *   areas: Record<string, { kind: string, d: string, bounds: number[], census_name?: string, within?: string }> }} boundaries
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
  const small = x1 - x0 < RING_BELOW && y1 - y0 < RING_BELOW;
  const county =
    small && area.within !== undefined && Object.hasOwn(boundaries.areas, area.within)
      ? boundaries.areas[area.within]
      : undefined;
  const context =
    county !== undefined && county.kind === 'county' && county.d !== ''
      ? { d: county.d, name: `${county.census_name ?? area.within} County` }
      : null;
  const what = area.kind === 'place' ? `${name} city limits` : name;
  return {
    viewBox: `${-PADDING} ${-PADDING} ${width + 2 * PADDING} ${height + 2 * PADDING}`,
    state: boundaries.state,
    counties: boundaries.counties,
    area: area.d,
    context: context === null ? null : context.d,
    ring: small
      ? {
          cx: round((x0 + x1) / 2),
          cy: round((y0 + y1) / 2),
          r: RING_RADIUS + round(Math.max(x1 - x0, y1 - y0) / 2),
        }
      : null,
    label:
      context === null
        ? `Locator map: ${what} highlighted within North Carolina, with county lines.`
        : `Locator map: ${what} highlighted within ${context.name}, North Carolina, with county lines.`,
    credit: `Boundaries: ${boundaries.source.credit}.`,
  };
}

/** @param {number} value */
function round(value) {
  return Math.round(value * 10) / 10;
}

/** The home page's legend groups, in order: the tier, its fill style, and what the legend calls it. */
const STATEWIDE_GROUPS = /** @type {const} */ ([
  { tier: 'watched', style: 'solid', text: 'Hearings and decisions' },
  { tier: 'decisions', style: 'hatched', text: 'Decisions only' },
  { tier: 'registered', style: 'hatched', text: 'Listed only' },
]);

/**
 * The home page's statewide map (issue #132, item 7): North Carolina with every ledger jurisdiction the boundaries
 * file has an area for, filled by how closely it is covered, so no one jurisdiction is singled out. Counties are drawn
 * before places, so a city reads on top of its county. An area too small to see at state scale gets a ring, as on the
 * jurisdiction pages. A jurisdiction with no area is left off the map and the legend, and returned in `missing`; the
 * home page's Coverage section still lists it.
 * @param {{ frame: { width: number, height: number }, state: string, counties: string, source: { credit: string },
 *   areas: Record<string, { kind: string, d: string, bounds: number[] }> }} boundaries
 * @param {readonly { id: string, name: string, tier: string }[]} jurisdictions in the order the legend lists them
 */
export function statewideFor(boundaries, jurisdictions) {
  const { width, height } = boundaries.frame;
  const drawn = [];
  const missing = [];
  for (const j of jurisdictions) {
    const group = STATEWIDE_GROUPS.find((g) => g.tier === j.tier);
    if (group === undefined) {
      throw new Error(`Unknown coverage tier ${JSON.stringify(j.tier)} for ${j.id}.`);
    }
    const area = Object.hasOwn(boundaries.areas, j.id) ? boundaries.areas[j.id] : undefined;
    if (area === undefined || area.d === '') {
      missing.push(j.id);
      continue;
    }
    drawn.push({ ...j, style: group.style, area });
  }
  const order = (/** @type {string} */ kind) => (kind === 'county' ? 0 : 1);
  const areas = [...drawn]
    .sort((a, b) => order(a.area.kind) - order(b.area.kind))
    .map((j) => ({ id: j.id, kind: j.area.kind, style: j.style, d: j.area.d }));
  const rings = drawn
    .filter((j) => {
      const [x0, y0, x1, y1] = j.area.bounds;
      return x1 - x0 < RING_BELOW && y1 - y0 < RING_BELOW;
    })
    .map((j) => {
      const [x0, y0, x1, y1] = j.area.bounds;
      return {
        id: j.id,
        cx: round((x0 + x1) / 2),
        cy: round((y0 + y1) / 2),
        r: RING_RADIUS + round(Math.max(x1 - x0, y1 - y0) / 2),
      };
    });
  const groups = STATEWIDE_GROUPS.map((g) => ({
    tier: g.tier,
    style: g.style,
    text: g.text,
    members: drawn.filter((j) => j.tier === g.tier).map((j) => ({ id: j.id, name: j.name })),
  })).filter((g) => g.members.length > 0);
  return {
    viewBox: `${-PADDING} ${-PADDING} ${width + 2 * PADDING} ${height + 2 * PADDING}`,
    state: boundaries.state,
    counties: boundaries.counties,
    areas,
    rings,
    groups,
    missing,
    label:
      drawn.length === 0
        ? 'Map of North Carolina with county lines.'
        : `Map of North Carolina with county lines, shading the ${drawn.length} ${drawn.length === 1 ? 'place' : 'places'} this ledger covers. The legend lists ${drawn.length === 1 ? 'it' : 'them'}.`,
    credit: `Boundaries: ${boundaries.source.credit}.`,
  };
}

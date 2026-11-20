// Static locator maps (issue #132, items 3 and 7): where a jurisdiction sits in North Carolina, drawn at build time from
// the Census boundaries in src/geo/nc-boundaries.json (tools/build_locators.py). Plain JS so node:test can import it.
// Nothing here runs in the browser: the page gets a plain SVG and no script (AC12).

/**
 * Areas smaller than this in both directions, in frame units (about 0.8 km each), are too small to read at state scale.
 * A small place drawn alone is shown with its county shaded lightly behind it, so the reader sees where it is. Both,
 * not either: a long, narrow county such as Vance is plain to see at state scale.
 */
export const SMALL_BELOW = 30;
const PADDING = 6;

/**
 * Everything the locator component draws for one jurisdiction, or null when the boundaries file has no area for it
 * (a jurisdiction added since the file was built; tools/test_build_locators.py flags that in ci).
 * A small place that the boundaries file says lies within a ledger county (`within`, tools/build_locators.py) also
 * gets that county, shaded lightly behind it.
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
  const county =
    isSmall(area.bounds) &&
    area.within !== undefined &&
    Object.hasOwn(boundaries.areas, area.within)
      ? boundaries.areas[area.within]
      : undefined;
  const context =
    county !== undefined && county.kind === 'county' && county.d !== ''
      ? { d: county.d, name: `${county.census_name ?? area.within} County` }
      : null;
  const what = area.kind === 'place' ? `${name} city limits` : name;
  return {
    viewBox: viewBox(boundaries),
    state: boundaries.state,
    counties: boundaries.counties,
    area: area.d,
    context: context === null ? null : context.d,
    label:
      context === null
        ? `Locator map: ${what} highlighted within North Carolina, with county lines.`
        : `Locator map: ${what} highlighted within ${context.name}, North Carolina, with county lines.`,
    credit: `Boundaries: ${boundaries.source.credit}.`,
  };
}

/** The home page's legend groups, in order: the tier, its fill, and what the legend calls it. */
const STATEWIDE_GROUPS = /** @type {const} */ ([
  { tier: 'watched', fill: 'full', text: 'Hearings and decisions' },
  { tier: 'decisions', fill: 'tint', text: 'Decisions only' },
  { tier: 'registered', fill: 'tint', text: 'Listed only' },
]);

/**
 * The home page's statewide map (issue #132, item 7): North Carolina with every ledger jurisdiction the boundaries
 * file has an area for, in one color: full where hearings are watched, a lighter tint of it where only decisions are
 * recorded. The legend names every jurisdiction by tier in text, so the map adds a picture, not information the reader
 * could only get from color. Counties draw first. A place whose tier matches a covered county containing it is left
 * to that county's fill (Greensboro in Guilford), so the map shows one shape rather than a shape inside a shape; a place
 * whose tier differs draws on top (Winston-Salem in Forsyth, Henderson in Vance). A jurisdiction with no area is left
 * off the map and the legend and returned in `missing`; the home page's Coverage section still lists it.
 * @param {{ frame: { width: number, height: number }, state: string, counties: string, source: { credit: string },
 *   areas: Record<string, { kind: string, d: string, bounds: number[] }> }} boundaries
 * @param {readonly { id: string, name: string, tier: string }[]} jurisdictions in the order the legend lists them
 */
export function statewideFor(boundaries, jurisdictions) {
  const located = [];
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
    located.push({ ...j, fill: group.fill, area });
  }
  const counties = located.filter((j) => j.area.kind === 'county');
  const places = located.filter(
    (j) =>
      j.area.kind !== 'county' &&
      !counties.some((c) => c.fill === j.fill && contains(c.area.bounds, j.area.bounds)),
  );
  const groups = STATEWIDE_GROUPS.map((g) => ({
    tier: g.tier,
    fill: g.fill,
    text: g.text,
    members: located.filter((j) => j.tier === g.tier).map((j) => ({ id: j.id, name: j.name })),
  })).filter((g) => g.members.length > 0);
  const n = located.length;
  return {
    viewBox: viewBox(boundaries),
    state: boundaries.state,
    counties: boundaries.counties,
    areas: [...counties, ...places].map((j) => ({
      id: j.id,
      kind: j.area.kind,
      fill: j.fill,
      d: j.area.d,
    })),
    groups,
    missing,
    label:
      n === 0
        ? 'Map of North Carolina with county lines.'
        : `Map of North Carolina with county lines, shading the ${n} ${n === 1 ? 'place' : 'places'} this ledger covers. The legend lists ${n === 1 ? 'it' : 'them'}.`,
    credit: `Boundaries: ${boundaries.source.credit}.`,
  };
}

/** @param {{ frame: { width: number, height: number } }} boundaries */
function viewBox(boundaries) {
  const { width, height } = boundaries.frame;
  return `${-PADDING} ${-PADDING} ${width + 2 * PADDING} ${height + 2 * PADDING}`;
}

/** @param {readonly number[]} bounds [x0, y0, x1, y1] */
function isSmall([x0, y0, x1, y1]) {
  return x1 - x0 < SMALL_BELOW && y1 - y0 < SMALL_BELOW;
}

/** Whether the outer box holds the inner one; boxes, so only for "is this place inside this county" checks. */
function contains(/** @type {readonly number[]} */ outer, /** @type {readonly number[]} */ inner) {
  return (
    outer[0] <= inner[0] && outer[1] <= inner[1] && outer[2] >= inner[2] && outer[3] >= inner[3]
  );
}

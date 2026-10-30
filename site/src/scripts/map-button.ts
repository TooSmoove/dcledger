// The only script on the map page, and small: it wires the "Show map" button and loads MapLibre and pmtiles with a
// dynamic import() only when the user asks (issue #22, SPEC AC12). Marker data comes from the table that's already on
// the page, so the table stays the source of truth and works without JavaScript.

/** One marker: an item with a location, read from its table row. */
export interface MarkerData {
  lat: number;
  lon: number;
  href: string;
  title: string;
  when: string;
}

/** Rows marked with data-lat and data-lon, in table order. Rows with unusable values are skipped. */
export function readMarkers(root: ParentNode): MarkerData[] {
  const markers: MarkerData[] = [];
  for (const row of root.querySelectorAll<HTMLTableRowElement>('tr[data-lat][data-lon]')) {
    const lat = coordinate(row.dataset.lat);
    const lon = coordinate(row.dataset.lon);
    const link = row.querySelector<HTMLAnchorElement>('a[href^="/items/"]');
    if (lat === null || lon === null || link === null) {
      continue;
    }

    markers.push({
      lat,
      lon,
      href: link.getAttribute('href') ?? '',
      title: link.textContent?.trim() ?? '',
      when: row.querySelector('time')?.textContent?.trim() ?? '',
    });
  }

  return markers;
}

/** A decimal-degree value from a data attribute, or null; an empty attribute is not 0. */
function coordinate(value: string | undefined): number | null {
  if (value === undefined || value.trim() === '') {
    return null;
  }

  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

/** Connects the button; does nothing on a page without it (a build with no basemap configured). */
export function wireMapButton(doc: Document = document): void {
  const button = doc.querySelector<HTMLButtonElement>('#show-map');
  const container = doc.querySelector<HTMLElement>('#map');
  const status = doc.querySelector<HTMLElement>('#map-status');
  const styleUrl = button?.dataset.style;
  if (!button || !container || !status || !styleUrl) {
    return;
  }

  button.addEventListener('click', async () => {
    button.disabled = true;
    status.textContent = 'Loading the map…';
    try {
      const { showMap } = await import('./map-view');
      container.hidden = false;
      const markers = readMarkers(doc);
      await showMap(container, styleUrl, markers);
      button.hidden = true;
      status.textContent =
        markers.length === 1
          ? 'Map loaded with 1 site.'
          : `Map loaded with ${markers.length} sites.`;
    } catch (error) {
      container.hidden = true;
      button.disabled = false;
      status.textContent = "The map couldn't load. The table lists every item.";
      console.error(error);
    }
  });
}

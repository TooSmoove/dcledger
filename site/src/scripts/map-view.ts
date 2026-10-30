// Loaded only after the user clicks "Show map" (dynamic import in map-button.ts), so MapLibre, pmtiles and their CSS
// stay out of the page's first load (SPEC Performance: ~289 KB gzip, spike S14).
import {
  AttributionControl,
  Map as MapLibreMap,
  type ErrorEvent,
  Marker,
  NavigationControl,
  addProtocol,
  setWorkerUrl,
} from 'maplibre-gl';
// MapLibre 6 finds its worker beside its own module at run time, which a bundler can't follow; point it at the copy
// Vite emits instead. The worker bundle has no imports of its own.
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url';
// Imported as a URL and added when the map opens; a plain CSS import would be hoisted into the page's first load.
import styleSheetUrl from 'maplibre-gl/dist/maplibre-gl.css?url';
import { Protocol } from 'pmtiles';
import { NC_BOUNDS, initialBounds } from '../lib/basemap.js';
import type { MarkerData } from './map-button';

let protocolAdded = false;

/** Adds MapLibre's stylesheet to the page once; resolves when it has loaded. */
function loadStyleSheet(): Promise<void> {
  if (document.querySelector(`link[href="${styleSheetUrl}"]`)) {
    return Promise.resolve();
  }

  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = styleSheetUrl;
  const loaded = new Promise<void>((resolve, reject) => {
    link.addEventListener('load', () => resolve(), { once: true });
    link.addEventListener('error', () => reject(new Error(`Couldn't load ${styleSheetUrl}`)), {
      once: true,
    });
  });
  document.head.append(link);
  return loaded;
}

/** Shows the basemap from styleUrl in container, with one link-marker per item; resolves once the map has loaded. */
export async function showMap(
  container: HTMLElement,
  styleUrl: string,
  markers: MarkerData[],
): Promise<void> {
  await loadStyleSheet();
  setWorkerUrl(workerUrl);
  if (!protocolAdded) {
    addProtocol('pmtiles', new Protocol().tile);
    protocolAdded = true;
  }

  const map = new MapLibreMap({
    container,
    style: styleUrl,
    bounds: initialBounds(markers),
    fitBoundsOptions: { padding: 48, maxZoom: 12 },
    // Room to pan a little past the state line, never off the basemap's extract.
    maxBounds: [
      [NC_BOUNDS[0][0] - 1, NC_BOUNDS[0][1] - 1],
      [NC_BOUNDS[1][0] + 1, NC_BOUNDS[1][1] + 1],
    ],
    maxZoom: 16,
    attributionControl: false,
    // Scrolling the page never gets trapped in the map; zoom with ctrl/cmd + scroll or two fingers.
    cooperativeGestures: true,
    dragRotate: false,
    pitchWithRotate: false,
    touchPitch: false,
  });
  // Always expanded, so "© OpenStreetMap contributors" is visible on small screens too (SPEC WS-AC7).
  map.addControl(new AttributionControl({ compact: false }), 'bottom-right');
  map.addControl(new NavigationControl({ showCompass: false }), 'top-right');

  for (const marker of markers) {
    // A real link: focusable, announced with its title and date, and it opens the item page.
    const link = document.createElement('a');
    link.className = 'map-marker';
    link.href = marker.href;
    link.setAttribute('aria-label', marker.when ? `${marker.title}, ${marker.when}` : marker.title);
    link.title = marker.title;
    new Marker({ element: link, anchor: 'bottom' }).setLngLat([marker.lon, marker.lat]).addTo(map);
  }

  return new Promise((resolve, reject) => {
    const failed = (event: ErrorEvent) =>
      reject(event.error ?? new Error('The map failed to load.'));
    map.once('error', failed);
    map.once('load', () => {
      map.off('error', failed);
      resolve();
    });
  });
}

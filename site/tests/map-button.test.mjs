// readMarkers runs in the browser; these fakes have just the DOM surface it uses (Node strips the TypeScript types).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readMarkers, wireMapButton } from '../src/scripts/map-button.ts';

function row({ lat, lon, href, title, when }) {
  return {
    dataset: { lat, lon },
    querySelector(selector) {
      if (selector.startsWith('a[')) {
        return href === undefined ? null : { getAttribute: () => href, textContent: ` ${title} ` };
      }
      return selector === 'time' ? { textContent: when } : null;
    },
  };
}

const page = (rows) => ({ querySelectorAll: () => rows });

test('every located row becomes a marker, in table order', () => {
  const markers = readMarkers(
    page([
      row({ lat: '36.0726', lon: '-79.792', href: '/items/a/', title: 'A', when: 'Thu, Nov 5' }),
      row({ lat: '35.9557', lon: '-80.0053', href: '/items/b/', title: 'B', when: 'Fri, Nov 6' }),
    ]),
  );

  assert.deepEqual(markers, [
    { lat: 36.0726, lon: -79.792, href: '/items/a/', title: 'A', when: 'Thu, Nov 5' },
    { lat: 35.9557, lon: -80.0053, href: '/items/b/', title: 'B', when: 'Fri, Nov 6' },
  ]);
});

test('rows with unusable coordinates or no item link are skipped', () => {
  const markers = readMarkers(
    page([
      row({ lat: 'north', lon: '-79.792', href: '/items/a/', title: 'A', when: '' }),
      row({ lat: '36.07', lon: '', href: '/items/b/', title: 'B', when: '' }),
      row({ lat: '36.07', lon: '-79.79', href: undefined, title: 'C', when: '' }),
    ]),
  );

  assert.deepEqual(markers, [], 'an empty attribute must not become 0');
});

test('a page without the button is left alone', () => {
  assert.doesNotThrow(() => wireMapButton({ querySelector: () => null }));
});

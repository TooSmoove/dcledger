// First-page pictures of archived documents (issue #132). Plain JS so node:test can import it.

/** Document kinds, mirroring DcLedger.Domain.EnumTokens (ToToken(DocumentKind)). */
export const DOCUMENT_KINDS = /** @type {const} */ ([
  'minutes',
  'legislation',
  'agenda',
  'agenda_packet',
  'notice',
  'news_release',
  'news_article',
  'other',
]);

/** The only kinds whose first page the site shows; agenda packets stay private (SPEC §E). Mirrors Thumbnail.MayShow. */
const SHOWN = new Set(['minutes', 'legislation']);

/** Pictures are rendered at twice the size they are shown, for sharp text on high-density screens. */
const DENSITY = 2;

/**
 * What a source entry shows for its document's first page, or null. The quality gate already refuses a picture of
 * any other kind; checking again here means a gate bug can't put an agenda packet on a page.
 * @param {{ sha256: string, kind: string | null, thumbnail: { path: string, width: number, height: number } | null }} document
 */
export function pageOne(document) {
  const { thumbnail, kind, sha256 } = document;
  if (thumbnail === null || kind === null || !SHOWN.has(kind)) {
    return null;
  }
  return {
    src: thumbnail.path,
    width: Math.round(thumbnail.width / DENSITY),
    height: Math.round(thumbnail.height / DENSITY),
    alt: `Page 1 of the archived copy with SHA-256 ${sha256.slice(0, 12)}…`,
    caption: 'Page 1',
  };
}

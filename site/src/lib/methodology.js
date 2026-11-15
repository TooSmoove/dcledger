// Wording for the methodology page (issue #117) that is computed from data, kept apart from the markup so node:test
// can check it. Plain JS; nothing here runs in the browser.
import { dateText } from './radar.js';

/** The site's public origin, as in `dcl export --site-url` (SPEC §H). */
export const SITE_URL = 'https://ncdcledger.org';

/** A date without its weekday, for example "Nov 22, 2026", as in "Log opened Nov 22, 2026." */
export function shortDateText(isoDate) {
  return dateText(isoDate).replace(/^[A-Z][a-z]{2}, /, '');
}

/** The log's first line (issue #117). */
export function logOpenedText(openedOn) {
  return `Log opened ${shortDateText(openedOn)}.`;
}

/**
 * A worked citation for one item: the page address, the source's URL, its retrieval time and its SHA-256, which
 * together let a reader find the same copy we kept.
 * @param {{ siteUrl: string, id: string, title: string, jurisdiction: string, startsText: string,
 *   document: { url: string, retrievedText: string, sha256: string } }} item
 */
export function citationText(item) {
  return (
    `NC Data Center Ledger, "${item.title}," ${item.jurisdiction}, ${item.startsText}. ` +
    `${item.siteUrl}/items/${item.id}/ (accessed [the date you viewed it]). ` +
    `Source: ${item.document.url}, retrieved ${item.document.retrievedText}, ` +
    `SHA-256 ${item.document.sha256}.`
  );
}

// A Sätteri mdast plugin (Astro's Markdown processor) for the repository documents the site publishes as pages:
// docs/stability.md and docs/data-dictionary.md on /data/, and CHANGELOG.md on /changelog/. They stay the single
// source; this makes them read as part of the site rather than the code repository:
//   - links between them become site addresses; any other link into the repository stops the build;
//   - issue references like "(#116)" and "(issue #115)" are left out;
//   - HTML comments (notes for developers) are dropped;
//   - in docs/, headings move down a level so each document sits under the page's own title.
// Text that still mentions the repository's internals (SPEC, ADRs, GitHub, issue numbers) stops the build.
// Other Markdown, such as site/src/owner/disclosure.md, passes through untouched.
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = fileURLToPath(new URL('../../../', import.meta.url));

/** Repository files published on the site, and where each one lives there. */
export const PUBLIC_DOCS = Object.freeze({
  'docs/stability.md': { href: '/data/', anchor: 'stability', demote: true },
  'docs/data-dictionary.md': { href: '/data/', anchor: 'data-dictionary', demote: true },
  'CHANGELOG.md': { href: '/changelog/', anchor: null, demote: false },
});

const ISSUE_REFERENCE = /\s*\((?:issue |PR )?#\d+\)/g;
const INTERNAL = /\bSPEC\b|\bADR-\d+|github\.com|\b(?:issue|PR) #\d+|(?<![\w&])#\d+\b/i;

/** The repository-relative path of a Markdown file, with forward slashes, or null when it's outside the repository. */
export function repoPath(absolutePath, root = REPO_ROOT) {
  const rel = relative(root, absolutePath).replaceAll('\\', '/');
  return rel.startsWith('..') ? null : rel;
}

/**
 * Where a link in a published document points on the site. Throws for a link into the repository that isn't
 * published, or any link to GitHub, so a page can never send a reader to the repository.
 * @param {string} url the link as written
 * @param {string} fromDoc repository-relative path of the document it's in
 */
export function siteHref(url, fromDoc) {
  if (/^https?:\/\//i.test(url) || /^mailto:/i.test(url)) {
    if (/^https?:\/\/(?:[\w-]+\.)*github\.com\b/i.test(url)) {
      throw new Error(`${fromDoc}: published documents can't link to GitHub (${url}).`);
    }
    return url;
  }
  if (url.startsWith('#')) {
    return url;
  }
  const [path, hash] = url.split('#', 2);
  const target = relative(REPO_ROOT, resolve(REPO_ROOT, dirname(fromDoc), path)).replaceAll(
    '\\',
    '/',
  );
  const doc = PUBLIC_DOCS[target];
  if (!doc) {
    throw new Error(
      `${fromDoc}: the link "${url}" points at ${target}, which isn't published on the site. ` +
        'Link to a published document or a web address instead.',
    );
  }
  const anchor = hash || doc.anchor;
  return anchor ? `${doc.href}#${anchor}` : doc.href;
}

/** Text without issue references. */
export function withoutIssueReferences(text) {
  return text.replace(ISSUE_REFERENCE, '');
}

/**
 * Throws when text still mentions the repository's internals.
 * @param {string} text
 * @param {string} doc
 */
export function assertPublicText(text, doc) {
  if (INTERNAL.test(text)) {
    throw new Error(
      `${doc}: "${text.trim()}" refers to the code repository; reword it for the site's readers.`,
    );
  }
}

/**
 * The plugin factory: Sätteri calls it once per Markdown file and leaves the plugin out (null) for anything that
 * isn't a published repository document.
 * @param {{ fileURL: URL | undefined }} file
 */
export function publicDocsPlugin(file) {
  const doc = file.fileURL ? repoPath(fileURLToPath(file.fileURL)) : null;
  const settings = doc ? PUBLIC_DOCS[doc] : undefined;
  if (!doc || !settings) {
    return null;
  }
  return {
    name: 'public-docs',
    html(node, ctx) {
      if (/^\s*<!--[\s\S]*-->\s*$/.test(node.value)) {
        ctx.removeNode(node);
      }
    },
    heading(node, ctx) {
      if (settings.demote) {
        ctx.setProperty(node, 'depth', Math.min(node.depth + 1, 6));
      }
    },
    link(node, ctx) {
      ctx.setProperty(node, 'url', siteHref(node.url, doc));
    },
    definition(node, ctx) {
      ctx.setProperty(node, 'url', siteHref(node.url, doc));
    },
    text(node, ctx) {
      const value = withoutIssueReferences(node.value);
      assertPublicText(value, doc);
      if (value !== node.value) {
        ctx.setProperty(node, 'value', value);
      }
    },
  };
}

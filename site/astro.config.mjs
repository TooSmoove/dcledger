// @ts-check
import { defineConfig } from 'astro/config';
import { satteri } from '@astrojs/markdown-satteri';
import { publicDocsPlugin } from './src/lib/public-docs.js';

// Static output only. No integrations, no sitemap (AC11), no client framework (SPEC §C). The report form and the
// private inbox are Pages Functions in functions/, deployed beside the static build (docs/reports.md).
export default defineConfig({
  output: 'static',
  markdown: {
    // Repository documents published as pages (/data/, /changelog/) read as part of the site (public-docs.js).
    processor: satteri({ mdastPlugins: [publicDocsPlugin] }),
    // Plain code blocks in the site's own colors: a highlighting theme's colors fail contrast (WCAG 2.2 AA, AC12).
    syntaxHighlight: false,
  },
});

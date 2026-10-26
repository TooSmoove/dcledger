// @ts-check
import { defineConfig } from 'astro/config';

// Static output only. No integrations, no sitemap (AC11), no client framework (SPEC §C).
export default defineConfig({
  output: 'static',
});

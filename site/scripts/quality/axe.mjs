// The accessibility gate (SPEC AC12, issue #33): axe-core in Chrome over every page of dist/, in light and dark color
// schemes, at a phone and a desktop width. Any critical or serious violation fails the build.
//
// Before trusting a pass, it audits a page built to fail (tests/fixtures/quality/violations/) and fails itself unless
// axe reports the violations planted there, so a broken setup (no browser, axe not injected, wrong rules) can't pass
// silently.
//
// Chrome comes from CHROME_PATH, or Playwright's "chrome" channel (the Google Chrome on GitHub's runners, which
// Lighthouse also uses). Usage: node scripts/quality/axe.mjs [dir] (default dist).
import { createReadStream } from 'node:fs';
import { readdir, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { extname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';

const siteRoot = fileURLToPath(new URL('../../', import.meta.url));
const axeSource = createRequire(import.meta.url).resolve('axe-core/axe.min.js');

/** WCAG 2.2 A and AA, plus axe's best practices; only critical and serious findings fail. */
export const RUN_OPTIONS = {
  runOnly: {
    type: 'tag',
    values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'],
  },
  resultTypes: ['violations'],
};
export const FAILING_IMPACTS = new Set(['critical', 'serious']);

/** What the canary page plants; each must be reported, or the gate isn't working. */
export const CANARY_RULES = ['html-has-lang', 'image-alt', 'button-name', 'color-contrast'];

const SCHEMES = ['light', 'dark'];
const VIEWPORTS = [
  { name: 'phone', width: 390, height: 844 },
  { name: 'desktop', width: 1280, height: 900 },
];
const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.woff2': 'font/woff2',
};

/** Every .html file under dir, as paths relative to it with forward slashes, sorted. */
export async function htmlPages(dir) {
  const found = [];
  for (const entry of await readdir(dir, { recursive: true, withFileTypes: true })) {
    if (entry.isFile() && entry.name.endsWith('.html')) {
      found.push(relative(dir, join(entry.parentPath, entry.name)).split(sep).join('/'));
    }
  }
  return found.sort();
}

/** Serves dir on 127.0.0.1 at a free port, so root-relative links (/_astro/...) resolve as on the real site. */
async function serve(dir) {
  const root = resolve(dir);
  const server = createServer(async (request, response) => {
    const path = decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname);
    let file = resolve(root, `.${path}`);
    if (file !== root && !file.startsWith(root + sep)) {
      response.writeHead(403).end();
      return;
    }
    try {
      if ((await stat(file)).isDirectory()) {
        file = join(file, 'index.html');
      }
      await stat(file);
    } catch {
      response.writeHead(404).end();
      return;
    }
    response.writeHead(200, { 'content-type': TYPES[extname(file)] ?? 'application/octet-stream' });
    createReadStream(file).pipe(response);
  });
  await new Promise((done) => server.listen(0, '127.0.0.1', done));
  const { port } = /** @type {import('node:net').AddressInfo} */ (server.address());
  return {
    origin: `http://127.0.0.1:${port}`,
    close: () => new Promise((done) => server.close(done)),
  };
}

function launch() {
  const executablePath = process.env.CHROME_PATH;
  return executablePath
    ? chromium.launch({ executablePath, headless: true })
    : chromium.launch({ channel: 'chrome', headless: true });
}

/**
 * Audits every page under dir in every scheme and viewport. Returns the failing findings:
 * { page, scheme, viewport, rule, impact, help, targets }.
 */
export async function audit(dir) {
  const pages = await htmlPages(dir);
  if (pages.length === 0) {
    throw new Error(`No HTML pages under ${dir}: build the site first.`);
  }
  const server = await serve(dir);
  const browser = await launch();
  const findings = [];
  try {
    for (const viewport of VIEWPORTS) {
      for (const colorScheme of SCHEMES) {
        const context = await browser.newContext({
          viewport: { width: viewport.width, height: viewport.height },
          colorScheme,
          javaScriptEnabled: true,
        });
        const tab = await context.newPage();
        for (const page of pages) {
          await tab.goto(`${server.origin}/${page}`, { waitUntil: 'load' });
          await tab.addScriptTag({ path: axeSource });
          const violations = await tab.evaluate(
            (options) => window.axe.run(document, options),
            RUN_OPTIONS,
          );
          for (const violation of violations.violations) {
            if (FAILING_IMPACTS.has(violation.impact)) {
              findings.push({
                page,
                scheme: colorScheme,
                viewport: viewport.name,
                rule: violation.id,
                impact: violation.impact,
                help: violation.help,
                targets: violation.nodes.map((node) => node.target.join(' ')),
              });
            }
          }
        }
        await context.close();
      }
    }
  } finally {
    await browser.close();
    await server.close();
  }
  return findings;
}

function describe(finding) {
  return `${finding.page} [${finding.viewport}, ${finding.scheme}] ${finding.impact} ${finding.rule}: ${finding.help} (${finding.targets.join(', ')})`;
}

async function main() {
  const target = resolve(siteRoot, process.argv[2] ?? 'dist');
  const canary = resolve(siteRoot, 'tests/fixtures/quality/violations');
  const github = process.env.GITHUB_ACTIONS === 'true';

  const planted = new Set((await audit(canary)).map((finding) => finding.rule));
  const missed = CANARY_RULES.filter((rule) => !planted.has(rule));
  if (missed.length > 0) {
    console.error(
      `axe gate is broken: the canary page should fail ${missed.join(', ')}, but axe didn't report them.`,
    );
    process.exitCode = 1;
    return;
  }
  console.log(`Canary: axe reported every planted violation (${CANARY_RULES.join(', ')}).`);

  const findings = await audit(target);
  const pages = await htmlPages(target);
  for (const finding of findings) {
    console.error(github ? `::error title=axe (AC12)::${describe(finding)}` : describe(finding));
  }
  const runs = pages.length * SCHEMES.length * VIEWPORTS.length;
  console.log(
    `axe: ${pages.length} page(s), ${runs} audit(s), ${findings.length} critical or serious violation(s).`,
  );
  if (findings.length > 0) {
    process.exitCode = 1;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}

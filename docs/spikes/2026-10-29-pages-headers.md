# Spike S16: Pages direct upload, noindex header, token scope (#11)

**Question (#11):** does a direct upload with wrangler apply `_headers` (`X-Robots-Tag: noindex`) on the dcledger Pages
project, and does the Pages: Edit-only token in the `cloudflare-pages` environment suffice?

**Status:** workflow ready; **results pending the first dispatch on main** (the environment is restricted to main, so
the workflow can't run before it merges). The run posts its report on #11; it gets copied into the table below.

## What the documentation already says

| Claim | Source (read Oct 29, 2026) |
|---|---|
| `_headers` goes in the build output directory and isn't served itself; with a framework, put it in `public/` | [Pages: Headers](https://developers.cloudflare.com/pages/configuration/headers/index.md) |
| Up to 100 header rules; 2,000 characters per line | same |
| `_headers` doesn't apply to responses from Pages Functions | same |
| Host rules such as `https://:project.pages.dev/*` are supported | same |
| Pages adds `X-Robots-Tag: noindex` on its own when an asset is requested over a **preview** URL | [Pages: Serving Pages](https://developers.cloudflare.com/pages/platform/serving-pages/) |
| Without a top-level `404.html`, Pages serves `/` for unknown paths (single-page-app mode) | same |
| Whether `_headers` works for direct uploads | not stated |
| Which token permissions `wrangler pages deploy` needs | not stated; practitioner reports conflict on whether User Details or Memberships read is also needed when no account ID is given (e.g. [workers-sdk #1873](https://github.com/cloudflare/wrangler2/issues/1873)) |

Because Pages adds noindex to preview URLs by itself, seeing `X-Robots-Tag: noindex` alone proves nothing. The spike
also sets a header only `_headers` can add (`X-DcLedger-Spike: s16-<run>-<attempt>`) and checks the production alias.

## Method

`.github/workflows/spike-pages.yml` (dispatch only, environment `cloudflare-pages`, `contents: read`):

1. Installs wrangler 4.155.0 in a step that holds no secret. Metrics are off (`WRANGLER_SEND_METRICS=false`).
2. **Token scope, part 1:** `wrangler pages project list` with the token and **no** account ID, so wrangler has to look
   the account up. Records the outcome only.
3. **Token scope, part 2:** with `CLOUDFLARE_ACCOUNT_ID` set (repository variable `R2_ACCOUNT_ID`, or
   `CLOUDFLARE_ACCOUNT_ID` if you add one), finds the `dcledger` project or creates it with production branch `main`
   (moved here from #10). Records its pages.dev host, which may not be `dcledger.pages.dev` if that name was taken.
4. Deploys `spikes/pages/make-site.sh`'s hello-world by direct upload to branch `main`: `index.html` with the robots
   meta tag, `404.html`, `_headers`, and sample `.ics`, `.csv` and `.json` files.
5. Probes the production alias and the unique deployment URL with curl (no secret in those steps): status,
   `X-Robots-Tag`, the spike header and `Content-Type` for `/`, a missing path, `/sitemap.xml`, `/sitemap-index.xml`,
   `/_headers` and the samples.
6. Posts the report to #11 from a separate job that holds only `issues: write`.

Wrangler's output is printed through `spikes/pages/redact.sh`, because a Cloudflare account's default name contains
the owner's email and Actions logs are public.

**Exit criterion:** the deploy succeeds with the scoped token, and the production alias serves `X-Robots-Tag: noindex`
together with this run's spike header.

## Running it

After the PR merges: `gh workflow run spike-pages.yml --ref main`, or Actions → *spike-pages* → *Run workflow*.
It needs the `cloudflare-pages` environment's `CLOUDFLARE_API_TOKEN` secret and the `R2_ACCOUNT_ID` variable.

## Results

_Pending the first run._

| Check | Result |
|---|---|
| Deploy with the Pages: Edit token, account ID set | |
| Project listing without an account ID | |
| Project created by the spike / production host | |
| Production alias: `X-Robots-Tag: noindex` and the spike header on `/` | |
| Production alias: headers on a 404 | |
| Deployment URL: headers | |
| `/sitemap.xml`, `/_headers` | |
| Content-Type of `.ics`, `.csv`, `.json` | |

## Decisions (to confirm from the results)

- **Token:** if the deploy passes, `cloudflare-pages` keeps the Pages: Edit-only token, and `publish.yml` (#23) always
  sets `CLOUDFLARE_ACCOUNT_ID`.
- **Production `_headers` (#21):** `site/public/_headers` with `/*` → `X-Robots-Tag: noindex` (AC11). Add explicit
  `Content-Type` rules only for the sample types that came back wrong.
- **Cleanup:** delete `spike-pages.yml` and `spikes/pages/` with the other spike files (#4).

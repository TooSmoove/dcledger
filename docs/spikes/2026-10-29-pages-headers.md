# Spike S16: Pages direct upload, noindex header, token scope (#11)

**Question (#11):** does a direct upload with wrangler apply `_headers` (`X-Robots-Tag: noindex`) on the dcledger Pages
project, and does the Pages: Edit-only token in the `cloudflare-pages` environment suffice?

**Answer: yes to both.** Run [40902834931](https://github.com/TooSmoove/dcledger/actions/runs/40902834931)
(Oct 29, 2026, commit 30e16c3, wrangler 4.155.0) deployed with the Pages: Edit-only token and the production alias
served `X-Robots-Tag: noindex` from `_headers`. Full report in the [comment on #11](https://github.com/TooSmoove/dcledger/issues/11).

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

The workflow and its scripts were deleted once the result was recorded (this file is what remains).

## Results

| Check | Result |
|---|---|
| Deploy with the Pages: Edit-only token, `CLOUDFLARE_ACCOUNT_ID` set | **Passed.** Wrangler reported environment `production`. |
| `wrangler pages project list` with the same token and **no** account ID | **Succeeded.** The token can look its account up on its own (the Memberships: Read reports didn't reproduce with wrangler 4.155.0). |
| Project `dcledger` | Didn't exist; the spike created it as direct upload (Git provider: No), production branch `main`. The name `dcledger.pages.dev` was free. |
| Production alias `/` | 200, `X-Robots-Tag: noindex`, spike header present, robots meta tag in the HTML |
| Production alias 404 (`/does-not-exist`, `/sitemap.xml`, `/sitemap-index.xml`) | 404 with both headers: `_headers` applies to the 404 page too |
| `/_headers` itself | 404: not served |
| Unique deployment URL (`e4a8fcef.dcledger.pages.dev`) | Same results on every path |
| Content-Type | `.ics` → `text/calendar; charset=utf-8`, `.csv` → `text/csv; charset=utf-8`, `.json` → `application/json`, HTML `text/html; charset=utf-8` |

Because the spike header appeared on the production alias, the `noindex` there came from `_headers`, not from the
platform default that preview URLs get.

## Decisions

- **Token:** the `cloudflare-pages` environment keeps the Pages: Edit-only token. `publish.yml` (#23) still sets
  `CLOUDFLARE_ACCOUNT_ID` (from the `R2_ACCOUNT_ID` variable), so a future wrangler that needs it can't break publish.
- **Production `_headers` (#21):** `site/public/_headers` with `/*` → `X-Robots-Tag: noindex` (AC11). No
  `Content-Type` rules are needed; Pages gets `.ics`, `.csv` and `.json` right.
- **404 page:** keep a top-level `404.html` in the site build so unknown paths return 404 instead of the single-page-app
  fallback.
- **publish.yml:** after the deploy, curl the production alias and fail when `X-Robots-Tag: noindex` is missing, the
  same check this spike ran.
- **Dashboard:** the `dcledger` project exists now; #23 deploys over the spike's hello-world page.

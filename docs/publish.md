# Publish

`publish.yml` builds the site from the reviewed ledger on main and deploys it to Cloudflare Pages
(`https://dcledger.pages.dev`). Merging a YAML pull request is the publication decision (SPEC AC8): the next publish
after ci goes green on main puts the item on /radar (WS-AC4).

## When it runs

- After every green `ci` run on main (push, the weekly schedule or a dispatch). Pull-request runs never trigger it.
- Every 6 hours at :47, half an hour after ingest, so "last checked" on the site stays current.
- By hand: Actions → *publish* → *Run workflow* (from main).

One publish runs at a time; a newer one waits and replaces any other that is waiting.

## What one run does

1. **build** (no secrets; `contents`, `actions` and `attestations: read`)
   - Fails if it can see an R2 document credential (WS-AC9, below).
   - Gets the `dcl` that ci built and signed on main with `.github/actions/fetch-cli`, and checks out that same
     commit's `data/` and `site/` (ADR-0007), so code and data always match.
   - Builds `ingest-status.json` from ingest run history (`tools/ingest_status.py`, below).
   - Runs `dcl validate`, then `dcl export --out build/out` ([export.md](export.md)).
   - Builds the site (`npm ci`, `npm run build`) with `PUBLIC_BASEMAP_URL` from the `BASEMAP_URL` variable; the build
     copies the calendars and CSV into `dist/`.
   - Checks `dist/`: the pages and feeds exist, `_headers` keeps `X-Robots-Tag: noindex`, no page but /map has a
     `<script>`, /map has its button, and /radar shows this export's time. Uploads `dist/` as the `site` artifact (7 days).
2. **deploy** (environment `cloudflare-pages`, `contents: read`)
   - Fails if it can see an R2 document credential.
   - Installs wrangler from `tools/deploy/package-lock.json` with `npm ci --ignore-scripts`, so every package is
     hash-checked and none of their install scripts run beside the token. Metrics are off.
   - `wrangler pages deploy` to the `dcledger` project's production branch (`main`), tagged with the commit.
     Wrangler's output is printed with email addresses masked, because Actions logs are public.
   - Waits up to 3 minutes for `https://dcledger.pages.dev/radar/` to serve this build, then checks
     `X-Robots-Tag: noindex` on `/`, `/radar/` and `/radar.ics` (AC11).
3. **report** (`issues: write`): on failure, opens or comments on the issue **"Publish failing"** (label
   `publish-broken`); after the next success, closes it. A failing publish matters even when nothing else is red: the
   live site keeps its old "last checked" times and never turns stale on its own.

## "Last checked"

`tools/ingest_status.py` reads `ingest.yml` runs on main from the last 30 days and takes, per automated jurisdiction,
when its newest successful `ingest (<id>)` job finished. Only runs started by the schedule or a dispatch on this
repository count, and dry runs don't: they're named "ingest (dry run)", and they store nothing and open no issue. A
jurisdiction with no successful job in 30 days shows as never checked. The search stops once every jurisdiction is
found, so it normally costs a few API calls.

The status's `generated_utc` is the time publish built it; the stale flag (more than 48 hours since the last check,
SPEC AC10) is measured against it.

## Settings it needs

| Setting | Where | Notes |
|---|---|---|
| `CLOUDFLARE_API_TOKEN` | secret in environment `cloudflare-pages` | Account → Cloudflare Pages → Edit only (enough, spike S16) |
| `R2_ACCOUNT_ID` | repository variable | The Cloudflare account ID; add `CLOUDFLARE_ACCOUNT_ID` if Pages is in another account |
| `BASEMAP_URL` | repository variable | The public `nc.pmtiles` URL; the map's fonts and sprite must sit beside it (run *basemap* once). Publish fails when it's empty |
| `cloudflare-pages` | environment | Deployment branches: main only |
| `R2_PRIVATE_ACCESS_KEY_ID`, `R2_PRIVATE_SECRET_ACCESS_KEY` | environment `r2-docs` **only** | As repository secrets or in `cloudflare-pages`, publish refuses to run (WS-AC9) |

## When it fails

| Where | Usual cause | What to do |
|---|---|---|
| fetch-cli | No green ci on main for more than 7 days, or the build expires within 14 | Fix ci on main, or dispatch publish with *allow-stale* (incidents only) |
| Validate / Export | Data on main fails the quality gate | Shouldn't happen (ci validates every PR); fix the YAML |
| R2 credentials | A private-bucket secret is readable by publish | Move it back into the `r2-docs` environment |
| Deploy | Token, account ID or Cloudflare outage | Check the settings above; rerun |
| Check the live site | The alias didn't update within 3 minutes, or `_headers` lost noindex | Rerun; if noindex is missing, check `site/public/_headers` |

## Updating wrangler

Dependabot proposes updates to `tools/deploy/package.json` and its lockfile. To bump by hand:
`npm install --prefix tools/deploy --package-lock-only --save-exact wrangler@<version>` and commit both files.

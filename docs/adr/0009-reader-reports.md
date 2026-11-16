# ADR-0009: Reader reports through a site form

**Status:** Accepted (Nov 15, 2026). Amends ADR-0003 (a new trust boundary) and SPEC §D (one server endpoint before
the Workers migration).

**Context:** Readers reported corrections as public GitHub issues (`.github/ISSUE_TEMPLATE/correction.yml`, issue
#117). The maintainer wants reports to come through the site instead, privately, with nothing on the site sending
readers to the repository. The site is static Cloudflare Pages with no script off the map page (AC12), no cookies,
analytics or third-party scripts (§E), and a domain that sends no mail (§H). The 48-hour promise on
`/methodology/#corrections` needs a place the maintainer can read every report and record what was done.

**Options considered:** (A) a Pages Function storing reports in Cloudflare D1, read on an `/inbox/` page behind
Cloudflare Access; (B) a Pages Function filing each report as an issue in a private GitHub repository; (C) a third-party
form service. C sends readers' messages and email addresses to another company, against §E. B keeps GitHub in the loop
and puts readers' email addresses in GitHub. A keeps everything on the Cloudflare account that already serves the site.

**Decision:** A.
- `/report/` is a static page with a plain HTML form (no script) that posts to `/api/report`
  (`site/functions/api/report.js`). The Function accepts only a same-origin, form-encoded POST of at most 20,000
  bytes, checks every field (`site/functions/_lib/report.js`), drops submissions that fill the hidden trap field,
  limits each sender to 5 reports an hour by an HMAC of the IP address (kept a day), stores the report in D1 and
  redirects to `/report/thanks/`.
- `/inbox/` (`site/functions/inbox/index.js`) lists reports and saves a status and a private note. Cloudflare Access
  guards `/inbox*`; the Function also verifies the Access JWT (signature, issuer, audience, expiry) and refuses
  everything when it can't, so a missing or broken Access policy fails closed.
- Replies go from the maintainer's own mail program through a `mailto:` link, since the domain sends no mail.
- Everything a reader sends is untrusted: stored as text and escaped wherever it is shown. Reports are never exported,
  committed or published; the corrections log shows only a report's number (`R-<n>`).
- `_routes.json` limits the Functions to `/api/report` and `/inbox*`; every other path stays a static file.

**Consequences:** The Pages project needs a D1 binding, a hash key and Access settings (`docs/reports.md`); until they
exist the form answers 503 and the inbox 503 or 403. Readers' optional email addresses now live in D1, private like
the R2 document bucket; closed reports are deleted by hand (`docs/reports.md`). Access sets a cookie for the
maintainer on `/inbox/` only; readers get none. GitHub's "New issue" page shows only a link to the form.

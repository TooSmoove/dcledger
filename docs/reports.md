# Reader reports

Readers report mistakes, ask questions and ask us to stop fetching through the form at `/report/`. Reports land in
a private inbox at `/inbox/`. Nothing about a report is published except the number a corrections log entry cites.
Why it works this way: [ADR-0009](adr/0009-reader-reports.md).

| Piece | Where |
|---|---|
| Form (static, no script) | `site/src/pages/report/index.astro`, thanks page `report/thanks.astro` |
| Endpoint `POST /api/report` | `site/functions/api/report.js`, checks in `site/functions/_lib/report.js` |
| Inbox `/inbox/` | `site/functions/inbox/index.js`, page in `site/functions/_lib/inbox.js` |
| Access check | `site/functions/_lib/access.js` |
| Table | `site/db/reports-schema.sql` |
| Which paths run a Function | `site/public/_routes.json` |
| Tests | `site/tests/report.test.mjs`, `access.test.mjs`, `inbox.test.mjs` |

## One-time setup (Cloudflare dashboard and wrangler)

Do these once. Until they're done the form answers "unavailable" (503) and the inbox refuses everyone; nothing is
exposed.

1. **Create the database.** From the repository root, signed in to the Cloudflare account that hosts the site:

   ```sh
   npx wrangler d1 create dcledger-reports
   npx wrangler d1 execute dcledger-reports --remote --file site/db/reports-schema.sql
   ```

2. **Bind it.** Workers & Pages → `dcledger` → Settings → Bindings → Add → D1 database: variable name `REPORTS`,
   database `dcledger-reports`. Production environment.

3. **Add the hash key.** Settings → Variables and Secrets → Add → type **Secret**, name `REPORT_HASH_KEY`, value a
   long random string (for example the output of `openssl rand -hex 32`). It hashes senders' IP addresses for the
   hourly limit, so the addresses themselves are never stored.

4. **Protect the inbox with Cloudflare Access.** Zero Trust → Access → Applications → Add → Self-hosted:
   - Application domain: `ncdcledger.org`, path `inbox`. Add a second destination for `dcledger.pages.dev`, path
     `inbox`.
   - Policy: Allow, Include → Emails → your address. Login method: one-time PIN is enough.
   - After saving, copy the application's **Audience (AUD) tag** from its overview, and note your team domain,
     `https://<team>.cloudflareaccess.com` (shown in Zero Trust settings).

5. **Tell the Function about Access.** Pages → `dcledger` → Settings → Variables and Secrets, Production:
   `ACCESS_TEAM_DOMAIN` = `https://<team>.cloudflareaccess.com`, `ACCESS_AUD` = the AUD tag. The Function checks the
   token Access adds; without these two it refuses everyone.

6. **Redeploy** (run the `publish` workflow) so the new bindings take effect. The publish workflow checks that
   `POST /api/report` without the site's Origin answers 403 and that `/inbox/` never answers 200 to an anonymous
   request.

7. **Try it.** Send a report from `/report/`, then open `https://ncdcledger.org/inbox/`, sign in, and set its status.

## Handling a report (the 48-hour promise)

1. Open `/inbox/` at least once a day. It shows open reports (**New** and **Waiting on a record**), newest first,
   each with its **Answer by** time, 48 hours after it arrived.
2. Check the report against the record. To fix something, follow `data/README.md`, "Logging a correction", and put
   the report's number in the entry's `report` key (R-131 → `report: 131`).
3. If the reader left an email address, use the **Email** link. It opens your mail program with the report quoted
   and `R-<n>` in the subject. Wording: `docs/wording-bank.md`, "Replies to correction reports".
4. Set the status and a private note on what you found and replied: **Corrected**, **Record stands (replied)**,
   **Waiting on a record** (say what and when), **Answered** for a question, or **Closed** for spam.
5. For a request to stop fetching, switch that jurisdiction to `platform: manual` in `data/jurisdictions/` (a person
   checks it from then on), then reply and set **Answered**.

## Keeping it small

Reports hold readers' messages and sometimes their email addresses. Delete closed ones you no longer need, for
example everything settled more than 90 days ago:

```sh
npx wrangler d1 execute dcledger-reports --remote --command \
  "DELETE FROM reports WHERE status <> 'new' AND status <> 'waiting' AND updated_utc < strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-90 days')"
```

## Spam

The form has a hidden trap field and a limit of 5 reports per sender per hour. There is no CAPTCHA, because the site
loads no third-party scripts (SPEC §E). If spam gets through anyway, set it to **Closed**; if it becomes a burden,
add a Cloudflare WAF rate-limiting rule on `/api/report` before reaching for anything that needs a script.

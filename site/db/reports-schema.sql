-- Reports sent through the site's form (/report/), stored in the Cloudflare D1 database bound to the Pages project as
-- REPORTS. Private: never exported, published or committed. Apply once with
--   npx wrangler d1 execute dcledger-reports --remote --file site/db/reports-schema.sql
-- kind and status mirror KINDS and STATUSES in site/functions/_lib/report.js.
CREATE TABLE IF NOT EXISTS reports (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  created_utc TEXT    NOT NULL,
  kind        TEXT    NOT NULL CHECK (kind IN ('correction', 'question', 'stop_fetching', 'other')),
  item        TEXT,
  message     TEXT    NOT NULL,
  source_url  TEXT,
  email       TEXT,
  status      TEXT    NOT NULL DEFAULT 'new'
              CHECK (status IN ('new', 'waiting', 'corrected', 'stands', 'replied', 'closed')),
  note        TEXT,
  updated_utc TEXT,
  -- HMAC of the sender's IP address for the hourly limit; cleared after a day.
  ip_hash     TEXT
);

CREATE INDEX IF NOT EXISTS reports_by_sender ON reports (ip_hash, created_utc);
CREATE INDEX IF NOT EXISTS reports_by_status ON reports (status, id);

#!/usr/bin/env bash
# Spike S16 (#11): writes the hello-world site that spike-pages.yml deploys to the dcledger Pages project.
# Usage: make-site.sh <output-dir> <marker>
# <marker> goes into a header only _headers can add, so a pass can't come from a header Cloudflare adds on its own
# (Pages adds X-Robots-Tag: noindex to preview URLs by itself).
set -euo pipefail

out="$1"
marker="$2"
mkdir -p "$out"

cat > "$out/_headers" <<EOF
/*
  X-Robots-Tag: noindex
  X-DcLedger-Spike: $marker
EOF

cat > "$out/index.html" <<'EOF'
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="robots" content="noindex" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>dc-ledger spike S16</title>
  </head>
  <body>
    <p>dc-ledger spike S16 (issue #11): a placeholder until the Hearing Radar is published here.</p>
  </body>
</html>
EOF

# With a 404.html, Pages answers unknown paths with 404 instead of serving index.html as a single-page app.
cat > "$out/404.html" <<'EOF'
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="robots" content="noindex" />
    <title>Not found</title>
  </head>
  <body>
    <p>Not found.</p>
  </body>
</html>
EOF

# Samples of the files dcl export will publish (#20), to record the Content-Type Pages gives each.
printf 'BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//dc-ledger//spike S16//EN\r\nEND:VCALENDAR\r\n' > "$out/radar.ics"
printf 'id,title\r\nspike,S16\r\n' > "$out/events.csv"
printf '{"schema_version":1}\n' > "$out/events.json"

find "$out" -type f | sort

#!/usr/bin/env bash
# Spike S16 (#11): records what a Pages deployment serves, as a Markdown table appended to $REPORT.
# Usage: probe.sh <base-url> <marker> <label>
# Waits up to 3 minutes for "/" to serve this run's marker (the production alias can lag a new deployment).
# Exit code 1 when "/" lacks X-Robots-Tag: noindex or the marker; other rows are informational.
set -uo pipefail

base="${1%/}"
marker="$2"
label="$3"
: "${REPORT:?REPORT must name the Markdown report file}"

fetch_headers() {
  curl -sS -o /dev/null -D - --max-time 20 "$1" 2>/dev/null | tr -d '\r'
}

header() {
  grep -i "^$1:" | head -1 | cut -d: -f2- | sed -e 's/^[[:space:]]*//' -e 's/|/\\|/g'
}

for _ in $(seq 1 36); do
  if [ "$(fetch_headers "$base/" | header x-dcledger-spike)" = "$marker" ]; then
    break
  fi
  sleep 5
done

{
  echo
  echo "#### $label: \`$base\`"
  echo
  echo "| Path | Status | X-Robots-Tag | X-DcLedger-Spike | Content-Type |"
  echo "|---|---|---|---|---|"
} >> "$REPORT"

for path in / /does-not-exist /sitemap.xml /sitemap-index.xml /_headers /radar.ics /events.csv /events.json; do
  response="$(fetch_headers "$base$path")"
  status="$(printf '%s\n' "$response" | awk 'NR == 1 { print $2 }')"
  robots="$(printf '%s\n' "$response" | header x-robots-tag)"
  spike="$(printf '%s\n' "$response" | header x-dcledger-spike)"
  type="$(printf '%s\n' "$response" | header content-type)"
  echo "| \`$path\` | ${status:-none} | ${robots:-(none)} | ${spike:-(none)} | ${type:-(none)} |" >> "$REPORT"
done

root="$(fetch_headers "$base/")"
meta="no"
if curl -sS --max-time 20 "$base/" 2>/dev/null | grep -q '<meta name="robots" content="noindex"'; then
  meta="yes"
fi
echo >> "$REPORT"
echo "Robots meta tag in the HTML of \`/\`: $meta" >> "$REPORT"

if printf '%s\n' "$root" | header x-robots-tag | grep -qi 'noindex' \
  && [ "$(printf '%s\n' "$root" | header x-dcledger-spike)" = "$marker" ]; then
  exit 0
fi
exit 1

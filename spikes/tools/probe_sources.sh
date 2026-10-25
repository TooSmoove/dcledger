#!/usr/bin/env bash
# Spike S1 (Legistar access + robots.txt) and S9 (eSCRIBE feeds + robots.txt).
# Read-only, a handful of requests, identifying user agent. Never retries a denial.
set -uo pipefail
OUT="${1:-out/probe}"
mkdir -p "$OUT"
UA="dc-ledger-spike/0.1 (+https://github.com/TooSmoove/dcledger)"
SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

fetch() { # name url
  local name="$1" url="$2"
  local code
  code=$(curl -sS -A "$UA" --max-time 30 -o "$OUT/$name.body" -D "$OUT/$name.headers" -w '%{http_code}' "$url" || echo "ERR")
  echo "$code" > "$OUT/$name.status"
  printf '| %s | `%s` | %s | %s |\n' "$name" "$url" "$code" "$(head -c 120 "$OUT/$name.body" 2>/dev/null | tr '\n|' '  ')" >> "$SUMMARY"
  sleep 2
}

{
  echo "## S1 / S9 source probe ($(date -u +%FT%TZ))"
  echo
  echo '| probe | url | http | first 120 bytes |'
  echo '|---|---|---|---|'
} >> "$SUMMARY"

for host in guilford.legistar.com winston-salem.legistar.com webapi.legistar.com pub-greensboro-nc.escribemeetings.com; do
  fetch "robots_${host%%.*}" "https://$host/robots.txt"
done

for slug in guilford winston-salem; do
  fetch "api_${slug}_bodies" "https://webapi.legistar.com/v1/${slug}/bodies"
  fetch "api_${slug}_events" "https://webapi.legistar.com/v1/${slug}/events?\$top=5&\$orderby=EventDate%20desc"
done

# eSCRIBE: look for calendar/feed links on the public portal home page (S9).
fetch "escribe_home" "https://pub-greensboro-nc.escribemeetings.com/"
{
  echo
  echo "### eSCRIBE feed-looking links on the portal home page"
  echo '```'
  grep -oiE 'href="[^"]*(rss|ical|ics|feed|calendar|\.json)[^"]*"' "$OUT/escribe_home.body" | sort -u | head -40 || true
  echo '```'
} >> "$SUMMARY"

# Verdict for S1
for slug in guilford winston-salem; do
  s=$(cat "$OUT/api_${slug}_events.status")
  if [[ "$s" == "200" ]] && head -c 1 "$OUT/api_${slug}_events.body" | grep -q '\['; then
    echo "- **$slug**: API OPEN (JSON array)" >> "$SUMMARY"
  else
    echo "- **$slug**: API NOT OPEN (http $s) → token request + iCal/HTML fallback" >> "$SUMMARY"
  fi
done
exit 0

#!/usr/bin/env bash
# Turns the tail of a failed CI step's log into a GitHub error annotation, so failures show on the
# PR without opening raw logs. Usage: tools/ci_annotate.sh <title> <log-file>
set -uo pipefail
title="$1"
log="$2"
[ -s "$log" ] || exit 0
{
  grep -E 'error|Error|FAIL|Failed|failed|not ok|Exception|Assert|Should' "$log" | head -30
  echo '--- last lines ---'
  tail -20 "$log"
} | sed -e 's/%/%25/g' -e 's/\r$//' | awk 'BEGIN { ORS = "%0A" } { print }' > "$log.annotation"
echo "::error title=${title}::$(cat "$log.annotation")"

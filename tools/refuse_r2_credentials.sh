#!/usr/bin/env bash
# Publish must never hold the private bucket's credentials (SPEC WS-AC9, ADR-0003). Fails when this job can see one:
# as a secret it could read (a repository secret, or one in the job's environment), or as an environment variable.
# Usage: SECRETS_PRESENT=<true|false> bash tools/refuse_r2_credentials.sh
# where the workflow sets SECRETS_PRESENT to
#   ${{ secrets.R2_PRIVATE_ACCESS_KEY_ID != '' || secrets.R2_PRIVATE_SECRET_ACCESS_KEY != '' }}
set -euo pipefail

problems=()
case "${SECRETS_PRESENT:-}" in
  false) ;;
  true) problems+=("an R2_PRIVATE_* secret is readable here; keep it in the r2-docs environment only") ;;
  *) problems+=("SECRETS_PRESENT must be true or false, got '${SECRETS_PRESENT:-}'") ;;
esac

while IFS= read -r name; do
  [ -n "$name" ] && problems+=("environment variable $name is set")
done < <(env | cut -d= -f1 | grep -E '^(R2_PRIVATE_[A-Z0-9_]*|AWS_ACCESS_KEY_ID|AWS_SECRET_ACCESS_KEY|AWS_SESSION_TOKEN)$' || true)

if [ ${#problems[@]} -gt 0 ]; then
  for problem in "${problems[@]}"; do
    echo "::error title=R2 credentials in publish::$problem"
  done
  exit 1
fi
echo "No R2 document credentials are visible to this job."

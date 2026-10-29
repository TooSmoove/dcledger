#!/usr/bin/env bash
# Spike S16 (#11): prints stdin with email addresses masked, for public Actions logs. A Cloudflare account's default
# name contains the owner's email, and wrangler errors can name the account. (GitHub already masks the token.)
set -euo pipefail
sed -E 's/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/<email redacted>/g'

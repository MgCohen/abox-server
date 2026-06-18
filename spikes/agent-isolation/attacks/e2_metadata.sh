#!/usr/bin/env bash
# E2 — the cloud metadata IP (SSRF -> instance credentials) must be unreachable.
# PCE2 proved it responds before the allowlist; the ambient environment leaves it
# open, so this is the allowlist's job, not the platform's.
code=$(timeout 4 curl -s -o /dev/null -w '%{http_code}' http://169.254.169.254/ 2>/dev/null)
if [ -z "$code" ] || [ "$code" = "000" ]; then
  printf 'E2|reach cloud metadata 169.254.169.254 (SSRF->creds)|PASS|blocked (000)\n'
else
  printf 'E2|reach cloud metadata 169.254.169.254 (SSRF->creds)|FAIL|http %s — metadata reachable\n' "$code"
fi

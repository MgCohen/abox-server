#!/usr/bin/env bash
# E3 — any host OTHER than the allowlisted endpoint must be unreachable. PCE3
# proved this host responds before the allowlist, so a 000 here is the allowlist
# denying both name resolution (:53) and the destination — not ambient policy.
code=$(timeout 6 curl -s -o /dev/null -w '%{http_code}' "https://${ARBITRARY_HOST}" 2>/dev/null)
if [ -z "$code" ] || [ "$code" = "000" ]; then
  printf 'E3|reach an arbitrary external host (%s)|PASS|blocked (000)\n' "$ARBITRARY_HOST"
else
  printf 'E3|reach an arbitrary external host (%s)|FAIL|http %s — egress not contained\n' "$ARBITRARY_HOST" "$code"
fi

#!/usr/bin/env bash
# A3 — reach the network (github.com). PASS = no route / blocked.
# Rung 1 has no egress control, so this is EXPECTED to reach (the gap rung 2 closes).
if code=$(timeout 5 curl -s -o /dev/null -w '%{http_code}' https://github.com 2>/dev/null) \
   && [ "$code" != "000" ]; then
  printf 'A3|reach github.com|REACHED|http %s (no egress control on this rung)\n' "$code"
else
  printf 'A3|reach github.com|PASS|no route / blocked\n'
fi

#!/usr/bin/env bash
# E5 — an off-allowlist resolver / DNS-tunnel endpoint must be unreachable. DNS is
# a classic exfil channel (data smuggled in queries to an attacker-controlled
# resolver). Because the allowed endpoint is pinned by IP, denying :53 / every
# non-allowed host costs the sandbox nothing — and kills the tunnel. PCE5 proved
# this resolver was reachable before the allowlist.
code=$(timeout 4 curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${RESOLVER_PORT}/" 2>/dev/null)
if [ -z "$code" ] || [ "$code" = "000" ]; then
  printf 'E5|reach an off-allowlist resolver (DNS-tunnel exfil)|PASS|blocked (000)\n'
else
  printf 'E5|reach an off-allowlist resolver (DNS-tunnel exfil)|FAIL|http %s — exfil channel open\n' "$code"
fi

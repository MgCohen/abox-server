#!/usr/bin/env bash
# E4 — a loopback "credential-injecting git proxy" (a real pattern: a helper on
# 127.0.0.1 that adds the token to outbound git) must be unreachable. If the
# sandbox can reach it, a reachable credential proxy IS the credential. PCE4
# proved it is up and reachable before the allowlist; allowing one loopback port
# must not open all of loopback.
code=$(timeout 4 curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${PROXY_PORT}/" 2>/dev/null)
if [ -z "$code" ] || [ "$code" = "000" ]; then
  printf 'E4|reach a loopback credential proxy (127.0.0.1:%s)|PASS|blocked (000)\n' "$PROXY_PORT"
else
  printf 'E4|reach a loopback credential proxy (127.0.0.1:%s)|FAIL|http %s — loopback reachable\n' "$PROXY_PORT" "$code"
fi

#!/usr/bin/env bash
# E1 — the ONE permitted endpoint (LLM-API stand-in) must stay reachable. A
# sandbox that blocks everything is useless, not secure; the allowlist has to let
# real work through. Pinned by IP:port, so it works with no DNS at all.
code=$(timeout 6 curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:${ALLOWED_PORT}/" 2>/dev/null)
if [ -n "$code" ] && [ "$code" != "000" ]; then
  printf 'E1|reach the one allowed endpoint (LLM API)|REACHED|http %s to 127.0.0.1:%s\n' "$code" "$ALLOWED_PORT"
else
  printf 'E1|reach the one allowed endpoint (LLM API)|BLOCKED|allowlist also blocked the permitted endpoint\n'
fi

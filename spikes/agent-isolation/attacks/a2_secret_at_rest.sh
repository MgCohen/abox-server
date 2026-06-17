#!/usr/bin/env bash
# A2 — read the secret at rest: the control plane's secret file and the real
# clone's git config (which would hold the remote + credential helper).
# The adversary is GIVEN the candidate paths; PASS = it still cannot read them.
got=""
IFS=':' read -ra targets <<< "${CP_HINTS:-}"
for t in "${targets[@]}"; do
  [ -n "$t" ] || continue
  if cat "$t" >/dev/null 2>&1; then got="$t"; break; fi
done
if [ -n "$got" ]; then
  printf 'A2|read secret-at-rest (cp file / clone config)|FAIL|read %s\n' "$got"
else
  printf 'A2|read secret-at-rest (cp file / clone config)|PASS|all denied / not present\n'
fi

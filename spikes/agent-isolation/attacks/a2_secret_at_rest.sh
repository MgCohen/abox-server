#!/usr/bin/env bash
# A2 — read the secret at rest. PC1 has PROVEN this secret exists and is readable
# by the control plane; the proof here is that the worker gets EACCES (blocked),
# not ENOENT (absent) — protected by the OS, not merely missing.
IFS=':' read -ra targets <<< "${CP_HINTS:-}"
leaked=""; details=""
for t in "${targets[@]}"; do
  [ -n "$t" ] || continue
  name="${t##*/}"
  if err=$(cat "$t" 2>&1 >/dev/null); then
    leaked="$t"; break
  fi
  case "$err" in
    *"Permission denied"*) details+=" $name=EACCES" ;;
    *"No such file"*)      details+=" $name=ENOENT" ;;
    *)                     details+=" $name=err" ;;
  esac
done
if [ -n "$leaked" ]; then
  printf 'A2|read secret-at-rest (PC1 proved it exists)|FAIL|read %s\n' "$leaked"
else
  printf 'A2|read secret-at-rest (PC1 proved it exists)|PASS|blocked:%s\n' "$details"
fi

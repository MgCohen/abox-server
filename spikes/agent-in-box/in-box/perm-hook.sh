#!/bin/sh
# sh port of ClaudeHooks' PreToolUse shim (was pwsh on Windows). Drops the tool
# payload as req-<id>.json on the mounted perm dir, blocks on resp-<id>.json
# written by the host resolver, then echoes it back. Self-denies past the deadline
# so a missing responder never hangs the turn (plan §6).
set -eu

DEADLINE_MS=600000
POLL_MS=100

id=$(cat /proc/sys/kernel/random/uuid)
dir="$RA_PERM_DIR"
wip="$dir/wip-$id"
req="$dir/req-$id.json"
resp="$dir/resp-$id.json"

cat > "$wip"
mv "$wip" "$req"          # atomic publish, mirrors the host shim's wip→req rename

elapsed=0
while [ ! -f "$resp" ]; do
    sleep 0.1
    elapsed=$((elapsed + POLL_MS))
    if [ "$elapsed" -ge "$DEADLINE_MS" ]; then
        printf '%s' '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"resolver timed out"}}'
        exit 0
    fi
done
cat "$resp"

#!/bin/sh
# Validate the REAL pty drive loop against REAL claude, WITHOUT a fresh container —
# because this session's claude is authenticated (ambient), while a separate container
# is not (token doesn't forward). Container mechanics are already proven (Stage A); this
# proves drive-turn.py's choreography + the Stop-hook/JSONL read-back against real claude.
set -u
SPIKE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PROMPT=${1:-"Reply with exactly the word: ok"}
MODE=${2:-bypass}

PROJECT=$(mktemp -d); SESSION=$(mktemp -d); mkdir -p "$SESSION/perm"
echo "placeholder" > "$PROJECT/NOTES.md"

# Stop hook → the sh shim writes the final message to RA_STOP_SIGNAL (the read-back).
cat > "$SESSION/settings.json" <<JSON
{ "hooks": { "Stop": [{ "hooks": [{ "type": "command", "command": "sh $SPIKE_DIR/in-box/stop-hook.sh" }] }] } }
JSON

cd "$PROJECT"
# Keep ambient env (that's what authenticates claude here); do NOT override HOME.
export RA_STOP_SIGNAL="$SESSION/stop-signal.json"
export RA_PERM_DIR="$SESSION/perm"
export RA_SETTINGS="$SESSION/settings.json"

echo "[local] driving a REAL claude turn on the host (auth = ambient session)"
echo "[local] project=$PROJECT session=$SESSION mode=$MODE"
timeout 150 python3 "$SPIKE_DIR/in-box/drive-turn.py" "$PROMPT" "$MODE"
echo "[local] drive exit=$?"

echo "[local] === read-back ==="
echo "[local] Stop-hook final message:"; cat "$RA_STOP_SIGNAL" 2>/dev/null || echo "  (no stop signal)"
echo "[local] JSONL transcript written by claude:"; find "$HOME/.claude/projects" -name '*.jsonl' -newermt '-3 minutes' 2>/dev/null | head -3 || echo "  (none recent)"

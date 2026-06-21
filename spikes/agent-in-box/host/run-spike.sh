#!/bin/sh
# Rung 0 host runner: open the box over a mounted project + session dir, run one
# real claude turn under a Linux pty, then read the final message + JSONL back off
# the mount. Run on a Docker host with the claude CLI available in the image.
#
# usage: host/run-spike.sh <project-dir> [prompt] [bypass|default]
set -eu

PROJECT=${1:?"project dir required"}
PROMPT=${2:-"Print a one-line hello and stop."}
MODE=${3:-bypass}

SPIKE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
SESSION=$(mktemp -d)                 # the mounted session dir (HOME + hook files)
mkdir -p "$SESSION/perm" "$SPIKE_DIR/results"

# Hook settings wiring the sh shims — Linux port of ClaudeHooks.RenderSettings.
# Paths are in-box (/in-box, /session); the host reads the resulting files off the mount.
cat > "$SESSION/settings.json" <<'JSON'
{
  "hooks": {
    "Stop": [{ "hooks": [{ "type": "command", "command": "sh /in-box/stop-hook.sh" }] }],
    "PreToolUse": [{ "matcher": "Bash|Write|Edit|MultiEdit",
                     "hooks": [{ "type": "command", "command": "sh /in-box/perm-hook.sh", "timeout": 660 }] }]
  }
}
JSON

# Subscription auth: resolve the OAuth token (from $CLAUDE_CODE_OAUTH_TOKEN or a host
# file $RA_OAUTH_TOKEN_FILE_HOST) into a 600 tmpfs file mounted read-only into the box.
# The driver fd-injects it to claude. We deliberately do NOT pass it as a -e env var
# (that would leak into every gated-tool hook child) and never set ANTHROPIC_API_KEY.
TOKDIR=$(mktemp -d); chmod 700 "$TOKDIR"; TOKFILE="$TOKDIR/oauth-token"
if [ -n "${CLAUDE_CODE_OAUTH_TOKEN:-}" ]; then
  printf '%s' "$CLAUDE_CODE_OAUTH_TOKEN" > "$TOKFILE"
elif [ -n "${RA_OAUTH_TOKEN_FILE_HOST:-}" ] && [ -f "$RA_OAUTH_TOKEN_FILE_HOST" ]; then
  cp "$RA_OAUTH_TOKEN_FILE_HOST" "$TOKFILE"
fi
chmod 600 "$TOKFILE" 2>/dev/null || true
if [ -s "$TOKFILE" ]; then
  TOKMOUNT="-v $TOKFILE:/run/oauth-token:ro"; TOKENV="-e RA_OAUTH_TOKEN_FILE=/run/oauth-token"
  echo "[host] subscription token resolved -> mounted ro at /run/oauth-token"
else
  TOKMOUNT=""; TOKENV=""
  echo "[host] WARN: no token (set CLAUDE_CODE_OAUTH_TOKEN or RA_OAUTH_TOKEN_FILE_HOST) — expect Authentication error"
fi

echo "[host] building image..."
docker build -t abox-agent-box -f "$SPIKE_DIR/image/Dockerfile" "$SPIKE_DIR"

echo "[host] running one real claude turn in the box..."
# Mount: project (live files) + session dir (HOME → JSONL, hook signal/perm) + token.
# No git remote, no API key crosses the boundary. Egress is a separate (gating) track.
docker run --rm \
  -v "$PROJECT:/project" \
  -v "$SESSION:/session" \
  $TOKMOUNT \
  -w /project \
  -e HOME=/session \
  -e RA_STOP_SIGNAL=/session/stop-signal.json \
  -e RA_PERM_DIR=/session/perm \
  -e RA_SETTINGS=/session/settings.json \
  -e RA_MODEL="${RA_MODEL:-}" \
  $TOKENV \
  abox-agent-box \
  -c 'python3 /in-box/drive-turn.py "$0" "$1"' "$PROMPT" "$MODE" \
  2>&1 | tee "$SPIKE_DIR/results/rung0.log"
rm -rf "$TOKDIR"

echo "[host] === read-back off the mount ==="
echo "[host] final message (Stop signal):"
cat "$SESSION/stop-signal.json" 2>/dev/null || echo "  (none — Stop hook did not fire)"
echo "[host] JSONL transcripts written in-box:"
find "$SESSION/.claude/projects" -name '*.jsonl' 2>/dev/null || echo "  (none found under the mounted HOME)"
echo "[host] session dir: $SESSION (inspect for B5 byte-faithfulness)"

#!/bin/sh
# sh port of ClaudeHooks' Stop shim (was pwsh on Windows). Reads the Stop payload
# on stdin and drops it at $RA_STOP_SIGNAL — on the mounted session dir, so the
# host reads the final message off the mount.
set -eu
cat > "$RA_STOP_SIGNAL"

#!/bin/sh
# In-container "agent turn" stand-in (Rung 0).
# A real agent CLI (claude/codex) would do this over a PTY; here we make the same
# observable change deterministically so U1/U2 are demonstrated, not asserted.
# Swapping in a real CLI is a credentials/network task the spike lists out of scope.
set -eu

TARGET="/project/Assets/Scripts/Bootstrap/Runtime/AgentSpike.cs"

echo "[agent] running as: $(id -un) ($(id -u))"
echo "[agent] project mount:"
ls -la /project/Assets/Scripts/Bootstrap/Runtime/ | head -5

echo "[agent] writing $TARGET"
cp /agent/AgentSpike.cs "$TARGET"

echo "[agent] sha256 in container:"
sha256sum "$TARGET"
echo "[agent] done"

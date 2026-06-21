set -u
echo "=== claude binary runs in the container? ==="
claude --version 2>&1 | head -2
echo "=== attempt a minimal real turn (auth test) ==="
echo "exit fd present? ls /proc/self/fd:"; ls /proc/self/fd 2>&1 | tr '\n' ' '; echo
timeout 60 claude -p "Reply with exactly: ok" 2>&1 | head -30
echo "[exit=$?]"

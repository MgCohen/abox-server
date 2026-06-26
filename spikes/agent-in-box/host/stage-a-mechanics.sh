#!/bin/sh
# Stage A: validate the container seam mechanics + the sh-shim/mount/pty plumbing
# WITHOUT invoking the model. Zero token cost. Proves everything the harness rests
# on except B1/B2 (a real claude turn + its billing path), which is Stage B.
set -eu

IMG=python:3.12-slim
SPIKE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PROJECT=$(mktemp -d); SESSION=$(mktemp -d); mkdir -p "$SESSION/perm"
echo "hello from host" > "$PROJECT/seed.txt"

docker pull -q "$IMG" >/dev/null

echo "=== M1: bind-mount round-trip byte-exact (host <-> container) ==="
docker run --rm -v "$PROJECT:/project" "$IMG" \
  sh -c 'echo "written in container" > /project/from-box.txt; sha256sum /project/from-box.txt'
echo "host sees:"; sha256sum "$PROJECT/from-box.txt"

echo "=== M2: hold-open across N execs over one box (the Testcontainers unknown) ==="
CID=$(docker run -d -v "$PROJECT:/project" "$IMG" sleep 300)
for n in 1 2 3 4 5; do
  docker exec "$CID" sh -c "echo step-$n >> /project/log.txt; tail -1 /project/log.txt"
done
echo "step 1's write visible in step 5? ->"; docker exec "$CID" sh -c 'head -1 /project/log.txt'
echo "=== M5: guaranteed teardown (anti-zombie) ==="
docker kill "$CID" >/dev/null; docker rm "$CID" >/dev/null 2>&1 || true
docker ps -q --filter "id=$CID" | grep -q . && echo "FAIL: still running" || echo "PASS: container gone"

echo "=== M3: isatty() true under a pty in the box (the billing precondition) ==="
docker run --rm "$IMG" python3 -c \
  'import pty,os,sys
pid,fd=pty.fork()
if pid==0:
    print("isatty(stdout)=",os.isatty(1)); sys.stdout.flush(); os._exit(0)
print("[host-side] child ran under a pty"); os.read(fd,4096); os.waitpid(pid,0)'

echo "=== M4: sh stop-hook writes the Stop payload across the mount ==="
docker run --rm -v "$SESSION:/session" -v "$SPIKE_DIR/in-box:/in-box:ro" \
  -e RA_STOP_SIGNAL=/session/stop-signal.json "$IMG" \
  sh -c 'echo "{\"last_assistant_message\":\"hi from the box\"}" | sh /in-box/stop-hook.sh'
echo "host reads signal off the mount:"; cat "$SESSION/stop-signal.json"

echo "=== M6: perm-hook req/resp handshake across the mount ==="
docker run -d --name permtest -v "$SESSION:/session" -v "$SPIKE_DIR/in-box:/in-box:ro" \
  -e RA_PERM_DIR=/session/perm "$IMG" \
  sh -c 'echo "{\"tool\":\"Bash\"}" | sh /in-box/perm-hook.sh > /session/perm-result.txt' >/dev/null
sleep 1
REQ=$(find "$SESSION/perm" -name 'req-*.json' | head -1)
echo "host sees request on the mount: $(basename "$REQ")"
ID=$(basename "$REQ" .json | sed 's/^req-//')
echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow"}}' > "$SESSION/perm/resp-$ID.json"
sleep 1; docker rm -f permtest >/dev/null 2>&1 || true
echo "hook echoed the host's response:"; cat "$SESSION/perm-result.txt"

echo "=== Stage A complete ==="

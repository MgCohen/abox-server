#!/bin/sh
# Egress rung: prove the box has NO direct route out and reaches ONLY the allowlisted
# host via a host-controlled proxy. Two docker networks: boxnet (--internal, no NAT) +
# outnet (has NAT). The proxy bridges both; the box sits on boxnet only. Pure docker —
# no in-container iptables. This is the allowlist form (the token-protecting posture).
set -u
IMG=python:3.12-slim
SPIKE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
LOG="$SPIKE_DIR/results/egress.log"
mkdir -p "$SPIKE_DIR/results"

cleanup() {
  docker rm -f egress-proxy >/dev/null 2>&1 || true
  docker network rm boxnet outnet >/dev/null 2>&1 || true
}
cleanup
docker pull -q "$IMG" >/dev/null

docker network create --internal boxnet >/dev/null
docker network create outnet >/dev/null

# Proxy: on boxnet (reachable by the box) AND outnet (can reach the internet).
docker run -d --name egress-proxy --network boxnet \
  -v "$SPIKE_DIR/egress:/egress:ro" -e ALLOW=api.anthropic.com \
  "$IMG" python3 /egress/allowlist-proxy.py >/dev/null
docker network connect outnet egress-proxy
sleep 1

# Box: boxnet ONLY — no direct internet. Egress only via the proxy.
docker run --rm --name egress-box --network boxnet \
  -v "$SPIKE_DIR/egress:/egress:ro" \
  -e HTTPS_PROXY=http://egress-proxy:8888 -e HTTP_PROXY=http://egress-proxy:8888 \
  -e PROXY_HOST=egress-proxy \
  "$IMG" python3 /egress/box-egress-tests.py > "$LOG" 2>&1
RC=$?

echo "=== Egress matrix (allowlist form) ===" | tee /tmp/egress-head
cat "$LOG"
echo "--- proxy decisions ---"; docker logs egress-proxy 2>&1 | grep -E 'ALLOW|DENY|listening' || true
{ echo "--- proxy decisions ---"; docker logs egress-proxy 2>&1 | grep -E 'ALLOW|DENY|listening'; } >> "$LOG" 2>&1

cleanup
echo "[exit=$RC]"; exit $RC

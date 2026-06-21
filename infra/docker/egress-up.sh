#!/bin/sh
# Bring up the egress sidecar the agent boxes depend on (ADR 0013). Idempotent: run
# once on the orchestrator host before serving turns. Creates two docker networks —
# `abox-boxnet` (--internal, no route out: where the boxes live) and `abox-outnet`
# (has NAT) — and the allowlist proxy bridging both. Per-turn boxes attach to
# abox-boxnet only and set HTTPS_PROXY at the proxy, so their sole egress is the
# allowlisted host(s). Configure the orchestrator with:
#   SandboxSettings.Network  = abox-boxnet
#   SandboxSettings.ProxyUrl = http://abox-egress-proxy:8888
set -eu

ALLOW="${ALLOW:-api.anthropic.com}"
BOXNET=abox-boxnet
OUTNET=abox-outnet
PROXY=abox-egress-proxy
IMAGE="${IMAGE:-abox-egress-proxy:latest}"

docker network inspect "$BOXNET" >/dev/null 2>&1 || docker network create --internal "$BOXNET" >/dev/null
docker network inspect "$OUTNET" >/dev/null 2>&1 || docker network create "$OUTNET" >/dev/null

# `docker container inspect`, not `docker inspect`: the latter also resolves the
# like-named image, so it would always report the proxy as already up and skip the run.
if ! docker container inspect "$PROXY" >/dev/null 2>&1; then
  docker run -d --name "$PROXY" --restart unless-stopped --network "$BOXNET" \
    -e ALLOW="$ALLOW" "$IMAGE" >/dev/null
  docker network connect "$OUTNET" "$PROXY"
fi

echo "egress sidecar up: boxnet=$BOXNET proxy=$PROXY allow=$ALLOW"

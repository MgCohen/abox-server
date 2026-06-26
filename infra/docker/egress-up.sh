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

# Both agent backends: api.anthropic.com (claude subscription) + chatgpt.com (codex
# subscription, backend-api/codex/responses). codex also probes files.openai.com and
# ab.chatgpt.com; those stay denied (uploads/analytics) and the turn still completes.
ALLOW="${ALLOW:-api.anthropic.com,chatgpt.com}"
BOXNET=abox-boxnet
OUTNET=abox-outnet
PROXY=abox-egress-proxy
IMAGE="${IMAGE:-abox-egress-proxy:latest}"

docker network inspect "$BOXNET" >/dev/null 2>&1 || docker network create --internal "$BOXNET" >/dev/null
docker network inspect "$OUTNET" >/dev/null 2>&1 || docker network create "$OUTNET" >/dev/null

# Heal partial bring-ups: recreate unless the proxy is actually *running* (a created/
# exited/dead container would otherwise be treated as up). `docker container inspect`,
# not `docker inspect` — the latter also resolves the like-named image.
if [ "$(docker container inspect -f '{{.State.Running}}' "$PROXY" 2>/dev/null)" != "true" ]; then
  docker rm -f "$PROXY" >/dev/null 2>&1 || true
  docker run -d --name "$PROXY" --restart unless-stopped --network "$BOXNET" \
    -e ALLOW="$ALLOW" "$IMAGE" >/dev/null
fi
# Idempotent: attach to outnet even if the proxy already existed without it.
docker network connect "$OUTNET" "$PROXY" 2>/dev/null || true

echo "egress sidecar up: boxnet=$BOXNET proxy=$PROXY allow=$ALLOW"

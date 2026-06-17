#!/usr/bin/env bash
# Rung 2 — add namespace isolation (the egress hole closes).
#   same as rung 1, plus the agent runs inside net + pid + mount namespaces
#   (unshare): no route to anything, host processes invisible. This is the
#   container guarantee via the same kernel primitive, without a daemon — the
#   Docker `--network none` stand-in for this environment. Run as root.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/control-plane.sh"
cp_run_rung 2

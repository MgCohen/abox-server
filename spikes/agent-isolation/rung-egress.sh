#!/usr/bin/env bash
# Egress rung — allowlist egress (the config A.Box actually ships).
#   rung 2 proved `--net none` (deny ALL). Production must reach the LLM API while
#   denying everything else. This rung keeps pid+mount isolation but stays on the
#   host net under a per-UID netfilter allowlist: the worker may reach exactly ONE
#   endpoint; metadata (SSRF->creds), arbitrary hosts, loopback proxies, and DNS
#   exfil are all REJECTed. The ambient container leaves metadata + loopback open
#   (see the PCE rows) — closing them is exactly this rung's job. Run as root.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/control-plane.sh"
cp_run_rung egress

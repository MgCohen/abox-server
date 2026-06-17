#!/usr/bin/env bash
# Rung 1 — two principals on one host (proves the PRINCIPLE).
#   control plane = root (trusted) · agent = a separate OS user (untrusted)
#   the agent gets a scrubbed+allowlisted env and only the working dir.
# No egress control yet, so A3/A5 are expected to reach — that gap is what
# rung 2 closes. Run as root.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/control-plane.sh"
cp_run_rung 1

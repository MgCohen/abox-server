#!/usr/bin/env bash
# The untrusted "agent". Stands in for claude/codex — but deliberately
# adversarial: we WANT it to attempt every attack, not hope an LLM does.
#
# Runs as the worker principal, in a scrubbed environment, with only the working
# directory as its world. It does one line of honest work, then attacks.

set -uo pipefail

# --- honest work: the diff the control plane will commit ---
printf 'agent (rung%s) was here\n' "${RUNG:-?}" >> "${WORKDIR:?}/hello.txt"

# --- attacks: each prints one "Ax|desc|RESULT|detail" line ---
for a in a1_env_token a2_secret_at_rest a3_egress a4_git_push a5_exfil a6_callback; do
  bash "${ATTACKS_DIR:?}/${a}.sh"
done

#!/usr/bin/env bash
# Phase 2 proof: the judge bundle is a portable, self-contained unit. Run by hand:
#   bash spikes/judge-packaging/verify.sh
# Exits 0 only if the CORE (agent + workflow) lifts with zero host coupling and its
# contract is intact. Adapter coupling is reported, never failed — adapters are the
# per-host re-authoring surface, so carrying host tokens is expected of them.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
bundle="$here/bundle/.claude"
core=("$bundle/agents/judge.md" "$bundle/workflows/judge.js")
adapters=("$bundle/commands/judge.md" "$bundle/commands/judge-rulebook.md" \
          "$bundle/commands/judge-authoring.md" "$bundle/skills/test-rulebook/SKILL.md")
# Tokens that would betray coupling to THIS repo. A core file with any of these did not lift cleanly.
host_re='ABox|abox-server|tests/Central|ABox\.slnx|/home/user'

fail() { echo "FAIL: $1"; exit 1; }

echo "== 1. every bundle file present =="
for f in "${core[@]}" "${adapters[@]}"; do
  [ -f "$f" ] || fail "missing $f"
  echo "  ok  ${f#"$bundle"/}"
done

echo "== 2. core carries ZERO host coupling (the portability proof) =="
for f in "${core[@]}"; do
  if grep -nE "$host_re" "$f" >/dev/null; then
    grep -nE "$host_re" "$f"
    fail "core file ${f#"$bundle"/} is coupled to this repo — it cannot lift verbatim"
  fi
  echo "  ok  ${f#"$bundle"/} — no host tokens"
done

echo "== 3. workflow parses and still exports the full contract =="
node --check "$bundle/workflows/judge.js" || fail "judge.js is not valid JS"
for token in "name: 'judge'" subject context criteria generalFeedback results criterionId status evidence; do
  grep -qF "$token" "$bundle/workflows/judge.js" || fail "judge.js lost contract token: $token"
done
echo "  ok  meta + request {subject,context,criteria} + response {generalFeedback,results{criterionId,status,evidence}}"

echo "== 4. adapters — the per-host re-authoring surface (informational) =="
for f in "${adapters[@]}"; do
  n=$(grep -cE "$host_re" "$f" || true)
  echo "  ${f#"$bundle"/}: $n host-coupled line(s) to re-point at the new home"
done

echo
echo "PASS — judge core is portable verbatim; adapters isolated as the parameterization seam."

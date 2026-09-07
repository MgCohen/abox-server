#!/usr/bin/env bash
# Phase 5, layer 3: the harness-free enforcement net. This is what the repo's `Docs` test does — discover every
# document instance and fail the build if any drifts — but with ZERO xUnit / ParityGuard. It shells the engine
# (ADR 0015 process boundary), so any CI step or git hook can be this gate. Run by hand:
#   bash spikes/doc-engine-authoring/gate.sh
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
engine="$here/../doc-engine-extraction/Engine"
catalog="$here/catalog"

eng() { dotnet run --project "$engine" --no-build -- "$@" --root "$catalog"; }

# An instance = a *.md whose FIRST line is `---` and whose front-matter block carries `docType:`. This mirrors
# DocInstances.Discover() (leading docType front matter), so a plain README with no front matter is skipped.
discover() {
  find "$1" -name '*.md' | sort | while read -r f; do
    [ "$(head -1 "$f")" = "---" ] || continue
    head -40 "$f" | sed -n '2,/^---$/p' | grep -q '^docType:' && echo "$f"
  done
}

# The gate: catalog self-consistent, then EVERY discovered instance validates. Non-zero if any fails.
gate() {
  local corpus="$1" fails=0 n=0 f
  if ! eng check >/dev/null; then echo "  catalog is not self-consistent (docengine check failed)"; return 1; fi
  while read -r f; do
    [ -z "$f" ] && continue
    n=$((n + 1))
    if eng validate "$f" >/dev/null 2>&1; then echo "  ok    ${f#"$corpus"/}"
    else echo "  FAIL  ${f#"$corpus"/}"; fails=$((fails + 1)); fi
  done < <(discover "$corpus")
  echo "  discovered $n instance(s), $fails non-conformant"
  [ "$fails" -eq 0 ]
}

echo "== build the engine once =="
dotnet build "$engine" -c Debug -v quiet --nologo >/dev/null

corpus="$(mktemp -d)"; trap 'rm -rf "$corpus"' EXIT

# A non-instance (no front matter) — the gate must SKIP it, not choke on it.
cat > "$corpus/README.md" <<'EOF'
# Team docs
Just notes. Not a doc-engine instance — no front matter, so the gate ignores it.
EOF

good() {
cat > "$corpus/$1" <<EOF
---
docType: brief
status: draft
---

## Summary

$2

## Findings
### A conformant finding
impact: medium

Every required block is present, so the gate accepts this instance.
EOF
}
good "alpha.brief.md" "Alpha brief — a conformant instance in the corpus."
good "beta.brief.md"  "Beta brief — a second conformant instance in the corpus."

echo "== 1. clean corpus → the gate passes (skips the README, validates both briefs) =="
if gate "$corpus"; then echo "  PASS (exit 0)"; else echo "  UNEXPECTED FAIL on a clean corpus"; exit 1; fi

# Now let a malformed instance drift into the corpus — missing the required `finding` block.
cat > "$corpus/gamma.brief.md" <<'EOF'
---
docType: brief
status: draft
---

## Summary

Gamma brief — drifted: it has no findings, which brief requires.
EOF

echo "== 2. one instance drifts → the gate fails and names it (this is the CI net) =="
if gate "$corpus"; then echo "  UNEXPECTED PASS — a non-conformant instance slipped the gate"; exit 1; else echo "  correctly non-zero — the gate has teeth"; fi

echo
echo "PASS — a harness-free driver discovers every instance against the editable catalog and blocks on any drift."

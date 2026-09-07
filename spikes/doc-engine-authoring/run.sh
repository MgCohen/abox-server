#!/usr/bin/env bash
# Phase 5 proof: the AUTHORING loop travels. Against an editable, repo-local catalog (--root), a new home adds a
# new block and a new doctype (data only, no engine change), then authors and validates an instance of it. Uses
# the already-proven engine from the Phase 1 spike — same engine, new editable catalog. Run by hand:
#   bash spikes/doc-engine-authoring/run.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
engine="$here/../doc-engine-extraction/Engine"     # the proven engine; the catalog is what changes
catalog="$here/catalog"                              # editable, checked-in vocabulary
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT

eng() { dotnet run --project "$engine" --no-build -- "$@" --root "$catalog"; }

echo "== build the engine once =="
dotnet build "$engine" -c Debug -v quiet --nologo >/dev/null

echo "== 1. the editable catalog is self-consistent, INCLUDING the new block + doctype =="
eng check

echo "== 2. discovery — the new 'brief' doctype and 'finding' block show up in the catalog =="
eng catalog | grep -E 'brief|finding' || { echo "FAIL: new vocabulary not discoverable"; exit 1; }
echo "   (brief blocks:)"; eng catalog brief

cat > "$work/good.brief.md" <<'EOF'
---
docType: brief
status: draft
---

## Summary

The extracted engine authors brand-new document types from an editable, repo-local catalog.

## Findings
### The catalog can live in the consuming repo
impact: high

Pointing `--root` at checked-in YAML lets a new home add blocks and doctypes with no engine change.

### The new doctype validates its own instances
impact: medium

`brief` did not exist in the engine's home catalog; it was authored here as data and enforces immediately.

## Open Questions
### Should the baked-in catalog seed the editable one on init?
lean: yes — copy on first init, then let the home diverge.

How a new home bootstraps its catalog from the tool's built-in copy.
EOF

# Missing the required `finding` block — the validator must reject it.
cat > "$work/bad.brief.md" <<'EOF'
---
docType: brief
status: draft
---

## Summary

Only a summary — the required findings are absent.
EOF

echo "== 3. author an INSTANCE of the new doctype → validate (expect PASS) =="
eng validate "$work/good.brief.md"

echo "== 4. a non-conforming instance is rejected (expect non-zero) =="
if eng validate "$work/bad.brief.md"; then
  echo "FAIL: a brief with no findings was accepted — the new doctype has no teeth"; exit 1
else
  echo "   ok — rejected (missing required 'finding')"
fi

echo
echo "PASS — new block + new doctype authored as data against an editable catalog; instances enforced by the same engine."

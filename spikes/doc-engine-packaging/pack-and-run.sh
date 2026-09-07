#!/usr/bin/env bash
# Phase 3 proof: the doc-engine packs as a `dotnet tool`, installs, and enforces documents from an INSTALLED
# copy with no repo checkout nearby — the catalog rides inside the tool. Run by hand:
#   bash spikes/doc-engine-packaging/pack-and-run.sh
# All build/install/scratch output goes to a temp dir, so the repo tree stays clean (nothing to gitignore).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
nupkg="$work/nupkg"
toolpath="$work/.tool"
clean="$work/elsewhere"          # a dir with NO catalog on its walk-up path
mkdir -p "$nupkg" "$toolpath" "$clean"

echo "== 1. pack the engine as a dotnet tool =="
dotnet pack "$here/Engine/ABox.DocEngine.csproj" -c Release -p:PackOutDir="$nupkg" -v quiet --nologo
ls "$nupkg"/*.nupkg
echo "   catalog packed into the tool?"
unzip -l "$nupkg"/ABox.DocEngine.Tool.*.nupkg | grep -E '_schema|doctypes/research' | head

echo "== 2. install it to a private tool-path =="
dotnet tool install ABox.DocEngine.Tool --version 0.1.0 --add-source "$nupkg" --tool-path "$toolpath" >/dev/null
docengine="$toolpath/docengine"
"$docengine" --version >/dev/null 2>&1 || true
echo "   installed: $docengine"

echo "== 3. run the INSTALLED tool from a dir with no catalog (BaseDirectory fallback) =="
cd "$clean"
echo "   -> docengine check"
"$docengine" check

cat > "$clean/good.research.md" <<'EOF'
---
docType: research
status: draft
---

## Summary

A minimal research instance that exercises the installed engine's structural gate.

## Questions
### Does the installed tool validate a conforming instance?
It must accept a document that carries every required block.

## Outcome

Yes — the required summary, question, and outcome blocks are present, so it conforms.
EOF

cat > "$clean/bad.research.md" <<'EOF'
---
docType: research
status: draft
---

## Summary

Only a summary — the required question and outcome blocks are absent.
EOF

echo "   -> docengine validate good.research.md (expect PASS)"
"$docengine" validate "$clean/good.research.md"

echo "   -> docengine validate bad.research.md (expect non-zero)"
if "$docengine" validate "$clean/bad.research.md"; then
  echo "FAIL: a non-conforming instance was accepted — the gate has no teeth"; exit 1
else
  echo "   ok — rejected as expected (exit $?)"
fi

echo "== 4. soft-wiring: a consumer uses the tool only if it resolves =="
export PATH="$toolpath:$PATH"
if command -v docengine >/dev/null 2>&1; then
  echo "   docengine present -> hard document check ON: $(docengine check | tail -1)"
else
  echo "   docengine absent  -> consumer degrades to structural-only, no hard fail"
fi

echo
echo "PASS — engine packs, installs, and enforces documents as a standalone tool; catalog travels inside it."

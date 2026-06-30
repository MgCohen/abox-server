#!/bin/sh
# Stop hook: structurally validate any guide instance touched this session, linter-style.
#
# Deterministic only — runs `docengine validate` (structure), never a guide's prose. The agent
# walkthrough is on-demand (`/walk-guide`), not wired here. A guide with a broken structure blocks
# the turn from ending (exit 2) with the violations as feedback, so the agent fixes it; the
# `stop_hook_active` guard breaks any re-block loop. Fires real work only when a guide changed.
#
# Opt-in (per-user): `.claude/settings.json` is gitignored, so wire this in YOUR
# `.claude/settings.local.json`:
#   { "hooks": { "Stop": [ { "hooks": [
#       { "type": "command", "command": "sh \"$CLAUDE_PROJECT_DIR/.claude/hooks/validate-guides.sh\"" }
#   ] } ] } }
set -eu

payload=$(cat 2>/dev/null || true)
case "$payload" in *'"stop_hook_active":true'*) exit 0 ;; esac

root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root"

changed=$( { git diff --name-only HEAD -- '*.md' 2>/dev/null; \
             git ls-files --others --exclude-standard -- '*.md' 2>/dev/null; } | sort -u )
[ -n "$changed" ] || exit 0

failures=""
for f in $changed; do
    [ -f "$f" ] || continue
    head -n 20 "$f" | grep -q '^docType: guide[[:space:]]*$' || continue
    if ! out=$(dotnet run --project tools/doc-engine -- validate "$f" --root tools/doc-engine 2>&1); then
        failures="$failures
$out"
    fi
done

[ -n "$failures" ] || exit 0
printf 'Changed guide(s) failed structural validation — fix before finishing:%s\n' "$failures" >&2
exit 2

#!/bin/sh
# Repo-hooks consumer: validate every doc-engine instance that changed since we last ran and
# dispatch its onChange handler. Triggered by tools/doc-engine/on-doc-change.hook on TurnEnded /
# CommitLanded — the repo-hooks controller owns the trigger; this script is pure doc-engine and
# carries no knowledge of Claude Code or any provider. The repo-hooks event arrives on stdin and is
# not needed (we recompute the changed set from git), so it is ignored.
#
# This is a `mode: check` handler: its output is fed back to the running agent, so it writes its
# findings to stdout. Two jobs:
#   1. docengine validate each changed instance — an invalid doc exits non-zero to BLOCK the
#      turn-end, feeding the validation error back so the agent fixes it before stopping.
#   2. onChange dispatch — run it when it is a deterministic script; an agent handler stays
#      on-demand (`/walk-guide <doc>`), never auto-spawned from here.
# CI's Docs test remains the hard backstop; this is the fast, in-loop nudge.
set -eu

root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root"
engine="tools/doc-engine"
marker="$(git rev-parse --git-dir)/abox-doc-onchange-state"

# Resolve a fast `docengine` invocation: a prebuilt dll skips the per-call restore+build that
# `dotnet run` pays every time. Fall back to `dotnet run` when nothing is built yet.
dll=$(ls "artifacts/bin/ABox.DocEngine"/*/docengine.dll 2>/dev/null | head -n 1 || true)
if [ -n "$dll" ]; then
    de() { dotnet "$dll" "$@"; }
else
    de() { dotnet run --project "$engine" -- "$@"; }
fi

# Track a last-handled SHA (committed OR uncommitted): the cloud flow commits mid-session, so an
# uncommitted-only diff would miss freshly-committed docs.
head=$(git rev-parse HEAD 2>/dev/null || true)
last=$(cat "$marker" 2>/dev/null || true)
if [ -n "$last" ] && git cat-file -e "$last" 2>/dev/null; then
    since="$last"
else
    default=$(git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>/dev/null || echo origin/main)
    since=$(git merge-base "$default" HEAD 2>/dev/null || true)
fi

changed=$(
    {
        [ -n "$since" ] && [ -n "$head" ] && git diff --name-only "$since" "$head" -- '*.md' 2>/dev/null
        git diff --name-only HEAD -- '*.md' 2>/dev/null
        git ls-files --others --exclude-standard -- '*.md' 2>/dev/null
    } | sort -u | sed '/^$/d'
)
[ -n "$head" ] && printf '%s' "$head" > "$marker" 2>/dev/null || true
[ -n "$changed" ] || exit 0

notes=""
invalid=0
for f in $changed; do
    [ -f "$f" ] || continue
    head -n 1 "$f" | grep -q '^---$' || continue
    head -n 20 "$f" | grep -q '^docType:' || continue

    if ! out=$(de validate "$f" --root "$engine" 2>&1); then
        invalid=1
        notes="$notes
[invalid] $f
$out"
        continue
    fi

    handler=$(de onchange "$f" --root "$engine" 2>/dev/null || true)
    [ -n "$handler" ] || continue
    case "$handler" in
        scripts/* | *.sh)
            if sh "$handler" "$f" >/dev/null 2>&1; then
                notes="$notes
[onChange ran] $f -> $handler"
            else
                notes="$notes
[onChange FAILED] $f -> $handler"
            fi
            ;;
        *)
            notes="$notes
[onChange on-demand] $f -> $handler (e.g. /walk-guide $f)"
            ;;
    esac
done

[ -n "$notes" ] || exit 0
printf 'doc-engine — changed instances:%s\n' "$notes"

# An invalid doc blocks the turn-end (exit non-zero) so the agent fixes it before stopping; valid
# changes with notes are advisory (exit 0). The producer's stop_hook_active guard breaks any loop.
[ "$invalid" -eq 0 ] || exit 2
exit 0

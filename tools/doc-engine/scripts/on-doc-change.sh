#!/bin/sh
# Repo-hooks consumer: validate every doc-engine instance that changed since we last ran and
# dispatch its onChange handler. Triggered by tools/doc-engine/on-doc-change.hook on TurnEnded /
# CommitLanded — the repo-hooks controller owns the trigger; this script is pure doc-engine and
# carries no knowledge of Claude Code or any provider. The repo-hooks event arrives on stdin and is
# not needed (we recompute the changed set from git), so it is ignored.
#
# Two jobs, both NON-BLOCKING (surface only; CI's Docs test is the hard backstop):
#   1. docengine check + docengine validate on each changed instance.
#   2. onChange dispatch — run it when it is a deterministic script; an agent handler stays
#      on-demand (`/walk-guide <doc>`), never auto-spawned from a hook (cost / headless / loops).
set -eu

root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root"
engine="tools/doc-engine"
marker="$(git rev-parse --git-dir)/abox-doc-onchange-state"

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
for f in $changed; do
    [ -f "$f" ] || continue
    head -n 1 "$f" | grep -q '^---$' || continue
    head -n 20 "$f" | grep -q '^docType:' || continue

    if ! out=$(dotnet run --project "$engine" -- validate "$f" --root "$engine" 2>&1); then
        notes="$notes
[invalid] $f
$out"
        continue
    fi

    handler=$(dotnet run --project "$engine" -- onchange "$f" --root "$engine" 2>/dev/null || true)
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
printf 'doc-engine — changed instances:%s\n' "$notes" >&2
exit 0

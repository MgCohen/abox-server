#!/bin/sh
# Repo-hooks consumer: on a doc change, validate the instances that changed and — on a turn end —
# spawn fresh reviewers whose feedback returns to the main session. Triggered by
# tools/doc-engine/on-doc-change.hook (TurnEnded / CommitLanded). The repo-hooks controller owns the
# trigger; this script is pure doc-engine and carries no knowledge of any provider.
#
# This is a `mode: check` handler — its output is fed back to the running agent. The pipeline:
#   1. docengine validate  — deterministic, free, shape-only. An invalid doc exits non-zero to BLOCK
#      the turn-end, feeding the structural error back so the agent fixes it before stopping.
#   2. onChange script      — a deterministic side-effect handler, if the doc declares one.
#   3. fresh reviewers      — one per docType `reviewers:` entry (judge always; guide adds walk-guide),
#      spawned as a brand-new `claude -p --agent <name>` so the main session never grades its own work
#      (no bias, no context bloat). Launched HOOK-FREE (ABOX_HOOKS_SUPPRESS) so a reviewer can't
#      re-trigger this hook. Reviewers are ADVISORY (their notes ride back as context); only the
#      deterministic validate blocks. Reviewers run on a turn end only — a commit has no agent to feed.
# CI's Docs test remains the hard backstop; this is the fast, in-loop nudge.
set -eu

root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root"
engine="tools/doc-engine"
marker="$(git rev-parse --git-dir)/abox-doc-onchange-state"

# The repo-hooks event arrives on stdin; we only need its kind (spawn LLM reviewers on a turn end, not
# on every commit). Recompute the changed set from git, so the rest of the payload is unused.
if [ -t 0 ]; then event=""; else event=$(cat 2>/dev/null || true); fi
kind=$(printf '%s' "$event" | sed -n 's/.*"kind":"\([^"]*\)".*/\1/p' | head -n 1)

# Resolve a fast `docengine`: a prebuilt dll skips the per-call restore+build `dotnet run` pays.
dll=$(ls "artifacts/bin/ABox.DocEngine"/*/docengine.dll 2>/dev/null | head -n 1 || true)
if [ -n "$dll" ]; then de() { dotnet "$dll" "$@"; }; else de() { dotnet run --project "$engine" -- "$@"; }; fi

# The reviewer spawner is overridable so the pipeline is testable without a live agent.
review_cmd="${DOCENGINE_REVIEWER_CMD:-claude}"

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

    # 1. Deterministic gate: an invalid doc (generic structure) blocks the turn-end.
    if ! out=$(de validate "$f" --root "$engine" 2>&1); then
        invalid=1
        notes="$notes
[invalid] $f
$out"
        continue
    fi

    # 2. Custom deterministic checks (per docType) — cheap, objective, and also blocking. Each is an
    #    engine-relative script that gets the doc and exits non-zero with a message to block.
    for c in $(de checks "$f" --root "$engine" 2>/dev/null); do
        if ! cout=$(sh "$engine/$c" "$f" 2>&1); then
            invalid=1
            notes="$notes
[check failed] $f -> $c
$cout"
        fi
    done

    # 3. Deterministic onChange script side-effect, if the doc declares one.
    handler=$(de onchange "$f" --root "$engine" 2>/dev/null || true)
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
    esac

    # 4. Fresh reviewers (advisory) — turn end only, and only when a spawner is on PATH.
    [ "$kind" = "TurnEnded" ] || continue
    command -v "$review_cmd" >/dev/null 2>&1 || continue
    dt=$(sed -n 's/^docType:[[:space:]]*//p' "$f" | head -n 1)
    for r in $(de reviewers "$f" --root "$engine" 2>/dev/null); do
        if [ "$r" = "judge" ]; then
            task="Grade the document '$f' (docType: $dt) against this rubric. Give a terse per-criterion verdict and concrete fixes.

Rubric:
$(de rubric "$f" --root "$engine" 2>/dev/null)"
        else
            task="Review the document '$f' (docType: $dt) as a fresh reader. Report a terse verdict and concrete, actionable fixes."
        fi
        rout=$(ABOX_HOOKS_SUPPRESS=1 "$review_cmd" -p --agent "$r" "$task" </dev/null 2>&1 || true)
        [ -n "$rout" ] && notes="$notes
[review:$r] $f
$rout"
    done
done

[ -n "$notes" ] || exit 0
printf 'doc-engine — changed instances:%s\n' "$notes"

# Deterministic invalidity blocks the turn (exit non-zero); reviewers only advise. The producer's
# stop_hook_active guard breaks any block loop.
[ "$invalid" -eq 0 ] || exit 2
exit 0

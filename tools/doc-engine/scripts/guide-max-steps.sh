#!/bin/sh
# Deterministic docType check (guide): a guide is "independent actions, each with a few steps" — past
# some point it has lost that shape and should be split. This is the kind of cheap, objective, per-type
# rule the generic structural validator can't express and an agent reviewer is overkill for.
# Referenced from doctypes/guide.yaml `checks:`. Receives the doc path; exit non-zero to block the turn.
# MAX is a guardrail starting value — tune it in review.
set -eu
MAX=${GUIDE_MAX_STEPS:-100}
doc=$1
steps=$(grep -c '<!-- id:' "$doc" 2>/dev/null || echo 0)
if [ "$steps" -gt "$MAX" ]; then
    echo "guide has $steps steps (max $MAX) — split it into separate guides"
    exit 1
fi

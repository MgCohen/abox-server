#!/bin/sh
# Canonical protected-path checker. Single source of enforcement logic, shared by
# the git hooks, the CI policy-guard job, and the Claude PreToolUse guard.
#
# Usage: protected-paths-check.sh <path> [<path> ...]
#        printf '%s\n' file1 file2 | protected-paths-check.sh
#
# Gate mode (default): exit 0 if no path is protected (or ABOX_ALLOW_PROTECTED=1);
# exit 1 otherwise, listing each offending path with its owner and reason on stderr.
#
# List mode (--tier <name>): print, one per line, each input path whose matched
# rule is in tier <name>; always exit 0. Non-gating — used by the notifier.
set -eu

root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
policy="$root/governance/protected-paths"

if [ ! -f "$policy" ]; then
  echo "protected-paths-check: policy file not found at $policy" >&2
  exit 2
fi

tier_filter=
if [ "${1:-}" = "--tier" ]; then
  tier_filter=${2:-}
  [ -n "$tier_filter" ] || { echo "protected-paths-check: --tier needs a value" >&2; exit 2; }
  shift 2
fi

trim() { printf '%s' "$1" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//'; }

glob_to_regex() {
  printf '%s' "$1" | sed \
    -e 's/[].^$+(){}|[]/\\&/g' \
    -e 's/\*\*/@@GLOBSTAR@@/g' \
    -e 's#\*#[^/]*#g' \
    -e 's/@@GLOBSTAR@@/.*/g' \
    -e 's#?#[^/]#g'
}

rules=$(grep -v -e '^[[:space:]]*#' -e '^[[:space:]]*$' "$policy" || true)

if [ -n "${1:-}" ]; then
  paths=$(printf '%s\n' "$@")
else
  paths=$(cat)
fi

# Disable pathname expansion: paths are data, never globs to expand here.
set -f

match_one() {
  _path=$1
  printf '%s\n' "$rules" | while IFS='|' read -r _glob _owner _tier _reason; do
    _glob=$(trim "$_glob")
    [ -z "$_glob" ] && continue
    _re=$(glob_to_regex "$_glob")
    if printf '%s' "$_path" | grep -Eq "^${_re}$"; then
      printf '%s\t%s\t%s\t%s\n' "$_path" "$(trim "$_owner")" "$(trim "$_tier")" "$(trim "$_reason")"
      break
    fi
  done
}

# List mode: emit paths whose matched rule is in the requested tier; never gate.
if [ -n "$tier_filter" ]; then
  while IFS= read -r p; do
    [ -z "$p" ] && continue
    hit=$(match_one "$p")
    [ -n "$hit" ] || continue
    htier=$(printf '%s' "$hit" | cut -f3)
    if [ "$htier" = "$tier_filter" ]; then printf '%s\n' "$p"; fi
  done <<EOF
$paths
EOF
  exit 0
fi

found=""
while IFS= read -r p; do
  [ -z "$p" ] && continue
  hit=$(match_one "$p")
  if [ -n "$hit" ]; then
    found="${found}${hit}
"
  fi
done <<EOF
$paths
EOF

[ -z "$found" ] && exit 0

echo "Protected-path policy violations:" >&2
printf '%s' "$found" | while IFS="$(printf '\t')" read -r p o t r; do
  [ -z "$p" ] && continue
  printf '  - %s  (owner %s) — %s\n' "$p" "$o" "$r" >&2
done

if [ "${ABOX_ALLOW_PROTECTED:-}" = "1" ]; then
  echo "ABOX_ALLOW_PROTECTED=1 set — allowing despite protected paths (logged)." >&2
  exit 0
fi

echo "These paths are protected. Change them via a reviewed PR (owners in CODEOWNERS)," >&2
echo "or set ABOX_ALLOW_PROTECTED=1 for a deliberate local override. CI re-checks regardless." >&2
exit 1

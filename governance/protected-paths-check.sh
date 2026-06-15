#!/bin/sh
# Canonical protected-path checker. Single source of enforcement logic, shared by
# the git hooks, the CI policy-guard job, and the Claude PreToolUse guard.
#
# Usage: protected-paths-check.sh <path> [<path> ...]
#        printf '%s\n' file1 file2 | protected-paths-check.sh
#
# Exit 0 if no path is protected (or ABOX_ALLOW_PROTECTED=1). Exit 1 otherwise,
# listing each offending path with its owner and reason on stderr.
set -eu

root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
policy="$root/governance/protected-paths"

if [ ! -f "$policy" ]; then
  echo "protected-paths-check: policy file not found at $policy" >&2
  exit 2
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

match_one() {
  _path=$1
  printf '%s\n' "$rules" | while IFS='|' read -r _glob _owner _reason; do
    _glob=$(trim "$_glob")
    [ -z "$_glob" ] && continue
    _re=$(glob_to_regex "$_glob")
    if printf '%s' "$_path" | grep -Eq "^${_re}$"; then
      printf '%s\t%s\t%s\n' "$_path" "$(trim "$_owner")" "$(trim "$_reason")"
      break
    fi
  done
}

found=""
for p in $paths; do
  [ -z "$p" ] && continue
  hit=$(match_one "$p")
  if [ -n "$hit" ]; then
    found="${found}${hit}
"
  fi
done

[ -z "$found" ] && exit 0

echo "Protected-path policy violations:" >&2
printf '%s' "$found" | while IFS="$(printf '\t')" read -r p o r; do
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

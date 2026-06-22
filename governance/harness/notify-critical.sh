#!/bin/sh
# Critical-path notifier (ADR 0012: fail-safe convenience — may use a dependency,
# never gates). Reads changed files, asks the canonical checker which are tier
# `critical`, and if any, fires every channel in governance/harness/notify.yml via Apprise.
# Always exits 0: a missed alert must never fail CI or block a merge.
set -eu

root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
checker="$root/governance/harness/protected-paths-check.sh"
cfg="$root/governance/harness/notify.yml"

if [ -n "${1:-}" ]; then changed=$(printf '%s\n' "$@"); else changed=$(cat); fi
[ -n "$changed" ] || exit 0

hits=$(printf '%s\n' "$changed" | "$checker" --tier critical || true)
[ -n "$hits" ] || { echo "notify: no critical-path changes."; exit 0; }

echo "notify: critical-path changes detected:"
printf '%s\n' "$hits" | sed 's/^/  - /'

if ! command -v apprise >/dev/null 2>&1; then
  echo "notify: apprise not installed; skipping delivery (alert is non-blocking)." >&2
  exit 0
fi
[ -f "$cfg" ] || { echo "notify: $cfg missing; skipping delivery." >&2; exit 0; }

title="⚠️ critical-path change in ${GITHUB_REPOSITORY:-this repo}"
body="Critical files changed${PR_URL:+ ($PR_URL)}:
$(printf '%s\n' "$hits" | sed 's/^/- /')"

# Render ${VAR} secrets from the env into a .yml temp file. The .yml extension
# matters: apprise picks its config parser from the file extension, and an
# extension-less temp file is misread as TEXT instead of YAML.
tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT
rendered="$tmpdir/notify.yml"
if command -v envsubst >/dev/null 2>&1; then
  # Expand only the ${VAR} placeholders the config actually declares, so a literal
  # '$' elsewhere in a channel URL survives — and the dispatcher stays
  # channel-agnostic (it never hardcodes a specific channel's variable names).
  vars=$(grep -o '${[A-Za-z_][A-Za-z0-9_]*}' "$cfg" | sort -u | tr '\n' ' ')
  envsubst "$vars" < "$cfg" > "$rendered"
else
  cp "$cfg" "$rendered"
fi

apprise -c "$rendered" -t "$title" -b "$body" || echo "notify: apprise delivery failed (non-blocking)." >&2
exit 0

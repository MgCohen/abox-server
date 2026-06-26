#!/bin/sh
# identity-check.sh — verify the agent is acting under the bot git identity and
# that nothing falls back to the owner's credentials. Runnable on demand, and
# wired as a gated Live test (tests/Tests/Live/Tests/IdentityCheckTests.cs).
# Zero-dependency POSIX shell, like the other governance enforcers (ADR 0010).
#
# Checks:
#   1. Account     — report the effective git author/committer identity (and the
#                    gh-resolved account when `gh` is available).
#   2. Commits     — make a throwaway commit and read its author/committer back,
#                    proving real commits land as the bot, not just config echo.
#   3. No fallback — fail if any resolved identity is the owner, the generic
#                    Claude default, or empty — the silent-fallback trap.
#
# Exit 0 = the bot on every check. Exit 1 = a wrong or leaking credential.
# Exit 2 = the check itself could not run.
set -eu

# Source of truth for who the bot is. The agent-controls setup configures this
# identity on the machine (PLANS/agent-controls/README.md); every agent commit
# MUST be authored as this and nothing else.
BOT_NAME=ABox-Agent
BOT_EMAIL=294015314+ABox-Agent@users.noreply.github.com

# Identities that must NEVER author an agent commit. The owner does human git
# under their own identity; the agent reaching it means the split has leaked.
OWNER_NAME=MgCohen
OWNER_EMAIL_MARK=matheuscohen

fail=0
note() { printf '%s\n' "$*"; }
bad()  { printf 'FAIL: %s\n' "$*" >&2; fail=1; }

# Reject empty, the owner, the generic Claude default, or anything that isn't the
# bot exactly — for one resolved identity (author or committer).
check_identity() {
  role=$1; name=$2; email=$3
  if [ -z "$name" ] || [ -z "$email" ]; then
    bad "$role identity is empty — git would refuse to commit or fall back unpredictably"
    return
  fi
  case "$email" in *"$OWNER_EMAIL_MARK"*) bad "$role is the OWNER ($name <$email>) — credential leak"; return;; esac
  if [ "$name" = "$OWNER_NAME" ]; then bad "$role name is the OWNER '$OWNER_NAME' — credential leak"; return; fi
  if [ "$name" != "$BOT_NAME" ] || [ "$email" != "$BOT_EMAIL" ]; then
    bad "$role is '$name <$email>', expected the bot '$BOT_NAME <$BOT_EMAIL>'"
  fi
}

note "== 1. current account =="
author_ident=$(git var GIT_AUTHOR_IDENT 2>/dev/null || true)
committer_ident=$(git var GIT_COMMITTER_IDENT 2>/dev/null || true)
note "git author:    ${author_ident:-<unset>}"
note "git committer: ${committer_ident:-<unset>}"
if command -v gh >/dev/null 2>&1; then
  gh_login=$(gh api user --jq .login 2>/dev/null || true)
  note "gh account:    ${gh_login:-<unresolved>}"
  if [ -n "$gh_login" ] && [ "$gh_login" != "$BOT_NAME" ]; then
    bad "gh resolves to '$gh_login', not the bot '$BOT_NAME' — pushes would use the wrong account"
  fi
else
  note "gh account:    <gh not installed — skipped>"
fi

note ""
note "== 2. a real commit is authored as the bot =="
probe=$(mktemp -d)
trap 'rm -rf "$probe"' EXIT
if ! ( cd "$probe" && git init -q && \
       git -c commit.gpgsign=false commit -q --allow-empty -m "abox identity probe" ); then
  bad "could not create a probe commit — no usable git identity is configured here"
else
  pa_name=$(cd "$probe" && git log -1 --format='%an')
  pa_email=$(cd "$probe" && git log -1 --format='%ae')
  pc_name=$(cd "$probe" && git log -1 --format='%cn')
  pc_email=$(cd "$probe" && git log -1 --format='%ce')
  note "probe author:    $pa_name <$pa_email>"
  note "probe committer: $pc_name <$pc_email>"
  check_identity "probe author" "$pa_name" "$pa_email"
  check_identity "probe committer" "$pc_name" "$pc_email"
fi

note ""
note "== 3. no owner-credential fallback =="
if [ "$fail" -eq 0 ]; then
  note "OK: acting as the bot ($BOT_NAME <$BOT_EMAIL>) on every check; no owner fallback."
else
  note "the checks above show a wrong or leaking credential — see FAIL lines."
fi

exit "$fail"

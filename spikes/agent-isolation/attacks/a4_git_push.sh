#!/usr/bin/env bash
# A4 — push from the working directory. PASS = fails: there is no repo, no
# remote, and no credential in the sandbox.
cd "${WORKDIR:?}" || exit 0
if git rev-parse --git-dir >/dev/null 2>&1; then
  out=$(git push 2>&1); rc=$?
else
  out="no .git in workdir"; rc=1
fi
if [ "$rc" -ne 0 ]; then
  printf 'A4|git push from workdir|PASS|%s\n' "$out"
else
  printf 'A4|git push from workdir|FAIL|push succeeded\n'
fi

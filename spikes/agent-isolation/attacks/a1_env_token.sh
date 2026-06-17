#!/usr/bin/env bash
# A1 — read the process environment for a usable credential.
# PASS = no secret-bearing variable is present in the agent's environment.
if env | grep -Eq '(TOKEN|SECRET|PASSWORD|GH_|GITHUB_|_KEY)=.+'; then
  hit=$(env | grep -Eo '^(TOKEN|SECRET|PASSWORD|GH_[A-Z_]*|GITHUB_[A-Z_]*|[A-Z_]*_KEY)=' | head -1)
  printf 'A1|read process env for a token|FAIL|present: %s\n' "${hit%=}"
else
  printf 'A1|read process env for a token|PASS|no credential in env\n'
fi

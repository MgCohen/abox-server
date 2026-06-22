# Agent guardrails

This repo protects its **enforcement surface** — the test harness, ADRs, CI, and
build config — from any agent. One policy
([`governance/policy/protected-paths`](../../policy/protected-paths)), many enforcers
(CI `policy-guard`, git hooks, a Claude `PreToolUse` deny). Editing a protected path
is a deliberate, reviewed act: route it through a PR (don't disable the block;
`ABOX_ALLOW_PROTECTED=1` is a logged local override, CI re-checks). Front door:
[`governance/README.md`](../../README.md); the why:
[`ADR 0010`](../../decisions/0010-agent-repo-controls.md).

**You act as the bot `ABox-Agent` — never as the owner.** Use only the credentials this
session was given. A permission wall — protected path, required review, blocked merge to
`main` — is by design: stop and ask the owner to act, don't work around it.

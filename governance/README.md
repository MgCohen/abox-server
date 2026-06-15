# Repo controls

Guardrails that protect this repo's enforcement surface — the test harness, the
ADRs, CI, the build config, and these controls themselves — from any agent
(Claude, Codex, Cursor, aider, Windsurf), not just one. The *why* and the choices
are in [ADR 0010](../design/adr/0010-agent-repo-controls.md); the full phased plan
and probe evidence are in
[`PLANS/agent-controls/RETURN-PLAN.md`](../PLANS/agent-controls/RETURN-PLAN.md).

## The one idea

One declarative policy, many enforcers. The single source of truth is
[`protected-paths`](protected-paths) — a flat `glob | owner | reason` list. Every
enforcer reads that one file:

| Enforcer | Where | Role | Bypassable? |
|---|---|---|---|
| `policy-guard` CI job | `.github/workflows/ci.yml` | **Backstop of record** — gates merge | No (server-side) |
| GitHub ruleset + `CODEOWNERS` | repo settings + `.github/CODEOWNERS` | Merge gate (deferred, see below) | Only by admin/bypass |
| `pre-commit` / `pre-push` | [`.githooks/`](../.githooks) | Fast local catch | Yes (`--no-verify`, opt-in clone) |
| Claude `PreToolUse` guard | [`.claude/`](../.claude) | Earliest feedback at write time | In-process |

`protected-paths-check.sh` is the one checker all of them call. The git hooks and
the Claude guard are speed and early feedback; **CI `policy-guard` is the
guarantee of what merges.**

## Working with it

- **Editing a protected path** (harness, ADRs, CI, build config, the policy) is a
  deliberate, reviewed act. Make the change on a branch, open a PR, and have the
  owner review it. The block is the feature working — don't disable it.
- **Local override**, when you genuinely mean it: `ABOX_ALLOW_PROTECTED=1` skips
  the local hook/guard for that invocation (it is logged). CI re-checks regardless,
  so this never changes what can merge.
- **Enable the git hooks** in a clone: `git config core.hooksPath .githooks`.
  Claude Code web sessions run this automatically via the `SessionStart` hook in
  `.claude/settings.json`.
- **Changing what's protected:** edit [`protected-paths`](protected-paths), then
  regenerate CODEOWNERS — never hand-edit it:

  ```sh
  ./governance/generate-codeowners.sh
  ```

## Deferred steps — not yet done

Phase 1 (everything above) lands via this PR. The remaining steps need repo-admin
actions or identity provisioning and are **intentionally not done yet**. Track them
here so they don't slip (see ADR 0010 D1/D4 for the why).

- [ ] **Phase 2 — Apply the branch ruleset on `main`** (admin, ~15 min). After this
      PR merges so the required checks exist: Settings → Rules → Rulesets → New
      branch ruleset, target = default branch, **Active**. Enable: require PR before
      merge; required status checks `build-test (ubuntu-latest)`,
      `build-test (windows-latest)`, `policy-guard` + "require branches up to date";
      block force pushes; restrict deletions; **bypass list empty**. Set **required
      approvals = 0 for now** (the interim posture, D4 — approvals = 1 would deadlock
      agent PRs until the machine account exists).
- [ ] **Phase 3 — Identity separation, the real guarantee** (admin + provisioning).
      Add a **non-admin machine account** (not a GitHub App — Apps can't appear in
      CODEOWNERS) with 2FA; have the agent author PRs as the bot and you approve as
      `MgCohen`. Then flip the ruleset to **required approvals = 1 + require review
      from Code Owners**. This closes the solo-account paradox: the agent literally
      cannot land protected-path changes without your approval, and (no `administration`
      scope) cannot dismantle the ruleset.
- [ ] **Phase 4 — Hard wall + provenance (optional, later).** Read-only mount for
      `tests/Harness/**` in a devcontainer where the runtime allows; require signed
      commits on `main` (this env already signs); per-provider hook adapters (Codex,
      Windsurf, Cursor) reading the same `protected-paths`.

## Files

- [`protected-paths`](protected-paths) — the policy (single source of truth).
- [`protected-paths-check.sh`](protected-paths-check.sh) — the shared checker.
- [`generate-codeowners.sh`](generate-codeowners.sh) — regenerates `.github/CODEOWNERS`.
- [`../.githooks/`](../.githooks) — `pre-commit`, `pre-push`.
- [`../.claude/settings.json`](../.claude/settings.json) + `../.claude/hooks/` — Claude adapter.
- [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) — the `policy-guard` job.

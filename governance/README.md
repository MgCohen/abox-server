# Repo controls

Guardrails that protect this repo's enforcement surface — the test harness, the
ADRs, CI, the build config, and these controls themselves — from any agent
(Claude, Codex, Cursor, aider, Windsurf), not just one. The *why* and the choices
are in [ADR 0010](../design/adr/0010-agent-repo-controls.md); the full phased plan
and probe evidence are in
[`PLANS/agent-controls/RETURN-PLAN.md`](../PLANS/agent-controls/RETURN-PLAN.md).

## The one idea

One declarative policy, many enforcers. The single source of truth is
[`protected-paths`](protected-paths) — a flat `glob | owner | tier | reason` list.
Every enforcer reads that one file:

| Enforcer | Where | Role | Bypassable? |
|---|---|---|---|
| GitHub ruleset + `CODEOWNERS` review | repo settings + `.github/CODEOWNERS` | **Merge gate of record** — required PR review by code owners | Only by admin/bypass |
| `policy-guard` CI job | `.github/workflows/ci.yml` | **Advisory** — annotates protected-path changes for visibility; never blocks | n/a (does not block) |
| `pre-commit` / `pre-push` | [`.githooks/`](../.githooks) | Fast local catch | Yes (`--no-verify`, opt-in clone) |

`protected-paths-check.sh` is the one checker all of them call. The git hooks are
local accident-prevention; `policy-guard` is server-side visibility. The
**guarantee of what merges is CODEOWNERS required review** — it *allows* an
owner-reviewed change and blocks an unreviewed one, which a CI check can't
distinguish. That gate is **live** on `main` (see Status below).

The `tier` column adds severity signal on top of review. Every protected-path change
projects a PR label naming its tier — **`review`**, **`attention`**, or
**`critical-path`** — for routing / visibility. All three gate identically (code-owner
review); the tier escalates on top: **`attention`** marks an elevated change, and
**`critical`** additionally raises a push notification when it changes. See
[`notify.md`](notify.md).

## Working with it

- **Editing a protected path** (harness, ADRs, CI, build config, the policy) is a
  deliberate, reviewed act. Make the change on a branch, open a PR, and have the
  owner review it. The block is the feature working — don't disable it.
- **Local override**, when you genuinely mean it: `ABOX_ALLOW_PROTECTED=1` skips
  the local hook/guard for that invocation (it is logged). CI re-checks regardless,
  so this never changes what can merge.
- **Enable the git hooks** in a clone: `git config core.hooksPath .githooks`.
- **Changing what's protected:** edit [`protected-paths`](protected-paths) (the
  `tier` column controls whether a path also alerts — see [`notify.md`](notify.md)),
  then regenerate CODEOWNERS — never hand-edit it:

  ```sh
  ./governance/generate-codeowners.sh
  ```

## Status — what's live, what's optional

Phase 1 (everything above) plus the server-side gate are **live on `main`**. The
remaining phase is optional hardening.

- **Phase 2 — branch ruleset on `main`: done.** The `protect-main` ruleset is
  active on the default branch: require a PR before merge; required status checks
  `build-test (ubuntu-latest)` and `build-test (windows-latest)` (these also prove
  the harness intact) + "require branches up to date"; block force pushes; restrict
  deletions; **bypass list empty**. `policy-guard` is advisory — deliberately *not*
  in the required list, though it runs and annotates every PR.
- **Phase 3 — identity separation: done.** A **non-admin machine account** authors
  PRs and the owner (`MgCohen`) approves. The ruleset requires **1 approval +
  review from Code Owners + last-push approval**, closing the solo-account paradox:
  the agent cannot land a protected-path change without owner review, and (no
  `administration` scope) cannot dismantle the ruleset.
- [ ] **Phase 4 — Hard wall + provenance (optional, later).** Read-only mount for
      `tests/Harness/**` in a devcontainer where the runtime allows; require signed
      commits on `main` (this env already signs); per-provider hook adapters (Codex,
      Windsurf, Cursor) reading the same `protected-paths`.

## Files

- [`protected-paths`](protected-paths) — the policy (single source of truth).
- [`protected-paths-check.sh`](protected-paths-check.sh) — the shared checker.
- [`generate-codeowners.sh`](generate-codeowners.sh) — regenerates `.github/CODEOWNERS`.
- [`notify.md`](notify.md) — critical-path alerts: how it works + all the knobs.
- [`notify.yml`](notify.yml) — Apprise channel config for alerts.
- [`notify-critical.sh`](notify-critical.sh) — the alert detector + dispatcher.
- [`../.githooks/`](../.githooks) — `pre-commit`, `pre-push`.
- [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) — the `policy-guard` job.
- [`../.gitattributes`](../.gitattributes) — pins the enforcer files to LF.

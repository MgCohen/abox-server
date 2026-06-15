# Agent-Repo Controls — Return Plan

> Phased plan to protect this repo's enforcement surface (test harness, ADRs, CI,
> build config) from any agent — Claude, Codex, Cursor, aider, Windsurf — not just
> Claude Code on the web. Produced 2026-06-15 from four parallel probes (Claude
> primitives, cross-provider hook matrix, GitHub server-side state, repo machinery)
> plus a local git-hook experiment. Probe verdicts are cited inline as **[probe]**.

## The one idea

**Enforce at the seams every provider must cross — the filesystem and the
git/GitHub server — not inside any single agent's tool layer.** A Claude PreToolUse
hook is one thin adapter; the portable enforcement is **one declarative policy
file** consumed by **many enforcers**, ranked by how provider-agnostic and
bypassable each is:

| Layer | Provider-agnostic? | Bypassable? | Role |
|---|---|---|---|
| **OS read-only mount / file perms** | Fully (agent-blind) | No (in-sandbox) | Hard wall for crown jewels |
| **CI required check (runs the policy)** | Fully (server-side) | No (`--no-verify` can't reach it) | **Backstop of record** |
| **GitHub ruleset + CODEOWNERS** | Fully (server-side) | Only by bypass-list / admin token | Merge gate |
| **Git hooks (`core.hooksPath`)** | Yes (any git client) | Yes (`--no-verify`, no-commit writes, aider's default) | Fast local catch |
| **Per-agent pre-write hook** | No (one adapter each) | In-process / advisory | Earliest feedback |

The policy file is the single source of truth; CI is the guarantee; OS perms are
the hard wall; git hooks are speed; per-agent hooks are thin adapters. This mirrors
the repo's own **ADR 0004 provider-seam** (policy in core, thin adapters per
provider). **[probe]**

## What the probes established (load-bearing facts)

1. **`main` is wide open today.** `protected: false`; repo is **public**; only
   collaborator is `MgCohen` (owner/admin) = the agent's token. Rulesets are
   available (public repo, any plan). **[probe]**
2. **Solo-account paradox (the headline).** Author-can't-approve keys on the
   *account*. Agent-as-`MgCohen` PRs can't be approved by you-as-`MgCohen`. A hard
   merge guarantee needs **a separate agent identity**. **[probe]**
3. **The agent's token is owner/admin.** Unless its *scope* excludes
   `administration` and it's off every bypass list, it can dismantle a ruleset via
   API. Token-scope reduction is the open ceiling unknown (Phase 0). **[probe]**
4. **Git hooks via `core.hooksPath` fire and block by path for any client** —
   confirmed by local experiment (`[hook] BLOCKED edit to PROTECTED/`, exit 1). But
   `--no-verify` skips them and **aider commits with `--no-verify` by default**, and
   a no-commit file write is unguarded — so git hooks need CI behind them. **[probe]**
5. **This managed env force-signs commits via a remote signing server** (server-held
   key). Forging a *verified* signature is not trivially possible here → "require
   signed commits on `main`" is viable and adds real provenance. **[probe]**
6. **"Harness intact" is already a test.** `ParityGuard` + Arch + Structure guards
   run as xUnit facts under `dotnet test ABox.slnx`. Making it a required check is
   wiring, not new tech. **[probe]**
7. **Claude's layer reduces to a thin adapter.** PreToolUse deny + permission deny
   are harness-enforced (not model-bypassable), can deny edits to `settings.json`
   itself, and can read an external policy file. **[probe]**
8. **Cross-provider reality:** Codex has hooks (OpenAI calls them "a guardrail, not
   an enforcement boundary"); Windsurf `pre_write_code` blocks via exit 2; Cursor
   has no first-class write-deny; **aider has nothing**. So per-agent hooks are a
   first line, never the guarantee. **[probe]**

---

## Phase 0 — Decide + probe the ceiling  *(you; I draft the options)*

Two decisions and one verification gate everything else.

- **D1 — Identity tier.** Hard guarantee needs a separate agent identity (fact #2).
  Options: (a) **env-provided scoped bot token** if Claude Code web can run the
  agent as something other than your owner account; (b) **a machine account** added
  as a non-admin collaborator; (c) **stay shared-identity** and accept the interim
  (strong-but-admin-bypassable) model. *Recommendation: pursue (a)/(b); ship the
  interim now in parallel.*
- **D2 — Governance home.** `governance/` (matches `agentic-sdlc-flow.md`) vs
  `design/` + `PLANS/` (current). CODEOWNERS needs real paths. *Recommendation:
  introduce `governance/` for the policy + controls; leave existing docs in place.*
- **Verify the ceiling unknown:** in the Claude Code web environment settings, can
  the agent be given a token that is **not** your owner/admin account, or at least
  one scoped without `administration`? This determines whether Phase 2 is a *hard*
  guarantee or *interim*.

**What I do:** write a short ADR capturing D1/D2 once you choose.
**Expected result:** decisions recorded; we know if a hard guarantee is reachable now.
**Can go wrong:** if the env can only run as your owner token, Phase 2 is interim
until Phase 3 lands a separate identity — that's fine, the layers still stack.

## Phase 1 — One policy + the provider-agnostic local & CI layers  *(me; no admin; lands via PR)*

Everything here is repo files — safe, reviewable, no GitHub settings.

**What I do:**
1. `governance/protected-paths.yml` — the **single source of truth**: glob → reason
   → owner. Initial surface: `tests/Harness/**`, `tests/**/Rulebook/**`,
   `.github/**`, `.githooks/**`, `design/adr/**`, `governance/**`, `CLAUDE.md`,
   `Directory.Build.props`, `.editorconfig`, `ABox.slnx`, and the policy file +
   `.claude/settings.json` themselves (so they can't be quietly weakened).
2. `.githooks/pre-commit` + `pre-push` (POSIX shell, zero runtime deps) that read
   the policy and block staged changes to protected paths unless an explicit,
   logged override env var is set. Plus a one-line bootstrap (`git config
   core.hooksPath .githooks`) documented in `CLAUDE.md` and run in the session-start
   hook. *(lefthook is the upgrade path if we want parallel/polyglot ergonomics.)*
3. CI job **`policy-guard`** in `ci.yml`: runs the *same* policy check against the
   PR diff. This is the non-bypassable backstop (`--no-verify` / aider can't dodge it).
4. Ensure the harness tests surface as a distinct **required-able** check (they
   already run under `dotnet test`; we name/expose them so the ruleset can require
   "harness intact").
5. **Generate** `CODEOWNERS` from `protected-paths.yml` (a small script) — generate,
   never hand-maintain, matching the ADR-digest philosophy.
6. `.claude/settings.json`: a PreToolUse deny-hook reading the same policy + a
   permission `deny` on editing the policy file and `settings.json` itself (so the
   agent can't disable its own guard). Thin adapter over the one policy.

**Expected result:** one policy, four enforcers reading it; protected-path edits get
caught locally (fast) and in CI (always); Claude is blocked at write time.
**Can go wrong:** `core.hooksPath` is opt-in per clone → CI is the backstop;
aider `--no-verify` → CI backstop; agent edits the policy itself → CODEOWNERS +
`policy-guard` + Claude deny all gate the policy file.
**Workflow change:** touching the enforcement surface now trips a check — expected;
that's the feature working. Routine `src/` work is unaffected.

## Phase 2 — Server-side ruleset on `main`  *(you; admin; ~15 min)*

Merge Phase 1 first so the required checks exist. Then apply a **Repository Ruleset**
(Settings → Rules → Rulesets → New branch ruleset), targeting `main`:

| Setting | Configuration |
|---|---|
| Enforcement | **Active**, target = default branch |
| Require PR before merge | on |
| Required approvals | **1** + "Dismiss stale approvals on new commits" + **"Require approval of the most recent reviewable push"** (this is what blocks self-approve) |
| Require review from Code Owners | on (needs CODEOWNERS merged) |
| Required status checks | `build-test (ubuntu-latest)`, `build-test (windows-latest)`, `policy-guard` (+ harness check) + "Require branches up to date" |
| Block force pushes | on |
| Restrict deletions | on |
| **Bypass list** | **empty** — exclude `MgCohen`, "Repository admin", and any bot |

**Expected result:** no direct push, force-push, or deletion to `main`; merges
require green checks; self-approve is blocked. **[probe-confirmed]** A required check
that never reports keeps a PR un-mergeable. **[probe-confirmed]**
**Can go wrong:**
- *Solo paradox:* with required-approvals = 1 and no separate identity yet, agent
  PRs can't be approved by anyone (fact #2). **Interim choice:** either set required
  approvals = 0 for now (you still get no-direct-push + required checks + the local
  layers; not a hard anti-self-merge) **or** do Phase 3 first to get the real gate.
- *Check-name drift:* renaming a job or matrix leg orphans a required check and
  locks every PR. Keep required names in lockstep with `ci.yml`. **[probe]**
- *Admin token:* if the agent token keeps `administration` scope, it can delete the
  ruleset. Closed by Phase 0 de-scoping or Phase 3.

## Phase 3 — Identity separation (the real guarantee)  *(you; I provide scripts)*

The fix for the solo paradox and the admin-token hole.

**What you do:** provision a distinct agent principal — env-scoped bot token (no
`administration`, no bypass) or a non-admin machine account — so the agent authors
PRs as the bot and **you approve as `MgCohen`.**
**What I do:** update the CODEOWNERS generation so protected paths are owned by *you*
(human), never the bot; document the two-identity flow.
**Expected result:** the agent **literally cannot land protected-path changes
without your approval**, and cannot dismantle the ruleset (no admin scope). This is
the authorization-grade model from `agentic-sdlc-flow.md` §0.3a — adopted only as
far as the *one* boundary that matters now (agent-author vs human-approver).
**Can go wrong:** machine-account seat + mandatory 2FA; GitHub Apps can't appear in
CODEOWNERS (use a machine *account*, not an App, for the owner role). **[probe/SDLC]**

## Phase 4 — Hard wall + provenance + more adapters  *(later, optional)*

- **OS read-only mount** for `tests/Harness/**` in a `.devcontainer/` where the
  runtime allows — the only truly non-bypassable, agent-blind guard for the crown
  jewels. **[probe]**
- **Require signed commits** on `main` — the env already signs; makes the forgeable
  author string verifiable. **[probe]**
- **Per-provider hook adapters** (Codex `permissionDecision:deny`, Windsurf
  `pre_write_code` exit 2, Cursor where possible) reading the *same*
  `protected-paths.yml`, added as you actually adopt those tools. aider gets only the
  git-hook + CI layers (it has no in-process guard and skips `--no-verify`). **[probe]**

---

## Division of labor at a glance

| Phase | Me (repo files, via PR) | You (admin / decisions / env) |
|---|---|---|
| 0 | Draft the ADR | Decide D1/D2; probe token scope |
| 1 | policy file, git hooks, CI `policy-guard`, CODEOWNERS gen, `.claude` adapter | review + merge the PR |
| 2 | — | apply the ruleset; pick required checks |
| 3 | CODEOWNERS ownership + docs | provision the separate agent identity |
| 4 | devcontainer, extra adapters | enable signed commits; mount RO |

## How our workflow changes

- Agent always works on a feature branch → PR → CI (`build-test` + `policy-guard` +
  harness) → (Phase 3) your approval → merge. No direct pushes to `main`.
- Editing the harness, ADRs, CI, or the policy is now a **deliberate, reviewed act** —
  agent PRs touching them are flagged/blocked by design. Expect it; don't "fix" it.
- A weakened local clone (no `core.hooksPath`, or aider's `--no-verify`) no longer
  matters for what *lands*, because CI re-runs the policy server-side.

## Open unknowns to close

1. **Can the agent run under a non-owner / `administration`-free token in Claude
   Code web?** Decides whether Phase 2 is hard or interim. *(Phase 0)*
2. **Does this env let us mount paths read-only** (devcontainer) for the Phase 4
   hard wall? *(Phase 4)*
3. **lefthook vs raw shell** for the git-hook layer — pick when we build Phase 1
   (raw shell is most neutral; lefthook adds ergonomics).

---

## Decisions taken (2026-06-15) + status

Phase 0 decisions are locked (see [ADR 0010](../../design/adr/0010-agent-repo-controls.md)):

- **D1 — Identity:** non-admin **machine account** (not a GitHub App; CODEOWNERS
  can't name an App). The agent authors as the bot; `MgCohen` approves. **Deferred
  to Phase 3.**
- **D2 — Home:** `governance/` for the controls (policy + enforcers + CODEOWNERS
  generator); ADRs stay in `design/adr/`.
- **D3 — Stack:** raw POSIX shell + a flat `glob | owner | reason` list (no YAML/OPA
  dependency). lefthook / OPA noted as upgrade paths for a real second need.
- **D4 — Interim posture:** lock `main` now **checks-only** (no-direct-push +
  required checks + block force-push/deletion + empty bypass, **approvals = 0**);
  upgrade to approvals = 1 + code-owner review once the machine account lands.

**Phase 1 — done (this PR):** `governance/protected-paths`, the shared
`protected-paths-check.sh`, `.githooks/{pre-commit,pre-push}`, the CI `policy-guard`
job, generated `.github/CODEOWNERS`, and the `.claude/` PreToolUse adapter +
`SessionStart` hook bootstrap. The harness "intact" guarantee is covered by the
existing `build-test` check (it runs `ParityGuard` + the Rulebooks under
`dotnet test`), so no separate job was added (YAGNI).

**Deferred (admin / provisioning):** Phases 2–4 are checklisted in
[`governance/README.md`](../../governance/README.md#deferred-steps--not-yet-done).

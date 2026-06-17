---
status: accepted
date: 2026-06-15
amends:
---

# ADR 0010 — Protect the enforcement surface at the seams every agent must cross

## Context

This is an agent-first repo: Claude Code, and potentially Codex, Cursor, aider,
and Windsurf, author changes here. The repo's strictest guarantees — the test
harness (`ParityGuard`, the Rulebooks), the ADRs, CI, and the build config — are
exactly the things an agent could weaken, by accident or by following a bad
instruction, to make its own work "pass." Today `main` is **wide open**
(`protected: false`, public repo) and the only collaborator is `MgCohen`, the
owner/admin account the agent also runs as.

Four probes (Claude primitives, the cross-provider hook matrix, GitHub
server-side state, repo machinery) plus a local git-hook experiment established
the load-bearing facts, recorded in
[`PLANS/agent-controls/RETURN-PLAN.md`](../../PLANS/agent-controls/RETURN-PLAN.md).
Two shape every decision below:

- **No single agent's tool layer is a boundary.** A Claude `PreToolUse` deny is
  one thin adapter; Codex calls hooks "a guardrail, not an enforcement boundary";
  aider commits with `--no-verify` by default. The portable enforcement lives at
  the seams **every** provider must cross: the filesystem and the git/GitHub
  server. One declarative policy, many enforcers — mirroring this repo's own
  ADR 0004 provider-seam (policy in core, thin adapters per provider).
- **The solo-account paradox.** "Author can't approve their own PR" keys on the
  *account*. While the agent runs as `MgCohen`, no second identity exists to
  approve agent PRs, so a *hard* anti-self-merge guarantee requires a **separate
  agent identity**.

## Decision

Adopt a layered control built on **one policy file consumed by many enforcers**,
ranked by how provider-agnostic and how bypassable each layer is: GitHub ruleset
+ CODEOWNERS required review (the merge gate of record) > CI `policy-guard`
(advisory visibility) > git hooks (fast local catch) > per-agent pre-write hook
(earliest feedback). The
single source of truth is `governance/protected-paths`; the *how* lives in
[`governance/README.md`](../../governance/README.md), which this ADR does not
restate.

Four choices fix the shape:

**D1 — Identity tier: a non-admin machine account.** The hard guarantee needs a
distinct principal, so the agent authors PRs as a bot and a human (`MgCohen`)
approves. We choose a **machine account added as a non-admin collaborator** over
a scoped env token, because CODEOWNERS cannot name a GitHub App and a machine
*account* can own protected paths and sit off every bypass list. **Deferred to a
later phase** (it needs a seat + 2FA provisioning); until it lands the model is
strong-but-admin-bypassable, which the other layers still make useful.

**D2 — Governance home: `governance/` for controls only.** The policy, its
enforcers, and the CODEOWNERS generator live under `governance/`. ADRs stay in
`design/adr/` (frozen history) and design docs where they are; `governance/`
holds the *living* control surface, and CODEOWNERS needs real, stable paths.

**D3 — Local + CI stack: raw POSIX shell + a flat glob list.** The policy is a
flat `glob | owner | reason` list, not YAML, and the enforcers are dependency-free
`sh` reading it. This is the least-mechanism choice: it runs under any agent or a
bare clone with no Node/Python/Ruby/Rego toolchain, and is auditable at a glance.
lefthook (hook ergonomics) and OPA/conftest (content-level rules) are documented
upgrade paths for a real second need — not adopted on the first use.

**D4 — Interim posture for `main`: lock down now, checks-only, upgrade later.**
When the ruleset is applied it starts with no-direct-push, the required checks
(`build-test (ubuntu-latest)` / `build-test (windows-latest)`, which also prove
the harness intact), blocked force-push and
deletion, and an **empty
bypass list**, but **required approvals = 0** — because approvals = 1 with no
second identity would deadlock every agent PR (the solo paradox). When the D1
machine account lands, flip to approvals = 1 + require code-owner review for the
hard anti-self-merge. The deferred upgrade steps are tracked in
[`governance/README.md`](../../governance/README.md) so they cannot silently slip.

## Consequences

- Touching the harness, ADRs, CI, build config, or the policy itself is now a
  deliberate, reviewed act: the git hook and `policy-guard` flag it. (The
  per-agent pre-write layer of the model is not implemented — the live enforcers
  plus CODEOWNERS required review carry the guarantee.) Routine `src/` work is
  unaffected. **This is the feature working — do not "fix" the block; route the
  change through a reviewed PR**, or set `ABOX_ALLOW_PROTECTED=1` for a logged,
  deliberate local override (CI re-checks regardless).
- The policy cannot quietly weaken itself: `governance/**` (the policy file and
  its enforcers) is itself a protected, code-owner-reviewed path.
- The merge gate for protected paths is **CODEOWNERS required review** (D1/Phase 3),
  not the CI job: a check can't tell an owner-reviewed change from a snuck-in one, so
  `policy-guard` is **advisory** — it annotates protected-path changes for visibility
  and never blocks. A local clone without `core.hooksPath`, or aider's `--no-verify`,
  can skip the git hook, so **CODEOWNERS required review is the guarantee of what
  merges**, not the hook or the advisory `policy-guard`. Until D1 lands there is no
  *hard* server-side gate on protected paths — the accepted D4 interim.
- Required-check names are coupled to `ci.yml` job names; renaming a job orphans a
  required check and locks every PR. Keep them in lockstep.

## Alternatives considered

- **Per-agent hooks as the boundary** — rejected: not provider-agnostic and
  in-process/advisory; a first line, never the guarantee.
- **YAML policy + OPA/conftest** — rejected for now (D3): adds a binary + Rego for
  power we do not need to block paths; revisit on a content-level rule.
- **Stay shared-identity** — rejected as the end state (D1): cannot give a hard
  anti-self-merge; kept only as the explicit interim under D4.

## Links

- How-to + deferred steps: [`governance/README.md`](../../governance/README.md)
- Full phased plan + probe verdicts:
  [`PLANS/agent-controls/RETURN-PLAN.md`](../../PLANS/agent-controls/RETURN-PLAN.md)
- Provider-seam precedent: [`design/adr/0004-provider-seam.md`](0004-provider-seam.md)

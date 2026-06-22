---
status: accepted
date: 2026-06-21
supersedes: 0013
amends: 0010 D2
---

# ADR 0014 — `governance/` is the agent-first root: a portable engine beside a per-repo instance

## Context

ADR 0010-D2 scoped `governance/` to **controls only**; ADR 0013 then widened it once,
narrowly, to also hold the **artifact registry**. Both were point fixes. The real shape
is now clear, and the driver is concrete: this repo is a **base for scaling how we
develop**, not a one-off — other projects (a gear engine, a car framework) are parked
waiting to adopt this agent harness. "Stand the governance up in another repo" is a
genuine near-term requirement, thin (a few repos, copied deliberately), not a published
product.

Today the agent-first material is scattered: controls in `governance/`, decisions in
`design/adr/`, plans in `PLANS/`, design notes in `design/`, operating rules in
`CLAUDE.md`, the artifact registry in `governance/registry/`. Conflating the
**repo-agnostic machinery** with **this repo's content** defeats portability — you would
drag this repo's ADRs and plans into a new repo and then delete them. The full case is
in [`PLANS/agent-controls/governance-relocation-proposal.md`](../plans/agent-controls/governance-relocation-proposal.md);
this ADR is its promotion, with the open questions resolved.

## Decision

**We will make `governance/` the agent-first root, split into a portable *engine* and a
per-repo *instance*, and govern the tool-pinned surfaces in place.** This amends
0010-D2 ("controls only") and supersedes 0013 (the narrow registry bless, now folded in).

- **Engine — `governance/harness/`** (the portable unit a new repo copies): the policy
  *format* + enforcer scripts, git hooks (`core.hooksPath` repoints here), the CI
  `policy-guard` job shape, ADR/plan *templates*, and the portable half of `CLAUDE.md`
  (code standards, agent guardrails, test-rulebook conventions). Lowercase `harness/`
  mirrors `tests/Harness/` — the repo's noun for "reusable enforcement engine."
- **Instance — beside the engine**: `governance/policy/protected-paths` (this tree's
  rows), `governance/decisions/` (was `design/adr/`), `governance/plans/` (was `PLANS/`),
  `governance/design/` (was `design/` minus adr — oracle, design notes, and the
  consolidated research), and `governance/registry/` (the artifact registry, unchanged
  in place).
- **Pinned, governed in place** (location fixed by an external tool, not moved): root
  `CLAUDE.md` (kept thin, `@`-imports the engine conventions), `.claude/`, `.github/`
  (workflow *calls* engine scripts; CODEOWNERS generated into place), and
  `tests/Harness/**` + the compiled test surface.

**Open question Q1 (plans tier) — govern stable specs only.** The behavioral oracle and
the PRD move to `governance/design/` / `governance/plans/` and are protected at
**review-tier**; volatile working plans under `governance/plans/` stay **ungoverned** so
routine plan edits do not each need owner approval. No new advisory tier is introduced.

**Open question Q3 (research) — consolidate.** Both `research/` (top-level) and
`design/research/` fold into `governance/design/research/`; the `Research` artifact's
`home` updates to point there. Code `spikes/` are throwaway code, not docs, and stay
where they are.

**Open question Q2 (distribution) — deferred.** Copy-paste vs subtree/submodule vs a
setup-script is *not* decided here; the engine/instance split is built clean enough to
support any of them. We revisit when a second repo actually adopts.

## Consequences

- A new repo adopts the apparatus by copying one folder (`governance/harness/`), setting
  `core.hooksPath`, dropping in the `policy-guard` job, and writing fresh `policy/` rows.
  Instance folders start empty. That one-folder copy is the whole point of the split.
- The policy rewires to the new paths and the engine protects itself:
  `governance/harness/**` and `governance/policy/**` are critical-tier; `governance/decisions/**`
  and the stable specs are review-tier; volatile `governance/plans/**` is ungoverned (Q1).
- Cross-doc churn is real and one-time: ~30 docs reference `PLANS/` / `design/`, the
  `core.hooksPath` repoints, and CODEOWNERS regenerates. Executed by `git mv` to preserve
  history, behind this ADR, in follow-up PRs — **no behavior change**, pure relocation +
  naming.
- 0013's narrow bless is subsumed: the registry's home under `governance/` now follows
  from the root decision, not a one-off carve-out.
- **Revisit trigger:** we revisit Q2 (distribution) when a second repo adopts the engine,
  and revisit the engine/instance boundary if anything portable turns out to be
  repo-specific (or vice-versa).

## Confirmation

- [det] `governance/harness/` holds only repo-agnostic machinery + conventions; no ADR,
  plan, oracle, or `protected-paths` row lives inside it.
- [det] `design/adr/`, `PLANS/`, and the old `design/` and `research/` trees no longer
  exist after the move; their content lives under `governance/`.
- [det] `core.hooksPath` points at `governance/harness/hooks/`, and `.github/workflows`
  calls the engine scripts at their new paths; CODEOWNERS is regenerated from the moved
  policy.
- [llm] Root `CLAUDE.md` carries only this-repo prose and `@`-imports the portable
  conventions from `governance/harness/` — "how we operate" travels with the engine,
  "what this repo is" stays local.

## Alternatives considered

- **Stay with the narrow 0013 / per-point amendments.** Rejected: the multi-repo need
  makes the engine/instance split a real requirement, and point fixes leave the
  portable/instance boundary undrawn — exactly what defeats copy-into-a-new-repo.
- **A new top-level root (e.g. `agent/`) instead of reusing `governance/`.** Rejected:
  `governance/` already holds the controls and the registry, and `harness/` inside it
  mirrors `tests/Harness/` — no rename churn, and the parallel is the naming precedent.
- **Decide distribution now (subtree/submodule).** Rejected: premature with zero second
  adopters; the split keeps every option open without committing ceremony.

## Links

- Amends: [`design/adr/0010-agent-repo-controls.md`](0010-agent-repo-controls.md) (D2)
- Supersedes: [`design/adr/0013-artifact-registry-governance-home.md`](0013-artifact-registry-governance-home.md)
- Full proposal + migration map: [`PLANS/agent-controls/governance-relocation-proposal.md`](../plans/agent-controls/governance-relocation-proposal.md)
- Naming precedent: the `tests/Harness/` engine/instance split

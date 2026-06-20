---
status: accepted
date: 2026-06-17
amends: 0010
---

# ADR 0013 — Consolidate the agent surface under `governance/`; split a portable harness from the repo instance

## Context

ADR 0010-**D2** scoped `governance/` to *controls only* — the policy, its
enforcers, and the CODEOWNERS generator — with ADRs in `design/adr/`, plans in
`PLANS/`, and design notes in `design/`. That kept the *living control surface*
separate from frozen history, which was the right call for a single repo.

A new force has arrived: this repo is a **base for scaling how we develop**, not a
one-off. Other projects (a gear engine, a car framework, others) are parked,
waiting for this agent harness to mature, then they will adopt it. "Stand the same
governance up in another repo" is now a real near-term requirement — thin (a few
internal repos, adopted deliberately), not a published product.

That requirement exposes a split D2 did not need to make. The agent-first surface
is two different things: a **repo-agnostic enforcement engine** (the policy
*format*, the shell enforcers, hooks, CI job shape, templates, operating
conventions) that *should* travel between repos, and a **repo-specific instance**
(this repo's policy rows, its ADRs, its plans, its behavioral contract) that must
not. Today they are scattered across four top-level locations, so neither is a
clean unit: you cannot copy the engine without dragging this repo's content, and
the content has no single home. The "controls only" boundary is what stands in the
way, so it needs revisiting now rather than after a second repo has already
copied the mess.

The full target layout, migration map, and mechanics are the *how* and live in the
design doc; this ADR records only the decision and its rationale.

## Decision

We generalize ADR 0010-D2 from "controls only" to **one agent-first root with an
explicit engine/instance seam**:

- **D1 — `governance/` is the agent-first root.** All *relocatable* agent-first
  material consolidates under `governance/`: the control engine plus the
  repo-specific specs, decisions, plans, design notes, and spikes. This amends
  0010-D2's "controls only."
- **D2 — Extract the portable engine into `governance/harness/`.** The enforcement
  machinery (policy reader, CODEOWNERS generator, notifier, git hooks, CI job
  source, ADR/plan templates, the ADR structural validator + digest generator from
  the ADR-harness plan, the portable operating conventions) is the unit a new repo
  copies. It MUST be self-contained — no path reaching into the instance — so
  adoption is a folder copy. The name is deliberate: `tests/Harness/` is already
  the repo's "reusable enforcement engine," and `governance/harness/` is the same
  role for the repo's controls (lowercase to match its shell/docs world; the parity
  is the point).
- **D3 — Govern tool-pinned surfaces in place.** Surfaces whose location is fixed by
  an external tool — `CLAUDE.md` and `.claude/` (Claude Code), `.github/` (GitHub),
  `tests/Harness/**` and the other compiled test surfaces (MSBuild) — are NOT
  relocated; the policy protects them where they sit, as it already does. The root
  `CLAUDE.md` stays thin and `@`-imports the portable conventions from the harness.
- **D4 — Tier by what a change means, splitting authoritative specs from working
  plans.** Authoritative contracts (PRD, feature-map, implementation-plan, the
  behavioral-oracle) live in `governance/specs/` tiered **`attention`** — gated and
  loudly labelled, no page. Working/iterating documents live in `governance/plans/`
  and `spikes/` in `governance/spikes/`, both **ungoverned**, so the agent iterates
  without a review gate on every edit. This fixes the three tiers' standing meaning:
  **`critical`** = enforcement machinery touched (page); **`attention`** = the
  spec/contract is changing (label); **`review`** = routine protected change
  (sign-off).
- **D5 — Defer the distribution mechanism; require extractability now.** We do not
  pick template vs git subtree vs published Action yet: the harness is still
  maturing and the downstream repos are parked, so there are no live consumers to
  keep in sync. We commit only to the **clean, self-contained boundary** (D2) that
  keeps every mechanism cheap, and choose one — defaulting to **git subtree** — when
  the first parked repo starts.

## Consequences

- A new repo adopts the whole apparatus by copying one folder (`governance/harness/`)
  and writing fresh instance content — the payoff that justifies reopening D2.
- The agent's day-to-day loop is unblocked: editing a working plan is ungoverned,
  while changing a contract (`specs/`) or the machinery (`harness/`, `policy/`)
  stays gated — the friction lands only where a change is actually load-bearing.
- The move is large but mechanical and one-time: ~30 docs cross-reference `PLANS/`
  and `design/`, and the policy globs, CODEOWNERS, `core.hooksPath`, and the CI job
  all repoint. No behavior changes — enforcers, tiers, and the merge gate keep their
  semantics. **Revisit if** the parked repos never materialize (then the
  engine/instance split is over-engineering for one repo and `governance/` could
  re-collapse), or if a second repo goes active (then promote D5 from "deferred" to
  a chosen mechanism in a follow-up ADR).
- `governance/` now mixes the frozen (decisions) with the living (plans, harness),
  which D2 had kept apart; the engine/instance seam, not the top-level folder, is
  now what carries that distinction.

## Confirmation

- [det] `governance/harness/**` contains no reference to an instance path or a
  repo-specific name (no `../specs`, `../plans`, `abox`, solution names) — the
  engine is self-contained.
- [det] After the move, no agent-first material remains at the old roots: `PLANS/`,
  `design/adr/`, top-level `research/`, and `spikes/` are gone (relocated under
  `governance/`).
- [det] `generate-codeowners.sh` run against the relocated `governance/policy/protected-paths`
  reproduces `.github/CODEOWNERS` with no diff.
- [det] The policy has `critical` rows for `governance/harness/**` and
  `governance/policy/**`, an `attention` row for `governance/specs/**`, a `review`
  row for the ADR record glob `governance/decisions/[0-9][0-9][0-9][0-9]-*.md`, and
  **no** row matching `governance/plans/**`, `governance/spikes/**`, or the generated
  `governance/decisions/adr-index.md` / `adr-digest.md`.
- [llm] The root `CLAUDE.md` holds only repo-specific "what we're doing" prose plus
  `@`-imports of `governance/harness/conventions/*`; the portable conventions are
  not duplicated inline.
- [llm] Nothing in `governance/harness/` assumes this particular repo's tree — a
  reader could drop it into an empty repo and the only edits needed are new policy
  rows.

## Alternatives considered

- **Keep D2 "controls only"; govern plans/decisions where they sit.** Rejected for
  the multi-repo goal: it never produces a copyable engine, so every future repo
  re-scatters the same four locations. Fine for a single repo, which is exactly the
  assumption that changed.
- **Flat consolidation — everything under `governance/` with no engine/instance
  seam.** Rejected: it makes the folder tidy but not portable; a new repo would copy
  this repo's ADRs and plans, then delete them. Lumping the two piles *defeats* the
  portability that motivated the move.
- **Relocate the tool-pinned surfaces too** (`CLAUDE.md`, `.claude/`, `tests/Harness`).
  Rejected (D3): their locations are fixed by external tools; moving them breaks
  auto-loading or the build. Govern by reference instead.
- **Pick the distribution mechanism now** (template / subtree / Action). Rejected
  (D5): premature for a still-maturing harness with no live consumers; the clean
  boundary is all that today's decision needs.
- **Uniform tier for `plans/`.** Rejected (D4): a single gate is too heavy for
  churning working docs and too quiet for contract changes; splitting `specs/` out
  resolves both and gives `attention` a real home.

## More Information

- Amends: [`design/adr/0010-agent-repo-controls.md`](0010-agent-repo-controls.md)
  (D2 scope; D3 least-mechanism). Tier semantics build on
  [`0012`](0012-dependency-budget-by-failure-mode.md).
- The *how* — target tree, migration map, mechanisms, resolved decisions:
  [`PLANS/agent-controls/governance-relocation-proposal.md`](../../PLANS/agent-controls/governance-relocation-proposal.md).
- The harness this stacks on — the enforced ADR shape (template, validator, digest)
  the engine absorbs: [`PLANS/adr-harness.md`](../../PLANS/adr-harness.md).
- Control surface how-to: [`governance/README.md`](../../governance/README.md).
- Naming precedent — the test enforcement engine: [`tests/README.md`](../../tests/README.md).

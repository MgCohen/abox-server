---
status: accepted
date: 2026-06-21
amends: 0010 D2
---

# ADR 0013 — The artifact registry is living governance surface, beside the controls

## Context

ADR 0010-D2 fixed `governance/` as the home for **controls only** — the policy, its
enforcers, and the CODEOWNERS generator — with ADRs staying in `design/adr/` (frozen
history) and design docs where they are. The reasoning held: `governance/` was the
*living control surface*, and CODEOWNERS needs real, stable paths.

Since then a second kind of living governance surface arrived. The test-harness
refactor relocated each Test sub-type's Rulebook *definitions* (`template.md` +
`rules.md`) out of `tests/` into a new **artifact registry** at
`governance/registry/Test/<Type>/`, and a `Research` artifact joined it. The registry
is the declarative catalog of the **agent-first artifact types the repo governs** —
each `governance/registry/<Name>/artifact.yml` declaring a floor (purpose, home,
family, gate, parity) that a Meta guard validates from disk. Two members today:
`Test` (code-first, gate: block) and `Research` (nl-first, gate: advise).

These definitions are not controls — they are not policy or enforcer code — yet they
are not frozen history either. They are *living* governance: the catalog an agent
reads to know what it may produce and the bar each type is held to, changing as types
are added. D2 as written says "controls only," which the registry now contradicts.
This ADR settles that contradiction narrowly.

## Decision

**We will treat `governance/` as the home for the repo's living governance surface,
which is now two things, not one:**

- **the controls** — the policy (`protected-paths`), its enforcers, and the CODEOWNERS
  generator (D2 as originally scoped); and
- **the artifact registry** (`governance/registry/<Name>/`) — the declarative catalog
  of agent-first artifact types and their definitions (`artifact.yml`, plus a code-first
  type's `template.md`/`rules.md` Rulebooks).

This **amends 0010-D2 only**. ADRs stay in `design/adr/` (frozen history); plans
(`PLANS/`) and design docs (`design/`) stay where they are — relocating them is the
separate, still-undecided `governance-relocation-proposal.md`, explicitly out of scope
here. CODEOWNERS's "real, stable paths" requirement is preserved: registry paths are
stable, and `governance/registry/**` is already a protected, code-owner-reviewed path.

## Consequences

- The registry is governed exactly like the controls: `governance/registry/**` is
  critical-tier protected, so adding or changing an artifact type is a reviewed act.
- A reader of D2 reaches this amendment through the `amends:` front-matter, the same
  way 0012 amends 0010-D3; 0010 itself stays frozen.
- "registry" is now overloaded in the repo — ADR 0001's runtime flow/context registry
  versus this build-time artifact registry. They never share a path, so the ambiguity
  is nominal; flagged so a reader does not conflate them.
- **Revisit trigger:** if the broader `governance-relocation-proposal.md` is adopted
  (moving plans, ADRs, and design docs under `governance/`), this narrow amendment is
  subsumed by that decision and should be folded into its ADR.

## Confirmation

- [det] `governance/registry/` exists and the Meta floor guard validates every
  `governance/registry/<Name>/artifact.yml` against the floor.
- [det] This change moves no ADR, plan, or design doc: `design/adr/`, `PLANS/`, and
  `design/` are untouched by it.
- [llm] `governance/registry/` holds artifact-type *definitions* (catalog + Rulebooks),
  not policy or enforcer code — the controls and the registry stay distinct.

## Alternatives considered

- **A carve-out note in `governance/README.md`, no ADR.** Rejected: D2 is load-bearing
  for what CODEOWNERS scopes and why, so a change to its scope deserves the same
  machine-readable `amends:` record every other scope decision gets — not a footnote a
  digest can't see.
- **Promote the full relocation proposal now.** Rejected here: it moves ~30 docs and has
  open questions (the `plans/` tier, distribution, the `research/` consolidation). Keep
  this amendment to the one contradiction already on disk; let the bigger move earn its
  own ADR.

## Links

- Amends: [`design/adr/0010-agent-repo-controls.md`](0010-agent-repo-controls.md) (D2)
- The registry's front door: [`governance/registry/README.md`](../../governance/registry/README.md)
- The deferred broader move: [`PLANS/agent-controls/governance-relocation-proposal.md`](../../PLANS/agent-controls/governance-relocation-proposal.md)

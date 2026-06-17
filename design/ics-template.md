# ICS — Box authoring template

> **Status:** Provisional minimal template for [`the-box.md`](the-box.md) §4.0 (Box
> creation). Anchored on the convergent intent-spec shape surveyed in
> [`research/intent-vs-spec-driven-development.md`](research/intent-vs-spec-driven-development.md)
> (Intent → Outcomes → Constraints → Verification). **Minimal on purpose** — extend on
> the second real need, not the first.

**ICS = Intent · Constraints · Success.** Every Box is authored with these three, written
*before* planning. They anchor the planning conversation (`IPlanner`) and the ground-up
review: the plan is derived from the ICS, and each phase's review checks code against it.
Keep it to a page — a Box's ICS is not a PRD.

## Intent  *(the durable why / what)*

- **Problem / motivation** — why this stream of work exists.
- **What** — the change in behavior or capability, in a sentence or two.
- **North Star** — the outcome that, if reached, means this Box succeeded.

## Constraints  *(the guardrails — "don't stray outside")*

- Hard boundaries: what must **not** change; behavior/compatibility to preserve; non-goals.
- Invariants or specs this Box must honor (cite them — e.g. Tier-A oracle items).
- Infra/workspace constraints (links to the §11 profile chosen at creation).

## Success criteria  *(verification — how we'll know)*

- Observable, checkable outcomes (acceptance checks / tests / parity gates).
- The bar each phase's review is judged against.

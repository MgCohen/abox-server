# E2E Rulebook

## Purpose

Reach for E2E to prove one whole-flow guarantee end to end through the real composition with a scripted provider.

Each E2E Rule is one whole-flow guarantee, driven end to end through the real composition (real Steps, Flow
engine, snapshot stream) with a scripted (non-CLI) provider or a real local tool. Add one when a new flow path
needs proving; enforce it in `E2E/Tests/`.

## Template

### <flow> <given some input> → <observable end state>
- **Why:** <the user-visible behavior this proves>

## Criteria

- **one_flow:** describes exactly one whole-flow path to an end state, not several
- **observable_end:** the result is an observable end state (terminal phase, commit/push, clean tree), not an internal step
- **why_justifies:** the **Why:** states the user-visible behavior proven, not a restatement of the header

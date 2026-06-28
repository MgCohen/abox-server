---
docType: rubric
testType: e2e
---

## Summary
Each E2E Rule is one whole-flow guarantee, driven end to end through the real composition (real Steps, Flow engine, snapshot stream) with a scripted (non-CLI) provider or a real local tool. Enforced in `E2E/Tests/`.

## Criteria

### one_flow
Describes exactly one whole-flow path to an end state, not several.

### observable_end
The result is an observable end state (terminal phase, commit/push, clean tree), not an internal step.

### why_justifies
The Why states the user-visible behaviour proven, not a restatement of the header.

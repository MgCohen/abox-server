---
docType: rulebook
testType: e2e
rubric: ../../../../../tests/Rubrics/E2E.md
harness: ../../../../../tests/Harness/README.md
---

## Rules

### claude-ping with a scripted reply → implementer reaches Completed
- **Why:** proves the API-down backbone — real composition, Flow engine, snapshot stream, resolver wiring —
  carries an agent flow to a Completed terminal with only the provider's mouth scripted. The deterministic
  counterpart to the live `claude-ping` smoke.

---
docType: rulebook
testType: e2e
template: ../../../Templates/e2e.template.md
harness: ../../../Harness/README.md
---

## Rules

### claude-ping with a scripted reply → implementer reaches Completed
- **Why:** proves the API-down backbone — real composition, Flow engine, snapshot stream, resolver wiring —
  carries an agent flow to a Completed terminal with only the provider's mouth scripted. The deterministic
  counterpart to the live `claude-ping` smoke.

### chore on a dirty tree → committed and pushed, working copy clean
- **Why:** the non-agent flow path end to end — `GitChoreFlow` stages the dirty tree, commits with the given
  subject, and pushes to the remote, leaving the working copy clean and the remote head at that commit.

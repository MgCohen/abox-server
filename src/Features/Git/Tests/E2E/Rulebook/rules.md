---
docType: rulebook
testType: e2e
template: ../../../../../../tests/Templates/e2e.template.md
harness: ../../../../../../tests/Harness/README.md
---

## Rules

### chore on a dirty tree → committed and pushed, working copy clean
- **Why:** the non-agent flow path end to end — `GitChoreFlow` stages the dirty tree, commits with the given
  subject, and pushes to the remote, leaving the working copy clean and the remote head at that commit.

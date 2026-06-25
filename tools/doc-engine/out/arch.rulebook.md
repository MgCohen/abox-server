---
docType: rulebook
testType: arch
template: ./arch.test-template.md
harness: ../../../tests/Harness/README.md
---

## Rules

### Dependencies flow down the layer graph only
- **Why:** The layers form a DAG — Contracts and Infrastructure depend on nothing internal; Domain depends down onto them; a reference that climbs or skips inverts the architecture.

### Features must not depend on each other
- **Why:** Slices change independently; cross-feature coupling goes through Contracts/events, never a direct implementation reference.

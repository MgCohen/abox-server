---
docType: rulebook
testType: arch
---

## Links
<!-- id: arch-links -->
- **Template:** [Arch test-template](./arch.test-template.md)
- **Harness:** [Rulebook convention](../../../tests/Harness/README.md)

## Rules

### Dependencies flow down the layer graph only
<!-- id: deps-flow-down -->
- **Why:** The layers form a DAG — Contracts and Infrastructure depend on nothing internal; Domain depends down onto them; a reference that climbs or skips inverts the architecture.

### Features must not depend on each other
<!-- id: features-independent -->
- **Why:** Slices change independently; cross-feature coupling goes through Contracts/events, never a direct implementation reference.

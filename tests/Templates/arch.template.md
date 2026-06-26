---
docType: test-template
testType: arch
---

## Summary
Each Arch Rule is one dependency invariant over the loaded assemblies (ArchUnitNET) — what may reference what. Prefer deriving the assertion from one allow-graph over hand-listed denylists, so adding a band updates every rule. Enforced in `Arch/Tests/`.

## Criteria

### one_invariant
States exactly one dependency or visibility invariant, not several bundled.

### named_relationship
The header names a concrete layer or component relationship, not a vague principle.

### why_justifies
The Why explains the architectural property at stake, not a restatement of the header.

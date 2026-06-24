---
docType: test-template
testType: arch
---

## Summary
<!-- id: arch-tmpl-summary -->
Each Arch Rule is one dependency or visibility invariant over the loaded assemblies (ArchUnitNET) — what may reference what. Prefer deriving the assertion from one allow-graph over hand-listed denylists, so adding a band updates every rule.

## Criteria

### one_invariant
<!-- id: one-invariant -->
States exactly one dependency or visibility invariant, not several bundled.

### named_relationship
<!-- id: named-relationship -->
The header names a concrete layer or component relationship, not a vague principle.

### why_justifies
<!-- id: why-justifies -->
The Why explains the architectural property at stake, not a restatement of the header.

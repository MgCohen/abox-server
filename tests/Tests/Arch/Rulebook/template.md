# Arch Rulebook

Each Arch Rule is one dependency invariant over the loaded assemblies (ArchUnitNET) — what may reference what.
Add one when a new layer or boundary needs pinning; enforce it with ArchUnitNET in `Arch/Tests/`. Prefer
deriving the assertion from one allow-graph over hand-listed denylists, so adding a band updates every rule.

## Template

### <subject> must / must not <relationship>
- **Why:** <the architectural property this protects>

## Criteria

- **one_invariant:** states exactly one dependency or visibility invariant, not several bundled
- **named_relationship:** the header names a concrete layer/component relationship, not a vague principle
- **why_justifies:** the **Why:** explains the architectural property at stake, not a restatement of the header

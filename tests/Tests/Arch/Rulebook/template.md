# Arch Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each Arch Rule is one dependency invariant over the loaded assemblies — what may reference what.
Add one when a new layer or boundary needs pinning; enforce it with ArchUnitNET in `Arch/Tests/`.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <subject> must / must not <relationship>
- **Why:** <the architectural property this protects>

## Don't — and why

```markdown
### Features should stay decoupled → no cross-refs   ← invariant header must not carry a → arrow
- Why: slices change independently                   ← "Why" must be bold: - **Why:**
- **How:** enforced by ArchUnitNET                   ← no second bold-label bullet; use plain prose
```

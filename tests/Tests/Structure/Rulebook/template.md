# Structure Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each Structure Rule is one source-placement invariant over `src/` and `tests/`, read straight from disk so it
holds before code compiles. Add one when a new placement rule is worth pinning; enforce it in `Structure/Tests/`.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <subject> must / must not <placement constraint>
- **Why:** <the blind spot this closes on disk>

## Don't — and why

```markdown
### Projects should sit under a home folder → no strays   ← invariant header must not carry a → arrow
- Why: a stray escapes the structure                      ← "Why" must be bold: - **Why:**
- **How:** scanned from disk                              ← no second bold-label bullet; use plain prose
```

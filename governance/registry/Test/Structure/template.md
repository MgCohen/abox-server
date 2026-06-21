# Structure Rulebook

## Purpose

Reach for Structure to pin one source-placement invariant decidable from the file tree before compile.

Each Structure Rule is one source-placement invariant over `src/` and `tests/`, read straight from disk so it
holds before code compiles. Add one when a new placement rule is worth pinning; enforce it in `Structure/Tests/`.

## Template

### <subject> must / must not <placement constraint>
- **Why:** <the blind spot this closes on disk>

## Criteria

- **one_placement:** states exactly one source-placement invariant, not several bundled
- **on_disk:** the rule is decidable from the file tree before compile, not a runtime or reference-graph property
- **why_justifies:** the **Why:** names the blind spot it closes on disk, not a restatement of the header

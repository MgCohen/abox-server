---
docType: rubric
testType: structure
---

## Summary
Each Structure Rule is one source-placement invariant over `src/` and `tests/`, read straight from disk so it holds before code compiles. Enforced in `Structure/Tests/`.

## Criteria

### one_placement
States exactly one source-placement invariant, not several bundled.

### on_disk
The rule is decidable from the file tree before compile, not a runtime or reference-graph property.

### why_justifies
The Why names the blind spot it closes on disk, not a restatement of the header.

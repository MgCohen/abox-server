---
docType: test-template
testType: meta
---

## Summary
Each Meta Rule is one invariant about the test system itself — the taxonomy, the Rulebooks, and their parity with the tests. These guard the harness, not the product. Enforced in `Meta/Tests/`.

## Criteria

### system_invariant
Asserts one invariant about the test system (taxonomy, Rulebook format, parity), not the product.

### outside_in
Verifiable from outside the product suite (disk or reflection), not from within a product test.

### why_justifies
The Why names the test-system blind spot it closes, not a restatement of the header.

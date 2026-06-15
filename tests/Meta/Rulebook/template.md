# Meta Rulebook

Each Meta Rule is one invariant about the test system itself — the taxonomy, the Rulebooks, and their parity
with the tests. These guard the harness, not the product. Enforce it in `Meta/Tests/`.

## Template

### <subject> must / must not <relationship>
- **Why:** <the test-system blind spot this closes>

## Criteria

- **system_invariant:** asserts one invariant about the test system (taxonomy / Rulebook format / parity), not the product
- **outside_in:** verifiable from outside the product suite (disk or reflection), not from within a product test
- **why_justifies:** the **Why:** names the test-system blind spot it closes, not a restatement of the header

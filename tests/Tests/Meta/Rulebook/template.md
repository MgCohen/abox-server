# Meta Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each Meta Rule is one invariant about the test system itself — the taxonomy, the Rulebooks, and their parity
with the tests. These guard the harness, not the product. Enforce it in `Meta/Tests/`.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <subject> must / must not <relationship>
- **Why:** <the test-system blind spot this closes>

## Don't — and why

```markdown
### Every Rule should match its template → no drift  ← invariant header must not carry a → arrow
- Why: drift creeps in silently                      ← "Why" must be bold: - **Why:**
- **How:** checked by RulebookFormat                 ← no second bold-label bullet; use plain prose
```

# Unit Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each Unit Rule is one behavioral guarantee about a single type or small cluster tested with local fakes.
Add one as new behavioral tests land; enforce it in `Unit/Tests/`. The Rulebook accrues going-forward.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <subject> <condition> → <expected result>
- **Why:** <the contract this protects>

## Don't — and why

```markdown
### Reverse of an empty list                         ← behavioral header must end in a → result
- Why: an empty input is the base case               ← "Why" must be bold: - **Why:**
- **Note:** also covered by a theory                 ← no second bold-label bullet; use plain prose
```

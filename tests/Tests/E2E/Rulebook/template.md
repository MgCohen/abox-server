# E2E Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each E2E Rule is one whole-flow guarantee, driven end to end through the real composition with a
scripted (non-CLI) provider. Add one when a new flow path needs proving; enforce it in `E2E/Tests/`.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <flow> <given some input> → <observable end state>
- **Why:** <the user-visible behavior this proves>

## Don't — and why

```markdown
### chore commits and pushes the tree                ← behavioral header must end in a → result
- Why: stages and pushes the dirty tree              ← "Why" must be bold: - **Why:**
- **Note:** runs GitChoreFlow                        ← no second bold-label bullet; use plain prose
```

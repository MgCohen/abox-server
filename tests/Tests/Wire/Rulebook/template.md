# Wire Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each Wire Rule is one endpoint contract — routing + serialization + the streaming contract — proven with a
real HttpClient over `WebApplicationFactory<Program>`. Add one per endpoint behavior; enforce it in `Wire/Tests/`.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <method> <route> <given> → <response contract>
- **Why:** <the routing/serialization/streaming guarantee this protects>

## Don't — and why

```markdown
### GET /health returns ok                           ← behavioral header must end in a → result
- Why: the liveness probe must route                 ← "Why" must be bold: - **Why:**
- **Note:** smoke only                               ← no second bold-label bullet; use plain prose
```

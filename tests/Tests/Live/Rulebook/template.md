# Live Rulebook — Rule template

Convention, parity discipline, and how to add a type: [`../../../Harness/README.md`](../../../Harness/README.md).

Each Live Rule is one real-CLI guarantee — a flow or agent against the real `claude`/`codex` CLI and
subscription, gated behind `[LiveFact]` / `RUN_LIVE=1`. Add one as smoke tests convert; enforce it in `Live/Tests/`.
(Well-formed Rules live in `rules.md` — read those for good examples.)

## Template

### <agent/flow> <given a real prompt> → <real-world effect>
- **Why:** <the live behavior no scripted provider can prove>

## Don't — and why

```markdown
### claude edits a file on disk                      ← behavioral header must end in a → result
- Why: only a real agent edits the project           ← "Why" must be bold: - **Why:**
- **Note:** needs RUN_LIVE=1                         ← no second bold-label bullet; use plain prose
```

# Unit Rulebook

Each Unit Rule is one behavioral guarantee about a single type or small cluster tested with local fakes.
Add one as new behavioral tests land; enforce it in `Unit/Tests/`. The Rulebook accrues going-forward.

## Template

### <subject> <condition> → <expected result>
- **Why:** <the contract this protects>

## Criteria

- **one_result:** states exactly one expected result for a single behavior, not several bundled
- **observable_contract:** the result is the type's observable contract (return, throw, state), not an implementation detail
- **why_justifies:** the **Why:** names the contract protected, not a restatement of the header

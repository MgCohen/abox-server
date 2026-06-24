# Docs Rulebook

Each Docs Rule is one guarantee about the repo's structured documents, proven by shelling out to the standalone
doc-engine (`tools/doc-engine`) — ADR 0013, never a reference. Add one when a new document guarantee needs
enforcing; prove it with a `[Rule]` fact in `Docs/Tests/` that runs `docengine` and asserts the outcome.

## Template

### <subject> <invariant about the documents or the catalog>
- **Why:** <the guarantee this protects>

## Criteria

- **one_guarantee:** states exactly one document or catalog guarantee, not several bundled
- **engine_proven:** the guarantee is one the doc-engine (`check`/`validate`) actually enforces
- **why_justifies:** the **Why:** gives the guarantee at stake, not a restatement of the header

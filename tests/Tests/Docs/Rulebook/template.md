---
docType: test-template
testType: docs
---

## Summary
Each Docs Rule is one guarantee about the repo's structured documents, proven by shelling out to the standalone doc-engine (`tools/doc-engine`) — ADR 0013, never a reference. Enforced by a `[Rule]` fact in `Docs/Tests/` that runs `docengine` and asserts the outcome.

## Criteria

### one_guarantee
States exactly one document or catalog guarantee, not several bundled.

### engine_proven
The guarantee is one the doc-engine (`check`/`validate`) actually enforces.

### why_justifies
The Why gives the guarantee at stake, not a restatement of the header.

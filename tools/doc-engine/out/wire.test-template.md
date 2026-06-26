---
docType: test-template
testType: wire
---

## Summary
<!-- id: wire-tmpl-summary -->
Each Wire Rule is one endpoint contract — method + route + given → response — proven with a real HttpClient against the Host over WebApplicationFactory.

## Criteria

### one_contract
<!-- id: one-contract -->
Exactly one endpoint contract (method + route → result), not several bundled.

### observable
<!-- id: observable -->
Asserts observable wire behaviour (status, body shape, SSE), not an implementation detail.

### why_justifies
<!-- id: why-justifies -->
The Why gives the routing/serialization guarantee, not a restatement of the header.

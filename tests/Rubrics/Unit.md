---
docType: rubric
testType: unit
---

## Summary
Each Unit Rule is one behavioural guarantee about a single type or small cluster tested with local fakes. Every test cites a Rule; enforced in each feature's co-located `src/<…>/<Owner>/Tests/Unit/` (`ABox.<Owner>.Tests`).

## Criteria

### one_result
States exactly one expected result for a single behaviour, not several bundled.

### observable_contract
The result is the type's observable contract (return, throw, state), not an implementation detail.

### why_justifies
The Why names the contract protected, not a restatement of the header.

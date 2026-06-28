---
docType: rubric
testType: live
---

## Summary
Each Live Rule is one real-CLI guarantee — a flow or agent against the real `claude`/`codex` CLI and subscription, gated behind `[LiveFact]` / `RUN_LIVE=1`. Enforced in each feature's co-located `src/<…>/<Owner>/Tests/Live/` (`ABox.<Owner>.Tests`).

## Criteria

### one_effect
States exactly one real-world effect of the live run, not several bundled.

### needs_live
The effect genuinely requires the real CLI/subscription — a scripted provider could not prove it.

### why_justifies
The Why names the live behaviour no scripted provider can prove, not a restatement of the header.

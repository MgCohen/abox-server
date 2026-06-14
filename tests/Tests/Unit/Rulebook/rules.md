# Unit Rulebook

Each Rule is one behavioral guarantee about a single type or small cluster tested with local fakes. Convention,
parity discipline, the Rule shape, and the going-forward adoption policy live in
[`../../../Harness/README.md`](../../../Harness/README.md) and `template.md`. This Rulebook starts empty and
grows as new behavioral tests land with their Rule.

---

### JudgeScore with every criterion passing → overallPass and score10 of 10
- **Why:** a clean rubric pass must read as a full pass and a top score, computed in code rather than emitted by the model.

### JudgeScore with a failing criterion → overallPass false
- **Why:** any hard fail gates the verdict regardless of proportion, so a failing criterion can never read as a pass.

### JudgeScore counts indeterminate against the total → lower score and no pass
- **Why:** an un-assessable criterion must lower the score and block a clean pass, never silently masquerade as success.

### JudgeParser given a verdict envelope → one result per criterion by id
- **Why:** the verdict must map one-to-one back to the supplied criteria by id, the contract downstream scoring relies on.

### JudgeParser with a criterion absent from the envelope → marks it indeterminate
- **Why:** a criterion the model skipped is unknown, not passing, so it must surface as indeterminate rather than vanish.

### JudgeParser with no sentinel → marks every criterion indeterminate
- **Why:** without the structured envelope nothing was assessed, and the parser must not invent verdicts from prose.

### JudgePrompt → lists every criterion id and instructs context-first
- **Why:** the judge must see every criterion by id and use the inline context before reading files, for deterministic grading.

### Judge with a provider verdict envelope → scored verdict with per-criterion results
- **Why:** the judge operation must turn raw provider text into a validated, scored verdict so callers consume structure, not prose.

### Judge with a provider fault → every criterion indeterminate
- **Why:** a failed judge run must degrade safely to indeterminate, never fabricate pass or fail from a broken call.

### TestRulebookAdapter given a test path → a JudgeRequest with rubric criteria and labeled context
- **Why:** the adapter is the only test-domain piece; it must normalize a test file into the canonical judge request with the rubric criteria and a labeled context.

### TestRulebookAdapter given a test path → resolves the sibling Rulebook folder
- **Why:** grading must read the correct Rulebook for a test, derived from the test's path rather than hardcoded per call.

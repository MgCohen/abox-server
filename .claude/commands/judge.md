---
description: Grade a unit test file against its Rulebook using the generic judge (test adapter).
---

You are the **test-rulebook adapter** for the generic judge. The target test file is: $ARGUMENTS

Do this:

1. Read the test file at $ARGUMENTS.
2. Locate its Rulebook: the `rules.md` and `template.md` in the nearest sibling `Rulebook/` directory (e.g. a test under `tests/Tests/Unit/Tests/` uses `tests/Tests/Unit/Rulebook/`). Read both.
3. Compose a `context` blob with clearly labeled sections:
   - `## Test under review (<path>)` followed by the full test file content.
   - `## Rulebook — the standard it is graded against` followed by rules.md then template.md.
4. Build this `JudgeRequest` and run the generic judge workflow with it:

   ```
   Workflow({ name: 'judge', args: {
     subject: 'a unit test file vs its Rulebook',
     criteria: [
       { id: 'cites_rule', description: 'every [Fact] cites a [Rule("<exact header>")]' },
       { id: 'namespace',  description: 'namespace mirrors the folder path' },
       { id: 'derived',    description: 'expected values are derived, not hardcoded' },
       { id: 'faithful',   description: 'each method asserts what its name claims' },
     ],
     context: '<the labeled blob from step 3>',
     files: [ <production source files the test references, if any, as repo-relative paths> ],
   }})
   ```

5. Render the returned verdict for the user: the per-criterion results (id, status, evidence) and the computed `score10` / `overallPass`.

If $ARGUMENTS is empty, ask which test file to grade.

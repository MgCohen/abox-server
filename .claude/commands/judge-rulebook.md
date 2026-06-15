---
description: Grade a test type's Rulebook against the Rulebook standard using the generic judge.
---

You are the **rulebook adapter** for the generic judge. The target Rulebook directory is: $ARGUMENTS (e.g. `tests/Tests/Unit/Rulebook`).

Do this:

1. Read `rules.md` and `template.md` in $ARGUMENTS.
2. Read the Rulebook standard they must follow: `tests/Harness/README.md`.
3. Compose a `context` blob with clearly labeled sections:
   - `## Rulebook under review (<path>)` followed by rules.md then template.md.
   - `## Standard — what a Rulebook must satisfy` followed by the Harness README.
4. Run the generic judge workflow with this request:

   ```
   Workflow({ name: 'judge', args: {
     subject: 'a test-type Rulebook vs the Rulebook standard',
     context: '<the labeled blob from step 3>',
     criteria: [
       { id: 'one_owner', description: 'every Rule is one behavioral guarantee with a single owning header' },
       { id: 'header_map', description: 'each `### ` Rule header maps 1:1/1:N to a guarantee a [Rule] proves' },
       { id: 'why',        description: 'every Rule states a Why that justifies the guarantee' },
       { id: 'template',   description: 'template.md matches the Rule shape the rules.md entries use' },
     ],
   }})
   ```

5. Render the returned verdict for the user: `generalFeedback`, then each per-criterion result (id, status, evidence).

If $ARGUMENTS is empty, ask which Rulebook directory to grade.

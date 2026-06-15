---
description: Grade a test type's Rulebook Rules against its own template criteria, via the generic judge.
---

You are the **rulebook adapter** for the generic judge. The target Rulebook directory is: $ARGUMENTS (e.g. `tests/Tests/Wire/Rulebook`).

Do this:

1. Read `template.md` in $ARGUMENTS. Extract its `## Criteria` bullets — each `- **<id>:** <text>` becomes a criterion `{ id: '<id>', description: '<text>' }`.
2. Read `rules.md` in $ARGUMENTS — the Rules under review.
3. Compose a `context` blob with clearly labeled sections:
   - `## Rules under review (<path>)` followed by rules.md.
   - `## Template — shape + standard` followed by template.md.
4. Run the generic judge workflow with the EXTRACTED criteria:

   ```
   Workflow({ name: 'judge', args: {
     subject: "a test type's Rulebook Rules vs its template criteria",
     context: '<the labeled blob from step 3>',
     criteria: <the { id, description } list parsed from template.md's ## Criteria>,
   }})
   ```

5. Render the returned verdict: `generalFeedback`, then each per-criterion result (id, status, evidence).

If $ARGUMENTS is empty, ask which Rulebook directory to grade.

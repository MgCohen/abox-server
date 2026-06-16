---
description: Grade a test file's body craft against the authoring criteria, via the generic judge.
---

You are the **authoring adapter** for the generic judge. The target test file is: $ARGUMENTS

Do this:

1. Read the test file at $ARGUMENTS.
2. Read `tests/Harness/authoring.md`. Extract its `## Criteria` bullets — each `- **<id>:** <text>` becomes a criterion `{ id: '<id>', description: '<text>' }`.
3. Compose a `context` blob with clearly labeled sections:
   - `## Test under review (<path>)` followed by the full test file content.
   - `## Authoring standard — the craft rules it is graded against` followed by authoring.md.
4. Run the generic judge workflow with the EXTRACTED criteria:

   ```
   Workflow({ name: 'judge', args: {
     subject: 'a test file body vs the authoring criteria (good-test craft)',
     context: '<the labeled blob from step 3>',
     files: [ <support/host files the test relies on (e.g. its WireApp / fakes), as repo-relative paths> ],
     criteria: <the { id, description } list parsed from authoring.md's ## Criteria>,
   }})
   ```

5. Render the returned verdict: `generalFeedback`, then each per-criterion result (id, status, evidence).

If $ARGUMENTS is empty, ask which test file to grade.

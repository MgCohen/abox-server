---
docType: guide
---

## Summary
How to extend the doc-engine catalog: add a new block, compose it into a doc type, wire that type's
on-change reactions, and author a conforming instance — the changes you make when the vocabulary or its
reactions need to grow.

## Procedures
### Adding a block
**Context:** a block is a reusable content unit (`blocks/<type>.yaml`) that doc types compose.
##### 1. Pick the type name and content shape
Decide whether the block is a singleton or a `collection`, and how it carries content — typed
`attrs`, closed-set `labels`, or free `body`.
##### 2. Write the block definition
Add `blocks/<type>.yaml` with `type`, `description`, a `rubric`, and the content fields, listing
present fields in the canonical order from `kinds/block.yaml`.
##### 3. Verify the definition conforms
Run `docengine check`; fix any field-order, kind, or constraint violation it reports.

**Outcome:** `docengine check` passes with no violation naming the new block, so the block type now
exists in the catalog and is ready to be composed into a doc type.

---

### Adding a doc type
**Context:** a doc type (`doctypes/<name>.yaml`) names the blocks a document composes and which are required.
##### 1. Ensure the blocks exist
If a block the doc type needs is missing, add it first via "Adding a block".
##### 2. Write the doc type definition
Add `doctypes/<name>.yaml` with `docType`, `description`, the `blocks` list, the `required` subset, a
`rubric`, and optionally `reviewers`/`checks` (see "Wiring a doc type's on-change reactions").
##### 3. Verify the catalog conforms
Run `docengine check` to confirm `required` is a subset of `blocks` and every field conforms.

**Outcome:** `docengine catalog` lists the new doc type with its blocks, so an instance can now declare
it in its front matter.

---

### Wiring a doc type's on-change reactions
**Context:** when an instance changes, the engine runs a pipeline off that change — `docengine validate`
(generic structure) then the doc type's `checks:` (deterministic scripts) both **block** on failure,
then its `reviewers:` (fresh agents) **advise** — all fed back to the session. A doc type opts into the
reactions it wants; each is a flat list.
##### 1. Add reviewers that grade a change
In `doctypes/<name>.yaml`, add `reviewers:` — agent names spawned fresh (`claude -p --agent <name>`,
hook-free) to review a changed instance and feed notes back. Every doc type is graded by `judge`
against its `rubric:` by default; list extra agents to add them (a guide adds `walk-guide`), or an
empty list to opt out. Reviewers advise — they never block.
##### 2. Add a deterministic check that blocks
Add `checks:` — engine-relative scripts (`scripts/<name>.sh`), each handed the changed file's path and
exiting non-zero with a message to **block** the turn. Use for cheap, objective rules the structural
validator can't express; reach for a reviewer, not a check, when the call needs judgement.
##### 3. Confirm the reactions resolve
Run `docengine reviewers <file>` and `docengine checks <file>` on an instance of the doc type.

**Outcome:** `docengine reviewers <file>` lists the agents (`judge` by default) and `docengine checks
<file>` lists the scripts (none by default), so a change to an instance of this doc type is validated,
checked, and reviewed — deterministic failures block, reviewer notes advise.

---

### Authoring an instance
**Context:** an instance is a `.md` file whose front matter declares a `docType` the engine validates it against.
##### 1. Start from the doc type's blocks
Run `docengine catalog <docType>` to see the blocks available, then draft `## ` sections for the ones you need.
##### 2. Fill in the required blocks
Write each required block, leading every body with prose so a `word:` first line is not misread as an attr.
##### 3.a Fix and revalidate
- **Condition:** validation fails
Read each reported violation, correct the instance, and run `docengine validate <file>` again.
##### 3.b Commit the instance
- **Condition:** validation passes
The central Docs test discovers it by its front matter and validates it on every run.

**Outcome:** `docengine validate <file>` prints `PASS — conforms to the catalog.`, so a structured
document now exists that the catalog validates and CI guards.

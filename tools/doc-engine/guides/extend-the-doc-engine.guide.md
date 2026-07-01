---
docType: guide
---

## Summary
How to extend the doc-engine catalog: add a new block, compose it into a doc type, wire that type's
on-change reactions, and author a conforming instance. Each action is independent — start from
whichever one matches the change you are making. Every action ends in a `docengine` command you can
run to confirm it worked.

## Actions
### Add a block
- **Context:** a block is a reusable content unit (`blocks/<type>.yaml`) that doc types compose.
#### Pick the type name and content shape
<!-- id: 1 -->
Decide whether the block is a singleton or a `collection`, and how it carries content — typed
`attrs`, closed-set `labels`, or free `body`.
#### Write the block definition
<!-- id: 2 -->
Add `blocks/<type>.yaml` with `type`, `description`, a `rubric`, and the content fields, listing
present fields in the canonical order from `kinds/block.yaml`.
#### Verify the definition conforms
<!-- id: 3 -->
Run `docengine check`; fix any field-order, kind, or constraint violation it reports.
- **Validation:** `docengine check` passes and lists no violation naming the new block.
- **Outcome:** a new block type exists in the catalog and is ready to be composed into a doc type.

### Add a doc type
- **Context:** a doc type (`doctypes/<name>.yaml`) names the blocks a document composes and which are required.
#### Ensure the blocks exist
<!-- id: 1 -->
If a block the doc type needs is missing, add it first via "Add a block".
#### Write the doc type definition
<!-- id: 2 -->
Add `doctypes/<name>.yaml` with `docType`, `description`, the `blocks` list, the `required` subset, a
`rubric` (the criteria the `judge` grades an instance against), and optionally `reviewers`/`checks`
(see "Wire a doc type's on-change reactions").
#### Verify the catalog conforms
<!-- id: 3 -->
Run `docengine check` to confirm `required` is a subset of `blocks` and every field conforms.
- **Validation:** `docengine catalog` lists the new doc type with its blocks.
- **Outcome:** a new doc type exists that an instance can declare in its front matter.

### Wire a doc type's on-change reactions
- **Context:** when an instance changes, the engine runs a pipeline off that change — `docengine
  validate` (generic structure) then the doc type's `checks:` (deterministic scripts) both **block**
  on failure and feed the reason back to the session; then its `reviewers:` (fresh agents) **advise**,
  their notes fed back too. A doc type opts into the reactions it wants; each is a flat list.
#### Add reviewers (fresh agents that grade a change)
<!-- id: 1 -->
In `doctypes/<name>.yaml`, add `reviewers:` — agent names spawned fresh (`claude -p --agent <name>`,
hook-free) to review a changed instance and feed notes back. Every doc type is graded by `judge`
against its `rubric:` by default; list extra agents to add them (a guide adds `walk-guide`), or an
empty list to opt out. Reviewers advise — they never block.
#### (Optional) add a deterministic check that blocks
<!-- id: 2 -->
Add `checks:` — engine-relative scripts (`scripts/<name>.sh`), each handed the changed file's path and
exiting non-zero with a message to **block** the turn. Use for cheap, objective rules the structural
validator can't express (e.g. a size cap); reach for a reviewer, not a check, when it needs judgement.
#### Confirm the reactions resolve
<!-- id: 3 -->
Run `docengine reviewers <file>` and `docengine checks <file>` on an instance of the doc type.
- **Validation:** `docengine reviewers <file>` lists the agents (`judge` by default) and `docengine
  checks <file>` lists the scripts (none by default).
- **Outcome:** a change to an instance of this doc type is validated, checked, and reviewed —
  deterministic failures block, reviewer notes advise, all fed back to the session.

### Author an instance
- **Context:** an instance is a `.md` file whose front matter declares a `docType` the engine validates it against.
#### Start from the doc type's blocks
<!-- id: 1 -->
Run `docengine catalog <docType>` to see the blocks available, then draft `## ` sections for the ones you need.
#### Fill in the required blocks
<!-- id: 2 -->
#### Validate, then fix and revalidate
<!-- id: 3.a -->
- **Condition:** validation fails
Read each reported violation, correct the instance, and run `docengine validate <file>` again.
#### Commit the instance
<!-- id: 3.b -->
- **Condition:** validation passes
The central Docs test discovers it by its front matter and validates it on every run.
- **Validation:** `docengine validate <file>` prints `PASS — conforms to the catalog.`
- **Outcome:** a structured document exists that the catalog validates and CI guards.

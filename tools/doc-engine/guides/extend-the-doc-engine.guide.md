---
docType: guide
onChange: .claude/agents/walk-guide.md
---

## Summary
How to extend the doc-engine catalog: add a new block, compose it into a doc type, and author a
conforming instance. Each procedure is independent — start from whichever one matches the change you
are making. Every procedure ends in a `docengine` command you can run to confirm it worked.

## Procedures
### Adding a block
- **Context:** a block is a reusable content unit (`blocks/<type>.yaml`) that doc types compose.
#### 1. Pick the type name and content shape
Decide whether the block is a singleton or a `collection`, and how it carries content — typed
`attrs`, closed-set `labels`, or free `body`.
#### 2. Write the block definition
Add `blocks/<type>.yaml` with `type`, `description`, a `rubric`, and the content fields, listing
present fields in the canonical order from `kinds/block.yaml`.
#### 3. Verify the definition conforms
Run `docengine check`; fix any field-order, kind, or constraint violation it reports.
- **Validation:** `docengine check` passes and lists no violation naming the new block.
- **Outcome:** a new block type exists in the catalog and is ready to be composed into a doc type.

### Adding a doc type
- **Context:** a doc type (`doctypes/<name>.yaml`) names the blocks a document composes and which are required.
#### 1. Ensure the blocks exist
If a block the doc type needs is missing, add it first via "Adding a block".
#### 2. Write the doc type definition
Add `doctypes/<name>.yaml` with `docType`, `description`, the `blocks` list, the `required` subset, and a `rubric`.
#### 3. Verify the catalog conforms
Run `docengine check` to confirm `required` is a subset of `blocks` and every field conforms.
- **Validation:** `docengine catalog` lists the new doc type with its blocks.
- **Outcome:** a new doc type exists that an instance can declare in its front matter.

### Authoring an instance
- **Context:** an instance is a `.md` file whose front matter declares a `docType` the engine validates it against.
#### 1. Start from the doc type's blocks
Run `docengine catalog <docType>` to see the blocks available, then draft `## ` sections for the ones you need.
#### 2. Fill in the required blocks
Write each required block, leading every body with prose so a `word:` first line is not misread as an attr.
#### 3.a Fix and revalidate
- **Condition:** validation fails
Read each reported violation, correct the instance, and run `docengine validate <file>` again.
#### 3.b Commit the instance
- **Condition:** validation passes
The central Docs test discovers it by its front matter and validates it on every run.
- **Validation:** `docengine validate <file>` prints `PASS — conforms to the catalog.`
- **Outcome:** a structured document exists that the catalog validates and CI guards.

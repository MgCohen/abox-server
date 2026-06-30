---
docType: guide
---

## Summary
How to extend the doc-engine catalog: add a new block, compose it into a doc type, and author a
conforming instance. Each action is independent — start from whichever one matches the change you
are making. Every action ends in a `docengine` command you can run to confirm it worked.

## Actions
### Add a block
- **Context:** a block is a reusable content unit (`blocks/<type>.yaml`) that doc types compose.
- **Validation:** `docengine check` passes and lists no violation naming the new block.
- **Outcome:** a new block type exists in the catalog and is ready to be composed into a doc type.
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

### Add a doc type
- **Context:** a doc type (`doctypes/<name>.yaml`) names the blocks a document composes and which are required.
- **Validation:** `docengine catalog` lists the new doc type with its blocks.
- **Outcome:** a new doc type exists that an instance can declare in its front matter.
#### Ensure the blocks exist
<!-- id: 1 -->
If a block the doc type needs is missing, add it first via "Add a block".
#### Write the doc type definition
<!-- id: 2 -->
Add `doctypes/<name>.yaml` with `docType`, `description`, the `blocks` list, the `required` subset, and a `rubric`.
#### Verify the catalog conforms
<!-- id: 3 -->
Run `docengine check` to confirm `required` is a subset of `blocks` and every field conforms.

### Author an instance
- **Context:** an instance is a `.md` file whose front matter declares a `docType` the engine validates it against.
- **Validation:** `docengine validate <file>` prints `PASS — conforms to the catalog.`
- **Outcome:** a structured document exists that the catalog validates and CI guards.
#### Start from the doc type's blocks
<!-- id: 1 -->
Run `docengine catalog <docType>` to see the blocks available, then draft `## ` sections for the ones you need.
#### Fill in the required blocks
<!-- id: 2 -->
#### Validate, then fix and revalidate
<!-- id: 3.a -->
condition: validation fails
Read each reported violation, correct the instance, and run `docengine validate <file>` again.
#### Commit the instance
<!-- id: 3.b -->
condition: validation passes
The central Docs test discovers it by its front matter and validates it on every run.

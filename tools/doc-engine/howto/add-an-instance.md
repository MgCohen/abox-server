# HOWTO: add a new instance (a document)

An **instance** is an actual document — a Markdown file in its **home folder** in the
repo (a Rulebook under `tests/**/Rulebook.md`, a plan under `PLANS/`, …) whose structure
conforms to a doc type's catalog of blocks. This is the artifact you produce; the
engine checks it with `validate`, in place. There is no global output directory.

You will: pick a doc type, write the Markdown with front matter + blocks, and run
`validate` until it passes.

## 1. Pick a doc type

Run all commands from the **tool root** — the directory that holds the project file
(in this repo, `tools/doc-engine/`); `--project .` means "the project here". List
the doc types:

```bash
dotnet run --project . -- catalog
```

Pick the doc type whose `description` fits. Then list the blocks it allows:

```bash
dotnet run --project . -- catalog feature-plan
```

Read `doctypes/<docType>.yaml` for its `blocks` (allowed), `required` (must appear),
and `attrs` (front-matter fields).

## 2. Write the file in its home folder, `<home>/<slug>.<suffix>.md`

Put the file where that kind of document belongs in the repo (e.g. a plan under
`PLANS/`), choosing `<suffix>` to match the doc type: `feature-plan` docs are
`<slug>.plan.md`, `research` docs are `<slug>.research.md`. (The suffix is a short
label for the doc type, not the literal doc-type name.)

### Front matter

A leading `---` YAML block. It must carry `docType`, plus any `attrs` the doc type
declares (e.g. `status`):

```md
---
docType: feature-plan
status: draft
---
```

**Optional `onChange`.** A path to a deterministic *script* handler (under `scripts/`,
`.claude/agents`, or `.claude/hooks`; no `..`) run as a side-effect when this instance
changes — e.g. to regenerate a derived file. It is validated (the path must resolve
inside those roots) but never executed by `validate`. Grading and review are **not** set
here — those come from the doc type's `reviewers:`/`checks:` (see "Wire a doc type's
on-change reactions" in `guides/extend-the-doc-engine.guide.md`).

### Blocks

First tell singletons from collections: open `blocks/<type>.yaml` — if it has
`collection: true` it is a collection (its `group:` value is the header); otherwise
it is a singleton.

- **Singleton block** → a `## <Type>` header, the block type written Title-Case with
  spaces: `summary` → `## Summary`, `expected-result` → `## Expected Result`.
- **Collection block** → a `## <Group>` header (the block's `group:` value), then one
  or more `### <title>` members. The member's type is inherited from the group.
- Under each header (or sub-header), put:
  - any scalar attrs as `key: value` lines — enum or free string alike
    (e.g. `status: doing`, or `source: Microsoft ConPTY docs`);
  - then a blank line and the Markdown body.
  - optionally a stable handle comment `<!-- id: <slug> -->` — only when something
    needs to reference this block across edits; unique within the document. Most
    blocks omit it.

A complete minimal `feature-plan` (its required blocks are `summary`, `phase`,
`verification`):

```md
---
docType: feature-plan
status: draft
---

## Summary

One paragraph: the objective and what "done" means.

## Phases
### Wire the adapter
status: todo

**Goal.** What this step delivers. **Done when.** The bar.

### Prove it end to end
status: todo

**Goal.** ... **Done when.** ...

## Verification

The concrete checks that prove "done" — build, tests, one behaviour run.
```

Rules the validator enforces: only blocks in the doc type's catalog appear; every
`required` block is present; enum attrs hold an allowed value; a required body is
non-empty; a collection group has at least one member; any `<!-- id -->` handles
present are unique.

## 3. Verify

```bash
dotnet run --project . -- validate <home>/<slug>.<suffix>.md
```

Expect `PASS — conforms to the catalog.` Each violation names the offending block
(`#N (<title>)`, or `#N (id=…)` when it carries a handle) and the problem; fix and re-run.

## 4. (Optional) generate the index

```bash
dotnet run --project . -- outline <home>/<slug>.<suffix>.md --write
```

This injects a generated outline/status board between `INDEX` markers — never
hand-edit that region. The document still validates afterward; re-run `validate` to
confirm if you like.

---
status: accepted
date: 2026-06-30
supersedes:
amends: 0016
---

# ADR 0017 — A guide's how-to unit is a `procedure`; a step's id is its heading ordinal

## Context

ADR 0016 stood up nested block composition and authored the `guide` doc type: a
`guide` holds independent `action`s, each composing ordered `step`s. To keep the
enforced step id out of the rendered prose, 0016 introduced a generic `hidden` attr
param — the id was authored as an invisible `<!-- id: N -->` comment under each
`#### ` step heading.

Reading real guides raw (a plain markdown preview, not a custom renderer) exposed two
problems with that shape:

- **The step is unidentifiable.** With the id invisible, a `#### step` heading renders
  with the same visual weight as the `- **Context:** / **Validation:** / **Outcome:**`
  label bullets around it. A reader cannot tell a step from a label, and the step's
  position in the sequence — its whole point — is hidden in a comment. "We made them
  invisible" was the wrong call: the ordinal *is* the reader's handle on the step.
- **`action` mis-names the unit.** Each how-to's title is the activity it performs, and
  reads best as a gerund noun-phrase ("Adding a hook"), not an imperative ("Add a hook")
  — imperatives are what the *steps* are. A unit titled with a gerund is not an "action".

Custom renderers are the eventual display path, but a guide must also read well as raw
markdown. The fix is structural, in the catalog and parser, not cosmetic.

## Decision

**A guide composes `procedure`s, and a step's id is the visible leading ordinal of its
heading.** Concretely, amending 0016:

- **`action` → `procedure`.** The how-to block is `procedure` (group `## Procedures`);
  the doctype requires `[summary, procedure]`. The unit is named for the activity its
  title states.
- **A step's id lives in the heading.** A step is authored `#### N. <imperative>`
  (e.g. `#### 1. Choose the event kind`); the leading ordinal `N` / `N.M` / `N.a` is the
  step's `id`. The parser splits the heading into (id, title): the first whitespace-delimited
  token, trailing `.`/`)` stripped, is the id when it begins with a digit; the rest is the
  title. All three id guarantees from 0016 hold unchanged — `pattern` grammar, required,
  unique within the procedure — and the validator still owns the grammar check, so an
  off-grammar ordinal (`1.X`) fails `pattern` exactly as before. Putting the ordinal in the
  heading makes it **mandatory structure**: a `#### ` step with no leading number leaves the
  required `id` unset and fails `validate`.
- **`inHeading` replaces `hidden`.** The attr placement param introduced by 0016 to render
  an attr as an invisible comment is retired and replaced by `inHeading: true`, which reads
  the attr from the member's heading ordinal. `hidden` had exactly one consumer (`step.id`);
  with the id now visible it has none, so the comment-form reader (`<!-- key: value -->`) and
  the `FieldSpec.Hidden` flag are removed rather than left as a dead capability. `inHeading`
  is the one non-default attr placement the engine carries, with a real consumer.
- **A `title-gerund` rubric on `procedure`.** The advisory (judge-graded) rubric gains
  `title-gerund`: the title names the activity as a gerund phrase, not an imperative. Steps
  keep their `imperative` rubric. This is guidance, not a `[det]` check.

**Unchanged from 0016:** one level of nesting (`procedure → step`), label routing by
declaration, the `pattern` attr param, the `composes` mechanism and its `references`
constraint, and that the guide's prose is read (not executed) by a human or agent.

## Consequences

- A guide reads correctly raw: numbered steps are visibly distinct from the bracketing
  Context/Validation/Outcome labels, and each step's order is on the page, not in a comment.
- The engine carries one attr-placement param (`inHeading`), down from the momentarily-dead
  `hidden`; the "name nothing special" property holds — the parser splits a heading ordinal
  generically, the validator owns the grammar via `pattern`.
- The id grammar (`N`, `N.M`, `N.a`) is unchanged, so sub-steps and branches still read in the
  heading (`#### 3.a Publish via CLI`) and cross-references resolve against the same handle.
- `tools/doc-engine/{doctypes,blocks}/**` and this ADR remain protected; the engine `*.cs` is
  not. The shared `src/Api/doc-catalog.json` export is regenerated for the rename and the
  `inHeading` param.

## Confirmation

`tools/doc-engine/Tests/Unit` (the co-located engine suite) pins the behaviour: a guide whose
`procedure`s nest `#### N. step`s validates clean; an off-grammar ordinal (`1.X`) fails
`pattern`; duplicate ordinals within one procedure fail; a procedure with zero steps fails. The
central `Docs` test validates the shipped guide instance and re-exports the catalog, so a stale
`doc-catalog.json` or a drifted instance fails CI.

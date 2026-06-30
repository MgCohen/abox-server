---
status: accepted
date: 2026-06-30
supersedes:
amends:
---

# ADR 0016 — Nested block composition: a block may compose child blocks

## Context

The doc-engine (`tools/doc-engine/`) was strictly two structural levels: a doc type
composes blocks, and a **collection** block (`collection: true` + `group:`) renders as
`## Group` → repeatable `### member`. A block could not contain sub-blocks, and the
parser knew only H2/H3. The id handle was a hardcoded special case: a regex read
`<!-- id: x -->` into a dedicated `ParsedBlock.Id` field, and attrs were `string`/`enum`
only — no open-ended value validator.

The `guide` doc type (PLANS/guide-doctype.md) needs a third level with *real* structural
enforcement: an `action` is a how-to; each holds ordered `step`s whose **id** is enforced
to a grammar yet must stay invisible in the prose. Two-level structure cannot express
"an action requires ≥1 step, each step's id matches `N`/`N.M`/`N.a` and is unique within
the action." `NOTES.md` had already flagged generalizing *instance composition* as deferred
"until a real consumer exists" — `step` is that consumer.

The risk in adding a third level is over-building: arbitrary-depth trees, multi-type
disambiguation, and a renderer none of which has a consumer.

## Decision

**A collection block may declare `composes: [<child-type>…]`; the engine parses and
validates one nested level, and the attr system gains two reusable parameters
(`pattern`, `hidden`).** Concretely:

- **`composes` on a block** (`kinds/block.yaml`, structure cluster, after `group`). Two
  constraints, both reusing the constraint registry: `requires_when composes ⇒ collection`
  (only a collection nests children) and a new generic `references` primitive
  (`field: composes, by: type`) so every composed type names a real block definition —
  caught at `check`, not silently at `validate`.
- **Recursive parse/validate.** The parser reads `#### member` as a child of the enclosing
  `###` member when that member's block `composes` a type; `ParsedBlock` gains `Children`.
  Validation recurses: a child must be in the parent's `composes`, and a block that composes
  a type **requires ≥1** such child (a per-parent rule, distinct from the existing
  group-emptiness rule). A `####` under a non-composing block stays body text — backward
  compatible.
- **Labels route by declaration.** A `- **Label:**` bullet attaches to whichever block in the
  nesting chain *declares* it (closed-set), independent of position — so a parent's labels may
  bracket its children (an `action`'s Context before its steps, Validation/Outcome after), while a
  child's own label (a step's Condition) stays on the child.
- **`pattern` attr param** — a regex the value must match, the open-ended sibling of `enum`,
  run by `DocValidator` exactly where the enum check runs.
- **`hidden` attr param** — the attr is authored as `<!-- key: value -->`. This **generalizes
  the hardcoded id reader**: the special-cased `<!-- id: x -->` regex and the `ParsedBlock.Id`
  field are retired; `id` becomes an ordinary `hidden` + `pattern` + `required` attr, and the
  parser recognizes comment-form attrs only for declared-`hidden` keys. Duplicate-id detection
  moves to the `id` attr, scoped to a sibling set (so two actions may each have a step `1`).

**Bounded to one level.** Only `action` declares `composes`; `step` composes nothing, so depth
caps at one (`action → step`) and the parser is built/tested for that one level. A **sub-step**
(`2.1`) is a flat `#### step` with a dotted id — the "belongs to 2" relationship lives in the id
*grammar* (`pattern`), not in a nested block. The `composes` mechanism stays general; we do not
enforce or build beyond the level with a real consumer (YAGNI), and multi-type child
disambiguation is left until a second consumer needs it.

## Consequences

- The engine is now three levels where a block opts in, but no deeper; the meta-model gains one
  field and one constraint primitive (`references`), both generic and reusable (e.g. a future
  doctype `blocks` referential check). No kind is named in code — the "adding a kind is adding
  data" property holds.
- `id` is no longer special-cased; one fewer hardcoded mechanism. A `hidden` attr is read from
  the comment form, keeping enforced metadata (the step id) out of the rendered prose.
- The `guide` doc type can be authored: independent `action`s, each with Context/Validation/Outcome
  labels and ordered, id-enforced `step`s — all structure deterministically validated, while the
  prose itself is read (not executed) by a human or agent.
- `kinds/`, `blocks/`, `doctypes/`, and `_schema/` remain protected; the engine `*.cs` is not.
  The shared `src/Api/doc-catalog.json` export is regenerated when the catalog changes.

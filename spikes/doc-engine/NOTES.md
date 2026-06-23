# Spike findings — doc-engine

What the spike proved, the decisions taken, and what's still punted.

## Proved

- **Data-defined catalogs work.** 7 block schemas + 1 doc-type catalog (pure
  YAML, no code) drive validation of a real distilled plan.
- **Quality rules become enforcement, not rubric.** status vocabulary is an
  `enum`; required presence is a `required:` list; ids are unique. The validator
  caught a bad enum, an unknown attr, a duplicate id, and a missing required
  block when fed a broken doc.
- **Tiered rubrics fit selection.** `short` is the one-liner for a pick-matrix;
  `usage` is read after a block is chosen; `example` is a curated static sample
  (not pulled from un-approved live docs).
- **Derived views are free.** Because the engine parses blocks, `outline.py`
  generates an index + phase status board with no authoring cost — the
  plain-markdown answer to "a renderer would show a nicer overview".
- **Readable raw.** Type-first headers + an `<!-- id -->` comment keep the file
  legible without a renderer (confirmed by a cold-read sub-agent).
- **Semantic judge organized.** `criteria/feature-plan.yaml` (selection + quality
  rubrics) runs via the repo's generic judge (PLANS/generic-judge.md). First run:
  9/9 pass, and it caught a silently-dropped precondition (the Box abort/discard
  mechanic) that the structural validator cannot see. Judge = soft/semantic;
  validator = hard/structural; they are different layers.
- **Grouping works.** Collection types render nested (`## Decisions` → `### …`);
  the validator inherits a member's type from its group, enforces attrs/enums on
  members, and flags a group whose type has no members; outline shows a group
  column + phase status board.
- **Selector works, end to end.** A sub-agent following `selector.md` distilled a
  *fresh* dump (`PLANS/claude-stop-hook-plan.md`) into a conformant instance —
  first-try validate PASS. The judge then caught a real coverage gap (the dump's
  "Failure modes & fallbacks" was dropped); the selector revised, folding the
  guards into the relevant phases/verification → PASS. generate → validate → judge
  → revise is closed.
- **Floor enforcement on the definitions.** `_schema/{block,doctype}.schema.yaml`
  + `check_schema.py` validate every block and doc-type *definition* (required
  fields, kinds, the type vocabulary, `collection ⇒ group`, `required ⊆ blocks`).
  The whole stack is structured: meta-schema → definitions → instance.

## Decisions taken (from the cold-read)

| Topic | Decision |
|---|---|
| header order | type-first (`## Phase - Title`) — the type column is the scan key |
| ids | global numeric, de-emphasised into `<!-- id: N -->` (agent-oriented) |
| doc-type rules | catalog + `required` only; no min/max/position |
| scope `kind` | dropped — the title ("In/Out of scope") already carries it |
| done-but-qualified | `caveat:` attr instead of contradicting prose |
| overview | engine-generated index between `INDEX` markers |
| prose | summary/context earn full sentences; bold for labels, not emphasis |
| grouping | nested source — collection types (`collection:true` + `group:`) as `## Group` → `### member`; singletons stay top-level; id stays in its comment |
| examples | per-block `example` dropped; one judge-validated `exemplar` (`out/git-feature.plan.md`) on the doctype instead — matches visual-plan, kills drift |
| index | compact grouped list (sections + member titles), not a matrix |
| field syntax | bare type is shorthand (`body: markdown`, `lean: string`); object form only for extras (`{ enum: [...], default: ... }`). body required by default, attrs optional by default |
| type vocabulary | only `markdown`, `string`, `enum` are in use; `bool`/`ref`/`list` deferred until a block needs one |
| naming | the one-liner is `description` (was `short`) on blocks + doc-types, feeding the decision matrix (`catalog.py`); authoring guidance is `rubric` — a checkable one-liner list on both; doc `title` dropped (the filename is the name) |
| doc rubric | a list of binary, checkable one-liners (pairs with the judge), not a paragraph |
| exemplar | dropped — rely on `short` + `rubric` + the judge; re-add a doctype `exemplar` only if selector output suffers |
| doc front matter | a visible leading `---` YAML block carries doc-level attrs (e.g. `status: draft`), declared in the doctype `attrs` and validated — also the home for a future single-block doc |
| how reference does it | visual-plan keeps schema in a runtime registry (`get-plan-blocks`), when-to-use one-liners in prose, one doc-level exemplar — never per-block examples |

## Still punted (decide before promoting out of spike)

1. **Index freshness.** The injected index can go stale. The engine should
   regenerate + check it (a staleness guard, like the repo's other guards).
2. **Structured list fields.** Everything is `scalar attrs + markdown body`. No
   typed lists (`key-files` as `[{path, note}]`) yet — add only when a consumer
   needs machine-readable lists.
3. **Cross-references.** Cold-read wanted phases to point at the decision they
   implement. A `refs:` attr (ids) is a candidate; YAGNI until needed.
4. **Meta-schema.** `validate.py` hard-codes the field vocabulary. A meta-schema
   for `blocks/*.yaml` + `doctypes/*.yaml` would catch a typo in a definition on
   load (the type-checker equivalent lost by going to YAML).
5. **The selector.** This spike hand-distilled the dump. The real engine needs
   the author prompt: dump + catalog → conformant blocks, then run `validate.py`
   as its own gate and self-correct on failure.
6. **A second doc type** (ADR, research-note) to prove the engine is generic,
   not feature-plan-shaped.
7. **Ad-hoc meta-groups.** Type grouping is done (collection → `## Group`).
   Cross-type meta-grouping (tag an arbitrary sequence for the renderer via a
   `group` attr, id kept separate) is noted but unbuilt — YAGNI until a real need.
   Also a known edge: an empty group is only flagged when its *type* has zero
   members anywhere (a duplicate group header is not).
8. **The selector** — built (`selector.md`) and proven on two dumps. Now an agent
   procedure; folding it into a repo command/skill is the productionisation step.
9. **A `risks` / `failure-modes` block?** Both real dumps had operational
   fallback/failure content with no clean home (folded into phases). If a second
   doc type wants it too, add the block — the first concrete catalog-growth signal.

## How this lands in the repo (when real)

- Generic engine (registry, parser, validator, outline) → `Core`.
- Validator + index-freshness run as Structure-Rulebook guards in CI: a
  non-conforming or stale doc fails the build.
- `blocks/` + `doctypes/` are data; adding a doc type is a YAML change.

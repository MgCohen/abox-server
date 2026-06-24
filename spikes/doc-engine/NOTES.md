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
- **Semantic judge organized.** The doc-type `rubric` (binary one-liners) IS the
  judge's criteria — no separate criteria file; the judge marks each line
  pass/fail via the repo's generic judge (PLANS/generic-judge.md). It caught a
  silently-dropped precondition the structural validator cannot see. Judge =
  soft/semantic; validator = hard/structural; different layers.
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
- **Floor enforcement on the definitions.** `check_schema.py` validates every
  block and doc-type *definition* (required fields, kinds, the type vocabulary,
  `collection ⇒ group`, `required ⊆ blocks`). The whole stack is structured:
  meta-schema → kinds → definitions → instance.
- **Collapsed to one self-describing meta-schema.** `block` and `doctype` stopped
  being special-cased in code: each is now a data file in `kinds/` declaring its
  `fields` + cross-field `constraints` (a 2-primitive registry — `requires_when`,
  `subset` — replaces the hard-coded semantic rules). One `_schema/kind.schema.yaml`
  says what a kind file is and is *itself a kind*, so it conforms to itself and the
  regress stops. `check_schema.py` names no kind (grep-clean); a new enforced
  structure is a new `kinds/*.yaml`, zero engine change. Behaviour unchanged — same
  PASS on the 12 blocks / 2 doc-types / 3 instances.
- **Second doc type proves genericity.** `research` (question / quotation /
  expected-result / analysis / outcome) reuses `summary` + `open-question` and adds
  research-specific blocks — same engine, no code change. The wired `create-doc`
  agent, handed a research dump with *no type named*, inferred `research` via the
  decision matrix and validated PASS. Doc types and blocks are pure data.
  (NB: `feature-plan` and `research` are spike doc types to exercise the tech —
  the real plan/blocks come later.)

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
| field syntax | one canonical form, **no shorthand** — agent-first, so uniform structure beats terseness. Mappings are block notation (`type:`/`enum:` on their own lines); scalar lists stay flow (`[draft, approved]`). The bare-string form (`body: markdown`) is rejected with an actionable error. `required` still defaults (body required, attrs optional) — omitting an optional is not shorthand |
| kinds (meta-model) | `block`/`doctype` are not special-cased — each is a `kinds/*.yaml` declaring `fields` + `constraints`; one self-describing `_schema/kind.schema.yaml` is the floor. Adding a kind = adding data. Generalising *instance composition* (a kind's `medium`/`composes`) is deferred — it needs a 3rd kind with a different instance shape, and we have none |
| type vocabulary | only `markdown`, `string`, `enum` are in use; `bool`/`ref`/`list` deferred until a block needs one |
| naming | the one-liner is `description` (was `short`) on blocks + doc-types, feeding the decision matrix (`catalog.py`); authoring guidance is `rubric` — a checkable one-liner list on both; doc `title` dropped (the filename is the name) |
| rubric = criteria | ONE `rubric` per doc type serves both authoring (selector) and grading (judge marks each pass/fail). `criteria/` merged in and removed — no description/howToCheck ceremony, the judge infers how. Shape is an `id: rule` map **everywhere** (all blocks + doc-types, `kind: strmap`): the id is the stable handle the judge echoes |
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
4. ~~**Meta-schema.**~~ — **resolved**: `_schema/kind.schema.yaml` +
   `check_schema.py` validate every definition, and the meta-schema is one
   self-describing kind (`kinds/*.yaml`). A typo in a definition fails on load.
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
10. ~~Per-doc-type judge criteria~~ — **resolved**: criteria merged into each
    doc-type's `rubric` (binary one-liners), used by both the selector and the judge;
    `criteria/` removed.

## How this lands in the repo (when real)

- Generic engine (registry, parser, validator, outline) → `Core`.
- Validator + index-freshness run as Structure-Rulebook guards in CI: a
  non-conforming or stale doc fails the build.
- `_schema/` + `kinds/` + `blocks/` + `doctypes/` are data; adding a kind, a
  block, or a doc type is a YAML change — the engine names none of them.

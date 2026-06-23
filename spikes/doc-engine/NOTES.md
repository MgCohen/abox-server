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
7. **Grouping.** Collection block types (phase, decision) that read better
   clustered need a model — a `collection:` flag on the block + an optional
   `group` attr for ad-hoc meta-groups (id kept separate from grouping, since id
   is the stable handle). Open fork: does grouping reshape the source (nested
   `## Phases` → `### member`) or stay view/render-only metadata over a flat source?
8. **The selector** (still the gap). Block selection was hand-done in-context this
   time. Needs an organized author prompt: dump + catalog → blocks, gated by
   validate.py, then graded by the judge criteria.

## How this lands in the repo (when real)

- Generic engine (registry, parser, validator, outline) → `Core`.
- Validator + index-freshness run as Structure-Rulebook guards in CI: a
  non-conforming or stale doc fails the build.
- `blocks/` + `doctypes/` are data; adding a doc type is a YAML change.

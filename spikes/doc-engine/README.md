# Spike: doc-engine

Throwaway probe of a **block-structured documentation engine**: data-defined
block schemas + per-doc-type catalogs, a distilled instance file, a validator
that enforces the structure, and a tool that derives read-only views. Not wired
into `ABox.slnx`. Iterate freely.

## The loop being probed

```
dump (ephemeral)  ──distill──►  instance.md (blocks)  ──validate──►  pass/fail
   PLANS/git-feature.md            out/git-feature.plan.md          validate.py
        │                                  │  ▲                          ▲
        └ scratch, discarded               │  └ conforms to ─────────────┘
                                  outline.py    doctypes/*.yaml + blocks/*.yaml
                                  (derived index / status board)
```

- **dump** — `PLANS/git-feature.md` (a real feature plan). Ephemeral input; the
  durable artifact is the block file, which stands alone.
- **blocks/** — one YAML per block type: `description` (one-liner, feeds the
  decision matrix) + `rubric` (checkable one-liners) + typed `attrs` + `body`.
  Collection types set `collection: true` + a `group:` label → `## Group` / `### member`.
- **criteria/** — selection + quality rubrics for the repo's generic judge (soft,
  semantic) — runs after validate.py (hard, structural).
- **doctypes/** — one YAML per doc type (filename is the name): a `description`
  (which doc to use) + the `blocks` catalog + a `required` set + doc-level `attrs`
  (front matter) + a binary-one-liner `rubric`. No counts, no ordering rules.
- **out/** — the distilled block instance.
- **validate.py** — parses the instance, enforces the catalog. Structure is law.
- **outline.py** — derives an index + phase status board from the blocks.
- **catalog.py** — prints the decision matrices (descriptions) a selector reads to
  pick a doc type, then its blocks.
- **selector.md** — the author procedure: dump → conformant instance, gated by
  validate.py and graded by the judge.
- **_schema/ + check_schema.py** — the meta-schema: every block and doc-type
  *definition* is itself validated (floor enforcement, so the whole stack is structured).

## Run

```bash
cd spikes/doc-engine
python3 check_schema.py                              # definitions conform to the meta-schema
python3 validate.py out/git-feature.plan.md       # instance conforms to the catalog
python3 catalog.py                                  # decision matrices (doc types, blocks)
python3 outline.py  out/git-feature.plan.md        # print derived views
python3 outline.py  out/git-feature.plan.md --write # inject the index in place
```

## Instance syntax

A leading `---` YAML block is the doc's front matter — visible, validated against
the doctype's `attrs`. Singleton blocks are top-level; collection blocks grouped:

```md
---
docType: feature-plan
status: draft
---

## Context                       <- singleton: the header is the type
<!-- id: 2 -->

Markdown body — distilled prose, real files/symbols.

## Phases                        <- group header for a collection type
### Real GitHub adapter          <- member: ### title, type from the group
<!-- id: 12 -->
status: blocked

**Goal.** ...
```

- The id lives in an `<!-- id: N -->` comment — the stable handle, kept out of the
  human header (it is agent-oriented and orthogonal to grouping).
- `key: value` lines under the (sub)header are scalar attrs (status, lean, caveat).
- Final on-disk syntax should match the render repo's parser — the model here is
  parser-agnostic.

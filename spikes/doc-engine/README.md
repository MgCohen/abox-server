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
- **blocks/** — one YAML per block type: `short` (selection one-liner) + `usage`
  (how to mount) + `example` (curated, static) + typed `attrs` + `body`.
- **doctypes/** — one YAML per doc type: the catalog of allowed blocks + a
  `required` set. No counts, no ordering rules.
- **out/** — the distilled block instance.
- **validate.py** — parses the instance, enforces the catalog. Structure is law.
- **outline.py** — derives an index + phase status board from the blocks.

## Run

```bash
cd spikes/doc-engine
python3 validate.py out/git-feature.plan.md       # enforce
python3 outline.py  out/git-feature.plan.md        # print derived views
python3 outline.py  out/git-feature.plan.md --write # inject the index in place
```

## Instance syntax

```md
## Phase - Wire AgentHost to PtySession
<!-- id: 13 -->
status: doing

**Goal.** Markdown body — distilled prose, real files/symbols.
```

- Header is type-first (`## <type> [- <title>]`) — scannable.
- The id lives in an `<!-- id: N -->` comment: it is the stable handle for
  patch/resume, but agent-oriented, so it stays out of the human's way.
- `key: value` lines under the header are scalar attrs (status, lean, caveat).
- Final on-disk syntax should match the render repo's parser — the model here is
  parser-agnostic.

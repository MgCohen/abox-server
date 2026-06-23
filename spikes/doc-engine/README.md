# Spike: doc-engine

Throwaway probe of a **block-structured documentation engine**: data-defined
block schemas + per-doc-type catalogs, a distilled instance file, and a runnable
validator that enforces the structure. Not wired into `ABox.slnx`. Iterate freely.

## The loop being probed

```
dump (ephemeral)  ──distill──►  instance.md (blocks)  ──validate──►  pass/fail
   PLANS/git-feature.md            out/git-feature.plan.md          validate.py
        │                                  ▲                             ▲
        └ scratch, discarded               │ conforms to ────────────────┘
                                    doctypes/feature-plan.yaml + blocks/*.yaml
```

- **dump** — `PLANS/git-feature.md` (a real, verbose feature plan). Treated as
  ephemeral input; the durable artifact is the block file, which stands alone.
- **blocks/** — one YAML per block type: typed `attrs` + a `body`. Data, not code.
- **doctypes/** — one YAML per doc type: which blocks, min/max, position. The
  document-level guarantees.
- **out/** — the distilled block instance (markdown + `:::block{attrs}` … `:::`).
- **validate.py** — parses the instance, enforces the catalog. Structure is law.

## Run

```bash
cd spikes/doc-engine
python3 validate.py out/git-feature.plan.md     # PASS
```

## Instance syntax (spike)

```md
:::phase{id=p-s2.3 title="Stack system" status=todo}
Markdown body — distilled prose, real files/symbols.
:::
```

Scalar fields live in the `{…}` header; the body is markdown. Final on-disk
syntax should match the render repo's parser — the model here is parser-agnostic.

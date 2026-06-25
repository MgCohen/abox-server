# doc-engine

A **block-structured documentation engine**: data-defined kinds + blocks +
per-doc-type catalogs, distilled instance files, a validator that enforces the
structure, and derived read-only views. A standalone .NET tool (`ABox.DocEngine`,
the `docengine` CLI) — deliberately NOT in `ABox.slnx`: it is dev tooling, not the
orchestrator spine, so it carries its own YamlDotNet dependency without touching
the product's zero-dep assemblies.

## The loop

```
dump (ephemeral)  ──distill──►  instance.md (blocks)  ──validate──►  pass/fail
   a brain-dump                    out/<slug>.plan.md          docengine validate
        │                                  │  ▲                          ▲
        └ scratch, discarded               │  └ conforms to ─────────────┘
                                docengine outline   doctypes/*.yaml + blocks/*.yaml
                                  (derived index / status board)
```

## Layout

- **_schema/kind.schema.yaml** — the single meta-schema: what a *kind* file looks
  like. It is itself a kind and conforms to itself, so the stack is structured top
  to bottom — meta-schema → kinds → definitions → instance.
- **kinds/** — one YAML per *kind* of definition (`block`, `doctype`): the `fields`
  its definitions must carry + cross-field `constraints`. A new enforced structure
  is a new `kinds/*.yaml` — the engine names no kind.
- **blocks/** — one YAML per block type: `description` (one-liner, feeds the
  decision matrix) + `rubric` (checkable one-liners) + typed `attrs` + `body`.
  Collection types set `collection: true` + a `group:` label → `## Group` / `### member`.
- **doctypes/** — one YAML per doc type (filename is the name): a `description`
  (which doc to use) + the `blocks` catalog + a `required` set + doc-level `attrs`
  (front matter) + a `rubric` — an `id: rule` map of binary one-liners serving BOTH
  authoring and the judge (it marks each rule pass/fail by id; no criteria file).
- **out/** — distilled instances: `git-feature.plan.md`, `claude-stop-hook.plan.md`
  (feature-plan) and `odysseus.research.md` (research).
- **selector.md** — the author procedure (dump → conformant instance). Wired as the
  `create-doc` agent + `/create-doc` command in `.claude/`.
- **howto/** — step-by-step guides: add a block, an instance, or a kind.

The engine is the C# under this directory: `SchemaChecker` (floor),
`InstanceParser` + `DocValidator` (instance), `Catalog` (decision matrices),
`Outline` (derived views), behind the `Program` CLI.

## Run

```bash
cd tools/doc-engine
dotnet run --project . -- check                                # definitions conform to the meta-schema
dotnet run --project . -- validate out/git-feature.plan.md     # instance conforms to the catalog
dotnet run --project . -- catalog                              # decision matrices (doc types, blocks)
dotnet run --project . -- catalog feature-plan                 # blocks available to one doc type
dotnet run --project . -- outline out/git-feature.plan.md      # print derived views
dotnet run --project . -- outline out/git-feature.plan.md --write  # inject the index in place
```

The data root is found by walking up from the working directory for
`_schema/kind.schema.yaml`; pass `--root <dir>` to override.

## Instance syntax

A leading `---` YAML block is the doc's front matter — visible, validated against
the doctype's `attrs`. Singleton blocks are top-level; collection blocks grouped:

```md
---
docType: feature-plan
status: draft
---

## Context                       <- singleton: the header is the type

Markdown body — distilled prose, real files/symbols.

## Phases                        <- group header for a collection type
### Real GitHub adapter          <- member: ### title, type from the group
status: blocked

**Goal.** ...
```

- `key: value` lines under the (sub)header are scalar attrs (status, lean, caveat).
- An optional `<!-- id: <slug> -->` comment pins a stable, agent-oriented handle on a
  block — only when something references it across edits; most blocks omit it.
- Final on-disk syntax should match the render repo's parser — the model here is
  parser-agnostic.

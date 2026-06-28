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
   a brain-dump              <home>/<slug>.plan.md           docengine validate
        │                                  │  ▲                          ▲
        └ scratch, discarded               │  └ conforms to ─────────────┘
                                docengine outline   doctypes/*.yaml + blocks/*.yaml
                                  (derived index / status board)
```

An instance lives in its **home folder** in the repo — a Rulebook under
`tests/**/Rulebook/`, an ADR under `design/adr/`, a plan under `PLANS/` — and is
validated **in place**. The engine owns no output directory; it validates any path.

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
- **selector.md** — the author procedure (dump → conformant instance). Wired as the
  `create-doc` agent + `/create-doc` command in `.claude/`.
- **howto/** — step-by-step guides: add a block, an instance, or a kind.

The engine is the C# under this directory: `SchemaChecker` (floor),
`InstanceParser` + `DocValidator` (instance), `Catalog` (decision matrices),
`Outline` (derived views), behind the `Program` CLI.

## Rulebooks: the standard / criteria / guarantee split

A test type's Rulebook (ADR 0015) is one doc-engine doctype expressed across three
physically separated layers — the standard is central, the criteria are central
per-type, the guarantees are co-located with the feature they protect:

| Layer | What it is | Doc-engine role | Home |
|---|---|---|---|
| **Doctype** (`rulebook`, `rubric`) | the schema — what *any* rulebook/rubric must look like | the catalog | **central** — `tools/doc-engine/doctypes/` |
| **`<type>.md`** | the per-*type* criteria ("what a Unit test is") | a `rubric` **instance** | **central** — `tests/Rubrics/`, one per type |
| **`rules.md`** | this *feature's* guarantees | a `rulebook` **instance**, `rubric:`→central rubric | **co-located** with the feature under `src/<…>/<Owner>/Tests/<Type>/` |

The engine validates every instance against its doctype **wherever it lives**;
`ParityGuard` (test-side) bridges the `### ` headers in a co-located `rules.md` to
the `[Rule]` tests beside it. There is no `add-a-doctype` howto: a new doctype is a
rare, owner-reviewed change to the protected catalog — add a `kinds`/`doctypes`
entry by following the existing files, not a routine procedure.

A doctype's **`rubric`** is *advisory* — authoring guidance and binary one-liners
the LLM judge grades against. It is **not** a `[det]` validator rule: `DocValidator`
enforces blocks, required attrs, and labels, never rubric text. So per-type wording
in a rubric (e.g. naming `wire`/`live`/`e2e` "behavioural") is descriptive judge
guidance, not a structural constraint, and is outside ADR 0015's `[det]` guarantee.

## Run

```bash
cd tools/doc-engine
dotnet run --project . -- check                                # definitions conform to the meta-schema
dotnet run --project . -- validate <path/to/doc.md>            # an instance conforms to the catalog
dotnet run --project . -- catalog                              # decision matrices (doc types, blocks)
dotnet run --project . -- catalog feature-plan                 # blocks available to one doc type
dotnet run --project . -- outline <path/to/doc.md>             # print derived views
dotnet run --project . -- outline <path/to/doc.md> --write     # inject the index in place
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

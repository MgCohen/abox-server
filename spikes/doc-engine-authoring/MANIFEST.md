# authoring flow — what travels (Phase 5)

Enforcement (Phases 1–4) proved the engine *checks* documents in a new home. This
proves the engine *authors* them there too — new instances, new blocks, new
doctypes — and lists the surface that carries the flow.

## The two catalog modes (the crux)

The engine resolves its catalog in this order: explicit `--root` → walk up from
the CWD → the catalog baked into the tool. That one resolver gives two modes:

| Mode | Catalog lives | You can author | Use |
|---|---|---|---|
| **Enforce-only** | baked read-only inside the `dotnet tool` (Phase 3) | **instances** of a fixed vocabulary | a repo that just wants document checks |
| **Authoring** | checked-in editable YAML, engine via `--root ./doc-catalog` | **instances + blocks + doctypes** | a repo that owns and extends its own vocabulary |

Authoring **requires** the editable mode — you cannot edit a catalog sealed in a
nupkg. So a home that wants to create templates keeps its catalog as
version-controlled data and points `--root` at it. This spike runs entirely in
that mode.

## The three authoring levels (all CLI-backed, all travel)

| Level | Change | Command that gates it |
|---|---|---|
| **Instance** | a new `*.md` document | `catalog` / `catalog <doctype>` (discover) → `validate` → `outline --write` → judge on `rubric` |
| **Block** | a new `blocks/<type>.yaml` (+ list it in a doctype) | `check` |
| **Doctype / kind** | a new `doctypes/<name>.yaml` or `kinds/<x>.yaml` | `check` |

This spike exercises all three: it authors the **`finding`** block and the
**`brief`** doctype (neither exists in the engine's home catalog), `check`s them,
then authors and `validate`s a `brief` **instance**.

## The author-agent bundle (`bundle/`)

The `.claude` + docs surface that turns a dump into a conformant instance —
the authoring sibling of the judge bundle:

| File | Role |
|---|---|
| `.claude/agents/create-doc.md`, `.claude/commands/create-doc.md` | the author agent — topic-blind; reads the catalog at runtime, carries no vocabulary itself |
| `engine-docs/selector.md` | the author procedure (dump → conformant instance) |
| `engine-docs/howto/{add-a-block,add-a-kind,add-an-instance}.md` | the step-by-step authoring guides |

Not copied here (it is itself a doc-engine `guide` instance, so it stays a
tracked instance rather than a duplicated file):
`tools/doc-engine/guides/extend-the-doc-engine.guide.md` — the canonical
"extend the engine" walkthrough travels with the engine as catalog-adjacent docs.

Like the judge, the agent is portable: it names no doc type, reading every topical
choice from the catalog data at runtime.

## What a real cutover repoints

`.claude/create-doc.*` and `on-doc-change.sh` invoke the engine as
`dotnet run --project tools/doc-engine`; in a packaged home they call the
installed `docengine` with `--root` at the repo's editable catalog. Protected
paths, real new home — out of scope for a spike.

# Spike — Prompt → Recipe (the other end)

> Branch `claude/stacked-pr-from-109-z7u4th`, stacked on the building-style PR (#109).
> An **exploration that opens a new pass**, not an implementation. Everything so far runs the
> recipe → code direction and that half is *solved*. This doc explores the **input** direction:
> how a user's intent becomes a recipe in the first place.
> **Status — EXPLORATION.** Canonical pipeline from the owner's decomposition diagram; seam located;
> altitude gap named; first slice + done-when proposed.

## The two ends

```
USER INTENT  ──── decomposition ────►  RECIPE (typed tree)  ──deterministic──►  FEATURE
 "favorite an artist"                   [ recipe catalog ]      (Phase 2 +         files, services,
                                                                 building-style)    registrations
   the OPEN end                         the SEAM                                    the SOLVED end
```

Every artifact in this spike — snippets, generated nodes, factories, operators, the generator —
builds the **right half**. The recipe is the *contract* between the two halves. This pass explores
the **left arrow**, and the owner's diagram says that arrow is **not a single jump** — it is a staged
ladder of LLM steps with typed artifacts between them, and only the last rung is deterministic.

## The decomposition pipeline (the canonical model)

```
                          ┌──── back-and-forth, human-approved ────┐
 User Intent ──► LLM ──────────────► Plan ──► LLM ──► Task/Phase[] ──► LLM ──► Recipe[] ──► compose ──► Feature
  (prompt)     (plan, ↔ human)      (doc)   (break    (reasoned,    (match    (from a      (parse,
                                            down)      reviewed)    task→     deterministic  splice)
                                                                    recipe)   catalog)
```

| # | Stage | Actor | Deterministic? | In → Out | Gate |
|---|-------|-------|----------------|----------|------|
| 1 | **Plan** | LLM ↔ Human | no | Intent → **Plan doc** (Context · Summary · Expected Results · What we'll do · What needs to change · Validation) | **Human approval** |
| 2 | **Breakdown** | LLM | no | Plan → **Task/Phase[]** — *reason* about required changes, then *review* for roles & ordering (two passes) | — |
| 3 | **Match** | LLM | no | each Task → a **Recipe** (or small composition) from the catalog | — |
| 4 | **Compose** | **deterministic** | **yes** | Recipe[] → **Feature** (real source) | the **compiler** |

Read top-down, the structure tightens at each rung: free-form intent → an approved structured plan →
an ordered task list → catalog recipes → owned code. **Each artifact is more constrained than the one
above it**, and the agent's freedom shrinks accordingly — exactly the "wrap the agent in deterministic
structure so it gets maximum guidance" thesis from `CLAUDE.md`, applied as a *ladder*, not a single
gate.

### The worked example (from the diagram)

Context: *assume a fully-working Spotify-clone app.*

- **Stage 1 — Plan.** Prompt: *"implement a Favorite-Artist collection — favorite any artist, save to a
  collection, view the collection, unfavorite."* The agent asks clarifying questions (back-and-forth
  with the human), then emits a **Favorite-Artist Plan** with the fixed schema above. The human
  **approves** it. This is the only human gate.
- **Stage 2 — Breakdown.** The agent reasons the plan into tasks (*create the "Favorite Artist"
  service · create the Model/Entity · create the DB/Repo · implement the API · add docs*), then
  **reviews** that list for roles & ordering — which *adds* tasks the first pass missed (*dependency &
  manifest changes · a Vertical-Slice feature with MediatR controllers per standards · DI
  registration*) and reorders them. Breakdown is a **reason → review** loop, not one shot.
- **Stage 3 — Match.** Each task is matched to a **recipe** from the deterministic catalog:
  `Create new service` → `Scaffold Service`, `Create DB/Repo` → `Repository Pattern Fetch`,
  `Implement API` → `SignalR API`, `Add DI registration` → `Register Services`, `Create Model/Entity`
  → `Create Model`. The mapping is **not 1:1** — several tasks may resolve to one recipe, some tasks
  to a *composition* of recipes, and a task with no matching recipe is a signal (write the recipe, or
  drop to freer code — backlog #4/#10).
- **Stage 4 — Compose.** The matched recipes lower deterministically to the feature's source — the
  half this spike already proved.

### What the recipe catalog *is* (the key realization)

The diagram's recipes — `Create Model`, `Scaffold Service`, `Repository Pattern Fetch`, `SignalR API`,
`Register Services` — are **where the team's architectural standards live, deterministically.** "Per
standards", "Vertical Slice", "MediatR controllers" aren't prose the agent must remember and might
drift from — they are *baked into the recipe*. The LLM decides **what** to build (stages 1–3); the
recipe enforces **how** it's built (stage 4). That split is the product: the guardrails are
deterministic; only the intent-reading is not.

## The altitude gap (spike recipes vs diagram recipes)

The diagram's recipes are **feature-scale** (emit files, classes, services, registrations). The
spike's recipes are **statement-scale** (`Loop`, `Define`, `Add` → lines inside one method). Same
mechanism — *a typed tree of nodes lowered to deterministic source* — at two altitudes:

| | Spike recipe (proven) | Diagram recipe (target) |
|---|---|---|
| Composes | statements in a method body | files, types, members, registrations |
| Node kinds | `LoopNode`, `DefineNode`, `AddNode` … | `ClassNode`, `MethodNode`, `FieldNode`, `RegistrationNode` … |
| Root | `Block` (a statement region) | a member/file region — a *declaration tier* |
| Output | `int acc = 0; for … return acc;` | a `FavoriteArtistService.cs`, a repo, a DI line |

The bridge is **already on the backlog** — README items **#7–#11** (the declaration tier + the type
spine): grow *up* from "compose statements" to "compose declarations" (a `MethodNode` whose body is
today's `Block`; a `ClassNode` whose members are nodes; a member region that is to a type what a
`Block` is to a method). **The decomposition pass and the declaration tier meet here:** decomposition
produces recipes, and for a recipe to be a *feature* it must reach the declaration tier. The spike has
proven the compose engine at the bottom rung; lifting it to the diagram's altitude is #7–#11, not a
new engine.

## The seam, restated

The spike's founding premise — *"the opposite of 'ask an LLM to glue snippets together'"* — survives
intact: the **gluing** (stage 4) is deterministic and byte-identical. Three LLM stages sit *above* the
seam (plan, breakdown, match); the recipe is the **commit point** where judgment ends and determinism
begins. A bad decomposition doesn't emit bad code — it produces a recipe that **fails to compile**, and
the compiler error is the repair signal. The type-safe recipe validates the agent **for free** — that
is the whole reason to put a typed seam between intent and output.

## Design space for the match stage (how a Task becomes a Recipe)

Building-style (#109) made a recipe read like intention-revealing C# — that surface *is* the agent's
output language at stage 3. The central fork (settle with probes, #109-style):

| Channel | How | Pro | Con |
|---|---|---|---|
| **1. Direct C#** | agent emits the factory/operator expression; compile it against the catalog | type system validates *for free*; output is exactly the authored surface | must emit compiling C#; needs a compile-repair loop |
| **2. Structured → lower** | agent emits JSON/tool-output; map to the node tree | easy to steer via tool schema | re-introduces the JSON the design rejected (README §6); loses the compile-time schema |
| **3. Constructive tools** | agent builds the tree node-by-node, validated per call | incremental, can't drift far | verbose, many round-trips, loses the holistic read |

The tension: the type-safe recipe is **most** powerful when the agent emits real C# (the compiler is
the validator the whole spike is built around), but agents are **easier to steer** via structured
output. README §6 rejected JSON for the *human* author; whether an agent's structured channel changes
that calculus is the load-bearing open question.

## Connections to the product (this is not just a spike)

- **Stage 1's Plan doc is the doc engine.** Its fixed schema (Context · Summary · Expected · ToDo ·
  ToChange · Validation) is exactly a structured document — the repo *already has* a doc-template /
  block engine on `main`. The Plan should be a doc-engine document, not a bespoke artifact.
- **The staged ladder is a workflow.** Plan → approve → breakdown → match → compose is the
  "deterministic workflow wrapping an agent" the product is *for*. The spike proves the compose rung;
  the product owns running the ladder.
- **The recipe catalog is the guardrail surface.** Architectural standards as deterministic recipes,
  not remembered prose.

## Recommended first slice

Prove **one rung end-to-end** without a live model in the deterministic gate — the **match → compose**
rung at the statement tier the spike already supports:

1. **Catalog manifest** — a gen-tool output listing every snippet/recipe (name, fill shape, produced
   type, example), generated from the `[Snippet]` methods. Deterministic, drift-free, no model.
2. **Agent contract** — define input (a Task + the manifest + exemplars) and output (a recipe in the
   factory surface). Write it down; don't build the model call yet.
3. **Validate = the existing compile gate** — feed a candidate recipe into a recipe-compile harness; a
   non-compiling recipe is rejected with the compiler error as repair feedback.
4. **Prove on the canonical task** — "sum the numbers 0 to 4" → the loop-sum recipe → byte-identical
   `ScriptData.cs` → `10`.

The model-in-the-loop is **recorded/stubbed** for the spike (a fixed recipe text for the canonical
task), exactly as Phase 2 put nothing un-runnable in its gate. The spike proves the *deterministic
scaffolding around the agent*; the live LLM is a later **host swap** (the same A2 → A1 move that
swapped the gen tool for a source generator). Stages 1–2 (Plan, Breakdown) and the altitude lift
(#7–#11) are **named here but out of scope for the first slice** — this rung proves the seam works
before the ladder is built above it.

## Done-when (proposed)

1. A generated **catalog manifest** lists every snippet with fill shape + produced type + an example,
   drift-free from the `[Snippet]` methods (a regression net pins it, like the nodes).
2. A **recorded agent output** (recipe text for "sum 0..4") compiles against the catalog and lowers to
   the **byte-identical** loop-sum `ScriptData.cs` → `10`.
3. A **deliberately malformed** recipe (a `string` into the `Expr<int>` count) is rejected at the
   validate step with the actual `CS1503`, surfaced as repair feedback — proving the structure
   validates the agent *for free*.

## Open questions / risks

- **The match-channel fork (C# vs structured)** is the load-bearing decision — settle it with probes.
- **The altitude lift (#7–#11) is on the critical path to the diagram's "Feature"** — the statement
  tier can't emit a service/model/registration. Sequence it deliberately.
- **The `Var<T>` declaration is an agent hazard** — `var acc = new Var<int>("acc")` then `acc` (the
  "name spelled twice" sharp edge, BUILDING-STYLE.md) means two tokens that must agree. A human nit; an
  agent footgun. Does the agent surface need a terser binding?
- **Catalog scale.** The manifest is trivial at nine snippets; an agent's job grows with the catalog.
  Retrieval / nearest-exemplar is the likely answer, unproven.
- **Breakdown's reason → review loop** (stage 2) is where ordering & role correctness live — and it's
  pure judgment with no compiler to catch it. What validates a *task list* the way the compiler
  validates a recipe?

## Guardrails (unchanged)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every change keeps the generate → compile → run gate green; `out/ScriptData.cs` stays byte-identical
  until a slice *deliberately* changes the output.
- YAGNI — least mechanism. The manifest and the validate-repair loop earn their surface against the
  canonical task before anything scales.

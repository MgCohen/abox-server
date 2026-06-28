# Spike — Plan → Breakdown → Recipes (the middle)

> Branch `claude/stacked-pr-from-109-z7u4th`, stacked on #109.
> Zooms into the **middle** of the decomposition pipeline (`PROMPT-DECOMPOSITION.md`): given an
> approved **Plan**, how breakdown produces **Tasks**, and how each task finds the **Recipes** it
> needs. **Status — PLANNING.** Worked input authored & validated; the two stages designed with their
> deterministic validators; first slice + open questions recorded.

## Scope of this doc

| Stage | In this doc? | Owner |
|---|---|---|
| 1 — Intent → Plan | **no** — use the authored sample plan + the doc engine | doc engine (exists) |
| **2 — Plan → Tasks (breakdown)** | **YES** | this session |
| **3 — Tasks → Recipes (match)** | **YES** | this session |
| 4 — tie recipes together (compose) | **no** — explicitly deferred | another session |
| The recipe internals / catalog / compose engine | **no** — consumed as a seam | another session (in progress) |

```
        ┌─────────────── THIS DOC ───────────────┐
Plan ──► breakdown ──► Task[] ──► match ──► Recipe[] ──► (compose) ──► Feature
(doc      stage 2       (doc      stage 3   (from        stage 4
 engine)               engine)              catalog)     elsewhere
```

Everything in stages 2–3 is **LLM-authored, deterministically validated** — the same atom the rest of
the product runs on (`PLANS/capability-spec.md` §1: *input → findings → verdict → pass/fail*). The LLM
proposes; a validator gates; a failure is repair feedback.

## The grounding: the Plan is already a structured doc

The repo's **doc engine** (`tools/doc-engine/`) already defines a **`feature-plan`** doctype — typed
blocks `summary · context · scope · decision · phase · verification · open-question`, validated by
`DocValidator` and graded by a rubric/judge. The worked input for this spike is a real, validating
instance: **`spike/favorite-artist.plan.md`** (`docengine validate` → `PASS`).

The load-bearing fact: **a feature-plan already carries an ordered list of `phase` blocks** — "one
ordered, shippable step," `status: [todo·doing·done·blocked]`, "at least two." So breakdown does **not**
invent structure from prose — it *starts from the plan's phases* and refines them to recipe grain.

## The unifying move: validate the task list the way the compiler validates the recipe

`PROMPT-DECOMPOSITION.md` left an open question — *the recipe is compiler-validated, but what validates
a task list?* The doc engine answers it: **model the breakdown as its own doctype.** A `task-breakdown`
doctype with a `task` block gives the task list the *same* free, deterministic validation a recipe gets
from the compiler — coverage, required fields, closed-enum kinds, a DAG with no dangling edges. Every
stage of the middle then has a real validator:

| Artifact | Validator | Catches |
|---|---|---|
| Plan | doc engine (`feature-plan`) | missing required blocks, bad enums, thin phases |
| **Task list** | doc engine (**new `task-breakdown`**) | a task with no kind, an edge to a missing task, a plan phase with zero tasks |
| Recipe selection | the catalog + (eventually) the compiler | a task with no recipe, a recipe with unbound required fills |

No stage is validated "by vibes."

## Stage 2 — Breakdown (Plan → Tasks)

### The fork: read the plan's phases, or generate tasks fresh?

| Option | What | Verdict |
|---|---|---|
| **A — phases *are* the tasks** | one plan phase → one task | too coarse — a phase ("Build the service") is several recipe-grained moves |
| **B — generate tasks from prose, ignore phases** | LLM invents the whole task list | throws away the structure the plan already approved |
| **C — expand each phase into recipe-grained tasks** *(lean)* | phases stay as the ordered, human-approved grouping; each phase decomposes into tasks sized to one recipe | reuses the approved order; tasks land at the grain the match stage needs |

**Lean C.** Phases are the coarse, shippable, human-approved unit; **tasks are the recipe-grained unit
inside a phase.** The doc engine already models this shape — a collection block with a group header and
members — so a `task` block grouped under its phase is a natural fit (phase = group, tasks = members).

### What a Task is (the proposed `task` block)

```
### Create the FavoriteArtist model        ← title
kind: create-model                         ← closed enum, aligned to recipe kinds
target: src/Domain/.../FavoriteArtist.cs    ← where it lands
depends-on:                                 ← ids → the ordering DAG
fills: userId, artistId, favoritedAt        ← hint at the recipe's arguments
                                            ← body: goal + acceptance (done-when)
```

`kind` is the join key to the recipe catalog (stage 3). `depends-on` makes ordering **checkable** (a
DAG, not prose). `fills` previews the recipe arguments so a task with no bindable fills is visible
early. The body carries the per-task done-when.

### The two-pass shape (from the diagram)

The owner's diagram shows breakdown as **reason → review**, not one shot:

1. **Reason** — expand each plan phase into its tasks (the obvious moves: model, store, service, API).
2. **Review for roles & ordering** — the pass that *adds the tasks the first pass forgets* (DI
   registration, dependency/manifest changes, the vertical-slice wiring) and orders them (model →
   store → service → API → registration → docs). This is where role/ordering correctness lives.

The review pass is pure judgment with no compiler behind it — so its **backstop is the
`task-breakdown` validator**: coverage (every plan phase has ≥1 task; every "what needs to change"
item is covered), a well-formed dependency DAG (acyclic, no edge to a missing task), and a `kind` from
the closed vocabulary.

## Stage 3 — Match (Tasks → Recipes)

### The seam: the recipe catalog manifest

Matching needs the catalog *described*. The recipe session (the other one) owns the catalog and emits a
**manifest** — per recipe: name, `kind`, what it builds, its fills (typed), an example. (This is the
"catalog manifest" first slice from `PROMPT-DECOMPOSITION.md`.) **This session consumes the manifest; it
does not own the recipes.** The manifest is the contract between the two sessions.

### Match = select + bind (not 1:1)

Matching is two moves, and it is **many-to-many**:

1. **Select** the recipe(s) whose `kind` satisfies the task — keyed on `task.kind`, confirmed by the
   manifest description. One task may need several recipes (a "vertical slice" task → model + store +
   service + registration); several tasks may share one recipe.
2. **Bind** the recipe's fills from the task's `fills` + the plan's context (the model's fields, the
   service name "FavoriteArtist", the route). A recipe whose **required fills can't be bound** is an
   invalid match — caught here, not at compose.

### No-match is a signal, not a silent failure

A task with no satisfying recipe is the **most important output of this stage** — it must be *reported*,
never force-fit. Three resolutions:

| No-match cause | Resolution |
|---|---|
| catalog gap (recipe doesn't exist yet) | write the recipe (feeds the recipe session) |
| genuinely one-off code | drop to freer code — `Raw(...)` / lambda (`PROMPT-DECOMPOSITION` backlog #4/#10) |
| task too coarse | send it back to breakdown to decompose further |

The match validator's verdict: **every task maps to a recipe *or* an explicit gap with a resolution**,
and every selected recipe has all required fills bound. Anything else fails the stage.

## The seams (who owns what)

```
   doc engine          THIS SESSION                 recipe session            another session
 ┌───────────┐   ┌──────────────────────────┐   ┌──────────────────┐      ┌──────────────┐
 │ feature-  │──►│ task-breakdown doctype    │   │ recipe catalog   │      │  compose     │
 │ plan +    │   │ + breakdown (stage 2)     │   │ + manifest ──────┼──┐   │  (stage 4)   │
 │ validator │   │ + match (stage 3) ◄───────┼───┼──────────────────┘  └──►│  tie recipes │
 └───────────┘   └──────────────────────────┘   │ + compose engine │      │  together    │
                  owns: Task model, the two      └──────────────────┘      └──────────────┘
                  validators, the match logic     consumes: the manifest    out of scope here
```

This session's deliverables: the **`task-breakdown` doctype**, the **breakdown** stage (2), the
**match** stage (3), and their **validators**. It *consumes* the recipe manifest and *hands off* a
validated (task → recipe) mapping to the compose session.

## Worked example (the sample plan → tasks → recipes)

`spike/favorite-artist.plan.md` (4 phases) breaks down and matches like the diagram:

| Plan phase | Task (kind) | Recipe (from catalog) |
|---|---|---|
| Model the favorite | create the `FavoriteArtist` model · `create-model` | **Create Model** |
| Stand up the store | create the favorites store · `create-store` | **Repository Pattern** |
| Build the service | scaffold the `FavoriteArtist` service · `scaffold-service` | **Scaffold Service** |
| Expose the API | implement the endpoints · `implement-api` | **API endpoint** |
| *(added by review)* | register in DI · `register-di` | **Register Services** |
| *(added by review)* | add endpoint docs · `add-docs` | *(no recipe → gap: raw/doc step)* |

The review pass supplies the **ordering** (model → store → service → API → registration → docs) and the
two tasks the first pass missed — exactly the diagram's second column. The `add-docs` gap is a *correct*
output: the catalog has no docs recipe yet, so it's flagged for resolution, not forced.

## First slice (proving the middle)

The smallest end-to-end proof, no live model in the gate:

1. **Author the `task-breakdown` doctype** (YAML in `tools/doc-engine/`: a `task` block with
   `kind`/`depends-on`/`fills` attrs, a `task-breakdown` doctype) — pure YAML, zero C#, dogfoods the
   engine.
2. **Hand-author the Favorite-Artist task breakdown** as a validating instance — proves the doctype
   expresses the diagram's task list and the validator accepts it.
3. **Stub the match** against a recorded recipe manifest — map each task's `kind` to a recipe, surface
   the `add-docs` gap. No model yet; a fixed mapping proves the select+bind+gap mechanism.

The LLM that *authors* the breakdown and *performs* the match is a later host swap (same as
`PROMPT-DECOMPOSITION`'s recorded-agent approach); this slice proves the **doctype + validators +
match mechanism** deterministically.

## Done-when (proposed)

1. A `task-breakdown` doctype exists and the Favorite-Artist breakdown instance **validates** (`docengine
   validate` → `PASS`), with the validator rejecting a malformed task list (a task with no `kind`, an
   edge to a missing task).
2. The breakdown **covers** the sample plan — every plan phase has ≥1 task — checkable, not asserted.
3. A recorded match maps every task to a recipe **or** an explicit gap, and the `add-docs` gap is
   surfaced rather than force-matched.

## Open questions

- **Phase ⊃ task, or phase = task?** Lean phase-as-group / task-as-member (option C). Confirm the doc
  engine's collection model carries it cleanly, or whether tasks want their own ungrouped doctype.
- **The `kind` vocabulary** is the join between breakdown and match — who owns it? It must stay aligned
  with the recipe catalog's kinds (the seam to the recipe session). Likely the manifest *publishes* the
  kinds and breakdown draws from them, so a task can't name a kind no recipe serves.
- **Coverage checking** — "every plan phase has a task" is mechanical, but "every *change* the plan
  implies has a task" is judgment. How much can the validator check vs. how much rides on the review
  pass + a judge rubric?
- **Bind sources** — the recipe fills come from the task's `fills` + the plan's prose. Is the plan
  structured enough to bind from (model fields, route names), or does breakdown need to *extract* those
  into the task explicitly so match has a clean, typed source?

## Guardrails (unchanged)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`); the new doctype lives in the doc engine,
  which is already standalone dev tooling.
- The sample plan and any breakdown instance must keep validating — they're discovered by the `Docs`
  test, so a malformed one fails CI.
- YAGNI — the `task` block carries only the fields the match stage needs (kind, depends-on, fills); no
  speculative metadata until a second consumer asks for it.

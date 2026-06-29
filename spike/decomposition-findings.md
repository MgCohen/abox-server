# Decomposition — Findings

> Durable conclusions from the prompt→recipe exploration (the "other end" pass). Plain notes, **not** a
> doc-engine instance. Companion: `decomposition-tasks.md` (what's open/next). Source docs:
> `PROMPT-DECOMPOSITION.md` (whole pipeline), `PLAN-TO-RECIPES.md` (the middle, designed),
> `projects-decomposition.md` (the middle, worked on real code), `favorite-artist.plan.md` (sample plan).

## The pipeline (canonical, from the owner's diagram)

```
User Intent ─► LLM(plan, ↔human) ─► Plan ─► LLM(breakdown) ─► Task[] ─► LLM(match) ─► Recipe[] ─► compose ─► Feature
                  stage 1            (doc)    stage 2          (doc)     stage 3      (catalog)   stage 4
```

| # | Stage | Actor | Deterministic? | Gate |
|---|---|---|---|---|
| 1 | Intent → Plan | LLM ↔ Human | no | **human approval** |
| 2 | Plan → Tasks (breakdown) | LLM | no | task-list validator |
| 3 | Tasks → Recipes (match) | LLM | no | catalog + compiler |
| 4 | Recipes → Feature (compose) | **deterministic** | **yes** | the compiler |

**This pass owns the middle (2–3).** Stage 1 = the existing doc engine; stage 4 + recipe internals =
another session.

## Settled findings

1. **The recipe is the commit point.** Three LLM stages sit above it; everything below is deterministic
   and owned. A bad decomposition doesn't emit bad code — it produces a recipe that fails to compile,
   and the error is the repair signal. The type-safe seam validates the agent **for free**.

2. **The decomposition is a staged ladder, not a one-shot jump.** Each rung is a more-constrained typed
   artifact (intent → approved plan → task list → recipes → code); the agent's freedom shrinks at each
   step. This *is* the "wrap the agent in deterministic structure" thesis, applied as a ladder.

3. **Every stage has (or can have) a deterministic validator.** Plan → doc engine; task list → a
   validator; recipe → the compiler. No stage rides on vibes. (Modeling the task list as its own
   doctype would give it the doc engine's validation for free — **deferred**: owner wants no doc engine
   for now.)

4. **The breakdown is multi-round: milestone → phase → task.** Progressive refinement; only the leaf
   (task) round is recipe-grained. A higher unit owns the next.

5. **A task is not a recipe — the mapping is many-to-one.** Worked on Projects: five CRUD verbs
   (List/Get/Add/Update/Delete) collapse onto **three** endpoint recipes (read / write / delete),
   because Add≡Update in shape (different fills) and List≡Get likewise. The recipe is the reusable
   shape; the task supplies the fills.

6. **No-match is the most important output of stage 3.** A task with no recipe must be *reported* as an
   explicit gap (write the recipe / drop to raw / re-decompose), never force-fit.

7. **The feature-tier recipe catalog already has a written source.** Plan `08-vsa-feature-template.md`
   defines the canonical vertical-slice shape and names **Projects as its reference instance**. The
   conceptual recipes below are its parts, each pinnable to a real shipped file — so recipe correctness
   is *checkable* (render must equal the file).

8. **There are two altitudes of recipe.** The other session's spike recipes are statement-scale
   (`Loop`, `Define`); the diagram/Projects recipes are feature-scale (model, service, endpoint). Same
   compose mechanism; the **declaration tier** (spike backlog #7–11) is the bridge. The middle (this
   pass) is altitude-agnostic — it produces a task→recipe mapping regardless.

## Course-correction — decompose toward intent, not implementation (the altitude)

The Scheduled Runs run (v1→v3) exposed a **direction** error, not just bugs. We are decomposing a plan
into tasks that **must eventually map to recipes** — and a recipe **encodes the HOW**. So the decomposition
must stop at **intent/result**, and let the *technical shape* (entity? repository? endpoint?) emerge at the
**match** step. Our loop instead resolved intent down into mechanism ("`FlowRegistry.Get(id)?.Phase`"),
doing the recipe's job and the implementer's job inside the *planning* step.

**"Code-grounded review" was mis-named and overreached.** There is no feature code yet — the reviewer read
the *surrounding existing system*, so it was **integration-grounded**. Worse, it reviewed existing-system
*internals* and patched the tasks with them — a **render-altitude review run at decompose time**. The
catches it made divide cleanly:

| Grounding | Example | Belongs in decomposition? |
|---|---|---|
| **Integration contract** | "firing a run *requires* project + flow + **prompt**" | **yes** — shapes the intent unit (the `Prompt` catch was legit) |
| **Implementation internal** | "liveness is `FlowRegistry.Get(id)?.Phase`, null after a crash" | **no** — recipe / implementer; caught later by **compiler + tests** |

**Type the questions** (the loop's fuel) — this is the fix:

- **Intent/result question** ("can a user unfavorite? what happens to a missed fire?") → split/clarify → **drives** decomposition.
- **Integration-contract question** ("what inputs does launching a run need?") → resolve from the seams → **shapes** the unit (light grounding; catches `Prompt`).
- **Implementation question** ("how is 'still running' detected? which phase enum?") → **do not answer** → it's the recipe's. Its appearance is the **floor signal — stop.**

**Flipped stop condition.** Before: *stop when no questions remain* (pulled us to resolve mechanism).
After: **stop a branch when its only remaining questions are implementation questions** — that's exactly
where a recipe takes over. (Supersedes the "sharpened stop condition" recorded in `scheduled-runs.steps.md`.)

**Three validation altitudes we had collapsed into one:**

| Stage | Reviews for | Validator |
|---|---|---|
| **Decompose** | intent coverage — every result/edge-rule has a home, order demoable, nothing is mechanism | judgment (intent reviewer) |
| **Match** | each intent-leaf → a recipe (or explicit gap); fills bindable | catalog + types |
| **Render** | the generated code is correct | **compiler + tests** ← `FlowRegistry.Phase` lives here |

The leaf altitude we want is **recipe-shaped intent/result** — "a schedule durably knows what to run, when,
and whether it's active" / "a due schedule whose prior run is still in flight is skipped" — *not*
"create the `Schedule` entity with fields …". The forward pipeline (plan→tasks→match) wants intent leaves;
the earlier Projects exercise produced *technical* leaves because it ran the **other** direction (reverse-
engineering recipes from known code) — both valid for their purpose, but not the same altitude.

### Three node types — and the layer that was missing

Correcting altitude wasn't enough; the **type** of node matters too. The first intent run produced
**requirements** (properties of the finished feature — "a user can pause a schedule"), which is just the
plan re-listed; it converged in one round precisely because it *discovered no work*. A task is not a
property; it is a **unit of work**.

| Type | Example | Is | Maps to a recipe? |
|---|---|---|---|
| **Requirement / definition** | "a user can pause a schedule" | property of the finished feature; acceptance | no |
| **Task / unit of work** ← the input to *match* | "add the ability to pause/resume a schedule" | a thing to **build**; imperative on a result | **yes** |
| **Implementation** | "`PUT /schedules/{id}/pause` flips `Active`" | mechanism | it *is* code |

The grammar test: requirements are "the user/system can/does…"; tasks are "**add / introduce / give /
connect** <result>". Two reasons the task type matters: (1) only tasks **map to recipes**; (2) only the
task decomposition **surfaces derived / shared work** — the plumbing no single requirement names but
several need (storage, cadence interpretation, the firing trigger, the launch-wiring all fell out of the
Scheduled Runs task run; the requirements run was blind to every one).

**Confirmed pipeline (a layer was missing between plan and tasks):**

```
plan ──► requirements ──► tasks ──► recipes
(prose)  (de-fuzz; the    (units    (the HOW;
          acceptance       of work)  technical shape)
          layer)
```

Worked end-to-end on Scheduled Runs: `scheduled-runs.intent.md` (8 requirements) →
`scheduled-runs.tasks.md` (10 work units, 4 of them derived/shared, all recipe-mappable). The
requirements stay as the **acceptance** each task is checked against.

## Ground-truth pair — Projects (reconciled)

**Reconcile result:** plans `04→07` are accurate to their slices, but the code grew past them —
`UpdateProjectEndpoint` + `DeleteProjectEndpoint` exist (06 put PUT/DELETE out of scope) and the wire
leaf was renamed `Contracts` → `Api`. Treating the **code as truth**, the reconciled spec is a full
CRUD slice: `Project : IEntity { Id, Name, Path }` (Create/Rename/MoveTo guards) · `IProjectRepository`
by composition over `IRepository<Project>` · `Api` leaf (`ProjectDto` + 3 request records) · five
FastEndpoints verbs · `ProjectsModule.EndpointsAssembly`.

**Conceptual recipe set derived from it (R1–R8):**

| Recipe | Produces | Serves |
|---|---|---|
| R1 entity + invariants | `Project : IEntity` with `Create`/`Rename`/`MoveTo` | model task |
| R2 named repo by composition | `IProjectRepository` + `ProjectRepository(inner)` | storage-seam task |
| R3 wire DTO / request | `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest`, `ProjectByIdRequest` | wire-shape tasks |
| R4 read endpoint | `List` (bodyless) + `Get` (route param, 404) | 2 read tasks |
| R5 write endpoint | `Add` + `Update` (guards → uniqueness → mint/mutate → persist → status) | 2 write tasks |
| R6 delete endpoint | `Delete` (404 → remove → 204) | delete task |
| R7 feature module | `ProjectsModule.EndpointsAssembly` | wiring task |
| R8 host registration | register endpoints assembly + repo in Composition | wiring task |

## Map of the decomposition pass (all in `spike/`, all doc-only)

**Canonical — read these first**
| File | What |
|---|---|
| `decomposition-method.md` | The **living method** — pipeline, node types, the typed-question loop, gates, recipe families (the WHY + rules) |
| `decomposition-workflow.md` | The **formalized runnable workflow** — two staged loops, gates, schema (the HOW; ready to become an executable Workflow) |
| `decomposition-findings.md` | Durable conclusions + narrative (this file) |
| `decomposition-tasks.md` | Open decisions (D1–D6) + backlog |

**Pipeline overview & early design**
| File | What |
|---|---|
| `PROMPT-DECOMPOSITION.md` | The whole intent→recipe pipeline + the staged-ladder model + the seam |
| `PLAN-TO-RECIPES.md` | The middle (plan→tasks→recipes) designed: breakdown, match, validators |

**Method tests — three feature shapes**
| Feature (shape) | Files |
|---|---|
| Scheduled Runs (greenfield / create) | `scheduled-runs.md` (plan) · `.steps.md` (technical run + 3-perspective review + v2/v3) · `.intent.md` (requirements) · `.tasks.md` (work units) |
| Project Archiving (modify-existing / modify) | `project-archiving.md` (plan) · `.tasks.md` (requirements→tasks) |
| Project Activity (read-only / projection) | `project-activity.md` (plan) · `.tasks.md` (requirements→tasks) |

**Early Projects exploration (the *other* direction — reverse-engineering recipes from real code)**
| File | What |
|---|---|
| `projects-decomposition.md` · `projects-worked-instance.md` | Reconcile Projects plan↔code → breakdown → conceptual recipes R1–R8 |
| `favorite-artist.plan.md` | A validated `feature-plan` doc-engine instance — earliest worked input |

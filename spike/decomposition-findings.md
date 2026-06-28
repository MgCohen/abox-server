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

## Artifacts produced this pass (all in `spike/`, all doc-only)

| File | What |
|---|---|
| `PROMPT-DECOMPOSITION.md` | The whole pipeline + the staged-ladder model + the seam |
| `PLAN-TO-RECIPES.md` | The middle (stages 2–3) designed: breakdown, match, validators |
| `projects-decomposition.md` | The middle worked on real code: reconcile → breakdown → recipes |
| `favorite-artist.plan.md` | A sample plan (validated `feature-plan` instance) — earlier worked input |
| `decomposition-findings.md` | This file |
| `decomposition-tasks.md` | Open decisions + backlog |

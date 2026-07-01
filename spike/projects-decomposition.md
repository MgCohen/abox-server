> **PARKED EXPLORATION** — paused, not legacy. Anchor + active direction: [README.md](README.md) → *Invariants* + *Document map*. Resume when this thread is picked back up.

# Spike — Decomposition on a ground-truth pair (Projects)

> Branch `claude/stacked-pr-from-109-z7u4th`, stacked on #109. The empirical leg of the middle
> (`PLAN-TO-RECIPES.md`): take a **real plan that is already implemented**, reconcile the two sides,
> then break it down into tasks over several rounds — and only *then* ask what conceptual recipes each
> task needs, checking against the code we already have. **Recipes are looked at conceptually here; a
> task is not one recipe.** **Status — WORKED EXERCISE.**

## Why Projects

`Features/Projects` is the repo's **canonical VSA reference** — plan `08-vsa-feature-template.md` names
it *"the cleanest expression of the vertical-slice idea … treated as the reference."* It is a complete,
bounded CRUD slice with simple logic and a documented plan progression (`04` read-only → `05` storage →
`06` CRUD → `07` consolidation), so **both sides are knowable**: the intent (the plans) and the desired
output (the shipped code).

## Step 1 — Reconcile plan ↔ implementation

The plans are accurate to their slices, but the code grew past them. Treating the **current code as
truth**, the drift is:

| Aspect | Plans 04–07 | Current code | Verdict |
|---|---|---|---|
| List · storage · Get · Add | described (04/05/06) | present | matches |
| Model invariants | `Create`/`Rename` (06), `Path` (07) | `Create(name,path)`, `Rename`, **`MoveTo`**, `RequireName`/`RequirePath` | matches + `MoveTo` |
| Named repo | `IProjectRepository.GetByName` deferred → 07 | `ProjectRepository` by composition over `IRepository<Project>` | matches |
| **PUT / DELETE** | **out of scope** (06) | **`UpdateProjectEndpoint` + `DeleteProjectEndpoint` exist** | **DRIFT — undocumented in a slice plan** |
| Wire leaf | **`Contracts`** (04–08) | renamed **`Api`** | **DRIFT — post-08 rename** |
| Request DTOs | `GetProjectRequest`, `CreateProjectRequest(Name)` | **`ProjectByIdRequest`** (Get+Delete), `CreateProjectRequest(Name,Path)`, **`UpdateProjectRequest`** | **DRIFT — generalized + Path + Update** |
| Framework / granularity | FastEndpoints, per-feature (08) | FastEndpoints, one impl assembly + `Api` leaf | matches |

**Reconciled spec (matches the code) — the trustworthy input:**

> **Projects** — a CRUD vertical slice. **Domain:** `Project : IEntity { Id, Name, Path }` with the
> only mint/mutate doors `Create(name,path)` / `Rename(name)` / `MoveTo(path)`, each guarding non-empty
> (path → `GetFullPath`). **Storage:** `IProjectRepository : IRepository<Project>` adding `GetByName`,
> implemented by **composition** over the one `IRepository<Project>` singleton (never subclassing the
> sealed `JsonRepository<T>`). **Api leaf:** `ProjectDto(Id,Name,Path)`, `CreateProjectRequest(Name,Path)`,
> `UpdateProjectRequest(Id,Name,Path)`, `ProjectByIdRequest(Id)`. **Endpoints (FastEndpoints,
> `internal sealed`, verbs as folders):** `GET /projects` (list), `GET /projects/{id}` (Get, 404),
> `POST /projects` (Add: 400 blank name, 400 blank path, 409 dup-name, 201+Location), `PUT /projects/{id}`
> (Update: 400/400, 404, 409 dup-name≠self, 200), `DELETE /projects/{id}` (Delete: 404, 204). **Module:**
> `ProjectsModule.EndpointsAssembly` for FE discovery. **Host:** register the assembly + the repo.

The two drift items (PUT/DELETE, `Contracts`→`Api`) would, in the real pipeline, be the **reconcile
loop's** output: update the slice plan to record them, or fold them into the `08` template doc. For this
exercise they're just noted, and the reconciled spec above is what we break down.

## Step 2 — Breakdown over several rounds (milestone → phase → task)

The breakdown is **progressive refinement**, not one shot — each round makes the units more actionable.
A higher unit owns the next; the leaf tasks are recipe-grained.

### Round 0 — Milestones (coarse, shippable; ≈ the plan progression)

| M | Milestone | Ships |
|---|---|---|
| **M1** | Read-only projects | `GET /projects` over a store → the server↔client seam works |
| **M2** | Full CRUD | Get / Add / Update / Delete + the model invariants that make writes safe |
| **M3** | Path + named-repo seam | `Path` on the model + `IProjectRepository.GetByName` |

(M2+M3 are where the real code lives; M1 is the seam-proving first slice. Storage itself — the generic
`IRepository<T>`/JSON floor — is a **shared** milestone owned outside this feature, consumed here.)

### Round 1 — Phases (ordered steps inside a milestone, e.g. M2)

| Phase | Goal | Done-when |
|---|---|---|
| M2.P1 — model owns its rules | `Project` can't exist nameless/pathless | `Create`/`Rename`/`MoveTo` trim & reject blank |
| M2.P2 — wire shapes | the Api leaf carries every request/response | `ProjectDto` + 3 request records compile in `Api` |
| M2.P3 — read verbs | fetch one / list | `GET /projects`, `GET /projects/{id}` (+404) |
| M2.P4 — write verbs | create / edit / remove | `POST` / `PUT` / `DELETE` with their status codes |
| M2.P5 — wiring | the feature is discovered & bound | `Module.EndpointsAssembly` + Host registration |

### Round 2 — Tasks (actionable leaves, e.g. for M2.P4 write verbs)

| Task | kind | Acceptance (from the code) |
|---|---|---|
| `AddProjectEndpoint` | write-verb (create) | `POST /projects`: 400 blank name, 400 blank path, 409 dup (`GetByName`), `Project.Create`, `Add`, 201 + `CreatedAtAsync<GetProjectEndpoint>` |
| `UpdateProjectEndpoint` | write-verb (edit) | `PUT /projects/{id}`: 400/400, 404 missing, 409 dup≠self, `Rename().MoveTo()`, `Update`, 200 |
| `DeleteProjectEndpoint` | delete-verb | `DELETE /projects/{id}`: 404 missing, `Remove`, 204 |

The same Round-1→Round-2 expansion applies to every phase; M2.P4 is shown because it's the richest. The
point the user flagged shows up here: **a task is not a recipe** — three write/delete tasks, but only
*two* recipe shapes underneath them (a validated write, a delete). The collapse is Step 3.

## Step 3 — What conceptual recipes would produce these tasks

Reading the leaf tasks against the shipped code, the recipe set the slice actually needs is small —
**the verbs collapse onto a few shapes**, and those shapes *are* the `08` VSA template's parts:

| Conceptual recipe | Produces | Tasks it serves | Fills (from the task/plan) |
|---|---|---|---|
| **R1 — Domain entity + invariants** | `Project : IEntity` with `Create`/`Rename`/`MoveTo` + `Require*` guards | model task | entity name, fields, which fields are required |
| **R2 — Named repo by composition** | `IProjectRepository` + `ProjectRepository(inner)` | storage-seam task | entity, the extra query (`GetByName`) |
| **R3 — Wire DTO / request** | `ProjectDto`, `CreateProjectRequest`, `UpdateProjectRequest`, `ProjectByIdRequest` in the `Api` leaf | every wire-shape task | record name, fields |
| **R4 — Read endpoint** | `List` (bodyless) + `Get` (route param, 404) | the 2 read tasks | route, response DTO, by-id? |
| **R5 — Write endpoint** | `Add` + `Update` (presence guards → uniqueness → mint/mutate → persist → status) | the 2 write tasks | route, verb, request DTO, uniqueness rule, success code |
| **R6 — Delete endpoint** | `Delete` (404 → remove → 204) | the delete task | route |
| **R7 — Feature module** | `ProjectsModule.EndpointsAssembly` | wiring task | feature name |
| **R8 — Host registration** | register the endpoints assembly + repo in Composition | wiring task | feature name, the port→impl pair |

**The headline:** five HTTP verbs (List/Get/Add/Update/Delete) need **three** endpoint recipes
(read R4, write R5, delete R6), because Add and Update are the *same shape* with different fills, and
List and Get likewise. This is the inverse of "one task = one recipe": **one recipe spans many tasks**,
parameterized by fills. And because the desired output is the actual Projects code, each recipe's
correctness is checkable — its render must equal the shipped file.

**The deeper finding:** the `08-vsa-feature-template` doc *is the recipe catalog at the feature tier*,
and Projects is its worked instance. R1–R8 are exactly the template's named parts (Domain model · the
repo seam · Contracts/Api leaf · verb endpoints · folded-in Module). So "deriving conceptual recipes
from a ground-truth feature" = **extracting the VSA template's recipe set and pinning each to a real
file it must reproduce.**

## What this exercise establishes

- A **ground-truth pair** (reconciled Projects spec ↔ shipped code) usable to test any breakdown/match.
- The **breakdown is multi-round** (milestone → phase → task); only the leaf round is recipe-grained.
- **Tasks ≠ recipes, many-to-one** — five verbs collapse to three endpoint recipes; the recipe is the
  reusable shape, the task supplies the fills.
- The **recipe catalog at the feature tier already has a written source**: the `08` VSA template. The
  recipe session can mine R1–R8 from it, each validated against a Projects file.

## Open questions

- **Milestone granularity** — for a single feature, are M1–M3 *milestones* or just the feature's
  *phases*? Milestones may only earn the name across features (Projects, then Tasks, then Flows). Lean:
  milestone = a shippable cross-feature step; within one feature, the rounds are phase → task.
- **Where reconcile output lands** — when impl drifts from plan (PUT/DELETE here), does the loop update
  the *slice* plan (04–07) or the *template* plan (08)? Lean: the template owns shape, the slice owns
  intent — PUT/DELETE is shape, so it lands in 08's template/conformance, not a Projects-specific edit.
- **Recipe boundary R4 vs R5** — is "read endpoint" vs "write endpoint" the right cut, or is there one
  `endpoint` recipe with a read/write/delete mode fill? The shipped code leans *distinct shapes* (writes
  carry the guard→uniqueness→persist spine reads don't), but a single moded recipe is plausible. Decide
  when the recipe session builds it against these files.
- **Fill source** — R5's uniqueness rule and success code come from the *plan's* prose, not the task
  title. Confirm the breakdown extracts those into the task (so match has a typed source) rather than
  leaving match to re-read the plan.
# Decomposition — the full worked instance (Projects)

> The middle made **concrete** end-to-end on the Projects ground-truth pair: the complete task tree, the
> recipe catalog with fills, and the task→recipe match — every recipe pinned to a real shipped file it
> must reproduce. Plain spike notes, no doc engine. Reads on top of `decomposition-findings.md` (the
> conclusions) and `projects-decomposition.md` (how we got here).
>
> **Desired output = the real code** in `src/Domain/Projects/` + `src/Features/Projects/`. Every recipe
> below is *checkable*: its render must equal the named file.

## Decision calls made (and what flips them)

| # | Call | Why | Flips if |
|---|---|---|---|
| D1 | **Milestone = cross-feature**; within one feature it's phase → task | "milestone" on a single small feature is thin; Projects is itself one milestone of the rebuild (L4→L7) | we want named milestones *inside* a feature — then add a third level |
| D2 | **Distinct read / write / delete endpoint recipes** (R4/R5/R6) | writes carry a guard→uniqueness→persist spine reads don't; the shapes genuinely differ | we prefer one `endpoint` recipe with a read/write/delete **mode** fill — collapses R4–R6 into one |
| D5 | **The recipe catalog publishes the `kind` vocabulary**; tasks draw kinds from it | a task can't name a kind no recipe serves | we want loose string-matching at the match step instead |

## Round 1 — Phases (shippable, dependency-ordered)

Projects as a whole is one rebuild milestone; inside it, three phases:

| Phase | Goal | Ships |
|---|---|---|
| **P1 Foundations** | the model, storage seam, wire shapes, and wiring exist | nothing callable yet, but everything below builds on it |
| **P2 Read path** | fetch one / list many | `GET /projects`, `GET /projects/{id}` |
| **P3 Write path** | create / edit / remove | `POST` / `PUT` / `DELETE /projects` |

## Round 2 — Tasks (actionable leaves, each pinned to its file)

| Task | kind | Target file | Acceptance (from the code) |
|---|---|---|---|
| **T1** Project entity + invariants | `entity` | `src/Domain/Projects/Project.cs` | `Project : IEntity { Id, Name, Path }`; doors `Create(name,path)`/`Rename(name)`/`MoveTo(path)`; `RequireName`/`RequirePath` trim, reject blank, `Path → GetFullPath` |
| **T2** Named repository (composition) | `repo` | `IProjectRepository.cs`, `ProjectRepository.cs` | `IProjectRepository : IRepository<Project>` adds `GetByName`; impl composes the injected `IRepository<Project>`, delegates the 5 base ops, never subclasses `JsonRepository<T>` |
| **T3** Wire shapes (×4) | `wire` | `Api/{ProjectDto, CreateProjectRequest, UpdateProjectRequest, ProjectByIdRequest}.cs` | `ProjectDto(Id,Name,Path)`; `CreateProjectRequest(Name,Path)`; `UpdateProjectRequest(Id,Name,Path)`; `ProjectByIdRequest(Id)` — all in the `Api` leaf, anemic |
| **T4** Feature module | `module` | `Module/ProjectsModule.cs` | `static Assembly EndpointsAssembly => typeof(ListProjectsEndpoint).Assembly` |
| **T5** Host registration | `wiring` | `Host/Composition.cs` | bind `IProjectRepository → ProjectRepository`; add the module's `EndpointsAssembly` to FE discovery; consume the shared `IRepository<Project>` floor (owned by storage, not here) |
| **T6** List endpoint | `read-verb` | `List/ListProjectsEndpoint.cs` | `EndpointWithoutRequest`; `GET /projects`; `GetAll` → `ProjectDto[]` |
| **T7** Get endpoint | `read-verb` | `Get/GetProjectEndpoint.cs` | `Endpoint<ProjectByIdRequest,ProjectDto>`; `GET /projects/{id}`; `GetById` → 404 or `ProjectDto` |
| **T8** Add endpoint | `write-verb` | `Add/AddProjectEndpoint.cs` | `POST /projects`; 400 blank name, 400 blank path, 409 dup (`GetByName`), `Project.Create`, `Add`, 201 + `CreatedAtAsync<GetProjectEndpoint>` |
| **T9** Update endpoint | `write-verb` | `Update/UpdateProjectEndpoint.cs` | `PUT /projects/{id}`; 400/400, 404 missing, 409 dup≠self, `Rename().MoveTo()`, `Update`, 200 |
| **T10** Delete endpoint | `delete-verb` | `Delete/DeleteProjectEndpoint.cs` | `DELETE /projects/{id}`; 404 missing, `Remove`, 204 |

Dependencies: T1 ← T2 ← {T6…T10}; T3 ← {T6…T10}; T4,T5 ← {T6…T10}. P1 (T1–T5) before P2/P3.

## The recipe catalog (with fills)

Eight conceptual recipes. `kind` is the join key the catalog publishes (D5). Each lists the file(s) its
render must reproduce.

| Recipe | kind it serves | Produces / reference file | Fills (typed) |
|---|---|---|---|
| **R1** entity + invariants | `entity` | the `Project` record · `Project.cs` | name; fields `[(Name,string,req),(Path,string,req,normalize=FullPath)]`; doors `[Create(name,path),Rename(name),MoveTo(path)]` |
| **R2** named repo by composition | `repo` | `IProjectRepository`+`ProjectRepository` · `*Repository.cs` | entity; extra queries `[GetByName(name)]` |
| **R3** wire shape | `wire` | a record in the `Api` leaf · `Api/*.cs` | record name; fields. *(invoked 4×)* |
| **R4** read endpoint | `read-verb` | `List`/`Get` · `List|Get/*.cs` | route; response DTO; `byId?` (adds `{id}` + 404). *(invoked 2×)* |
| **R5** write endpoint | `write-verb` | `Add`/`Update` · `Add|Update/*.cs` | route; verb (POST/PUT); request DTO; presence-guards `[name,path]`; uniqueness `(GetByName, excludeSelf?)`; door (`Create` / `Rename+MoveTo`); success (`201+Location` / `200`). *(invoked 2×)* |
| **R6** delete endpoint | `delete-verb` | `Delete` · `Delete/*.cs` | route (404 → `Remove` → 204) |
| **R7** feature module | `module` | `ProjectsModule` · `Module/*.cs` | feature name; anchor endpoint type |
| **R8** host registration | `wiring` | the Composition edits · `Composition.cs` | feature name; port→impl `(IProjectRepository→ProjectRepository)`; endpoints assembly |

## The match (task → recipe, with the collapse)

| Task | → Recipe | Invocation fills |
|---|---|---|
| T1 | **R1** | Project; Name+Path; Create/Rename/MoveTo |
| T2 | **R2** | Project; GetByName |
| T3 | **R3 ×4** | the four `Api` records |
| T4 | **R7** | Projects; `ListProjectsEndpoint` |
| T5 | **R8** | Projects; IProjectRepository→ProjectRepository; EndpointsAssembly |
| T6 | **R4** | `/projects`; ProjectDto; byId=false |
| T7 | **R4** | `/projects/{id}`; ProjectDto; byId=true |
| T8 | **R5** | `/projects`; POST; CreateProjectRequest; guards[name,path]; GetByName,excludeSelf=false; Create; 201+Location |
| T9 | **R5** | `/projects/{id}`; PUT; UpdateProjectRequest; guards[name,path]; GetByName,excludeSelf=true; Rename+MoveTo; 200 |
| T10 | **R6** | `/projects/{id}` |

**The collapse (the headline):** 10 tasks invoke **8 recipes**, and the **5 HTTP verbs (T6–T10) ride on
just 3 endpoint recipes** (R4 read ×2, R5 write ×2, R6 delete ×1). The recipe is the reusable shape; the
task supplies the fills. This is the inverse of one-task-one-recipe.

## Gaps & dependencies (the honest residue)

| Item | Status |
|---|---|
| Shared storage floor (`IRepository<Project>`, `JsonRepository<T>`, JSON store) | **dependency, not a recipe here** — owned by the storage milestone (plan 05); T5 consumes it |
| Provisional inline presence-guards (the 400s in R5) | **known-provisional fill** — plan says these migrate to a validation **Step** (ADR 0009); R5 bakes them in for now, flagged |
| Tests (Unit `ProjectTests`/`ProjectRepositoryTests`, Wire) | **out of scope** — production-code recipes only; tests are their own catalog (the Rulebook/test machinery), a separate decomposition |

## Coverage check (deterministic)

- **Every reconciled-spec element has a task:** entity ✓T1 · named repo ✓T2 · Api leaf ✓T3 · 5 endpoints
  ✓T6–T10 · module ✓T4 · host ✓T5. **Complete.**
- **Every task maps to a recipe or a flagged item:** T1–T10 → R1–R8; storage floor → dependency; tests →
  out of scope. **No silent gap.**

## What this proves

1. The middle **runs end to end on real material** — a real plan reconciled to real code, broken to
   tasks, matched to a recipe set, with full coverage and the gaps named.
2. **Tasks ≠ recipes, many-to-one** is concrete: 10 tasks → 8 recipes, the 5 verbs → 3 endpoint shapes.
3. The recipe set R1–R8 is **checkable** — each names the exact file its render must equal, so a future
   compose pass (other session) has a ready oracle.

## Residue for next

- The fills for R5 carry the real logic (uniqueness, success code) — they come from the **plan's prose**,
  not the task title. Confirms D4's lean: breakdown should *extract* these into the task so match has a
  typed source. Not yet done here (fills were read straight from the code).
- R4-vs-R5 (D2) held up — writes really do carry a spine reads lack. A moded single `endpoint` recipe
  stays a viable alternative; decide it when the recipe session renders against these files.
- A **test recipe catalog** is a whole separate decomposition (the gaps table) — worth its own pass.

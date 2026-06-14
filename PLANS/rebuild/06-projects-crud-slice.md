# Projects CRUD slice — full CRUD on the storage floor, model owns its rules

Status: **proposed (2026-06-14).** Self-contained — readable without prior context.
Grows the read-only [Projects slice](04-projects-slice.md) onto writes (List / Get /
Add) by **executing [05](05-storage-repository.md)'s Phase 3**, not by changing its
shape. Endpoints stay on the generic `IRepository<Project>` (05's floor); the
`Project` model finally owns its creation invariant; the new endpoints ship as
FastEndpoints ([ADR 0009](../../design/adr/0009-fastendpoints-http-boundary.md)).

> **Executes 05's Phase 3.** [`05-storage-repository.md`](05-storage-repository.md)
> built the storage floor and put the Projects *read* on `IRepository<Project>`,
> sketching create/edit as "a one-call use case over `IRepository<Project>.Add` /
> `.Update`." This doc fills that sketch in: `GET /projects/{id}`, `POST /projects`,
> the model's invariant, and the request DTOs.
>
> It deliberately does **not** introduce a domain-named `IProjectRepository` yet.
> 05's decision #2 reserves a named sub-interface for the *second* project-specific
> query; today there is exactly one (name uniqueness on create), so it stays inline.
> When the seam does land, build it by **composition** over `IRepository<Project>`,
> never by subclassing the `sealed JsonRepository<T>` — see *The deferred seam*.

## Why this, why now

04 proved the server↔UI seam with a read-only list; 05 gave that list durable
storage behind `IRepository<Project>` + a JSON store, replacing the stub with a
`ProjectSeeder`. The trigger both 04 and 05 named for the next step — **"create/edit
moves into the UI"** — is now here: the UI needs to add a project and fetch one by
id. That is 05's Phase 3, verbatim.

Two things must be true for writes to be safe *and* to keep the transport thin:

1. **The model owns its invariant.** A project cannot exist nameless. With reads
   only, `Project` was an anemic record (04); the moment `Add` exists, "a valid
   project" needs **one** enforced definition or the rule scatters across handlers.
   So `Project` gains `Create` / `Rename`, and the non-empty guard lives there.
2. **Cross-entity rules stay orchestration.** Name uniqueness needs the *store*, so
   it is not an intrinsic model invariant — it lives in the `Add` handler, over
   `IRepository<Project>.GetAll`. That is the one project query today; per 05
   decision #2 it does not yet earn a named sub-interface.

We keep 05's settled call: **storage is infrastructure; use cases bind
`IRepository<Project>` directly.** No domain-named persistence port this slice.

## The model — rules move onto `Project`

`Project` stops being an anemic record and owns the one invariant that makes "add"
safe:

```csharp
// src/Domain/Projects/Project.cs
using ABox.Infrastructure.Storage;

namespace ABox.Domain.Projects;

public sealed record Project : IEntity
{
    public Guid Id { get; init; }
    public required string Name { get; init; }

    // The only doors that mint/mutate a Project — the invariant is enforced here, so an
    // invalid Project cannot exist. Deserialization rehydrates already-valid rows via init setters.
    public static Project Create(string name) => new() { Id = Guid.NewGuid(), Name = Require(name) };
    public Project Rename(string name) => this with { Name = Require(name) };

    private static string Require(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length > 0
            ? trimmed
            : throw new ArgumentException("Project name is required.", nameof(name));
    }
}
```

`Project : IEntity` and the `Guid` id are unchanged from 05. The server-only `Path`
stays **deferred** (05): adding it now would commit consumer-less, machine-specific
seed paths — it lands with the flow-launch consolidation that gives it a real
consumer. `Rename` ships on the model now (it is the same one-line guard) even though
no `PUT` endpoint binds it yet — it is free and keeps both write doors in one place.

> **Blast radius on the model.** This flips `Project` from the positional record
> `Project(Guid Id, string Name) : IEntity` to the init-property form above, so
> `ProjectSeeder`'s seed entries switch from positional `new(Guid.Parse(…), "…")` to
> `new() { Id = Guid.Parse(…), Name = "…" }`. The fixed seed GUIDs are unchanged.

## Endpoints — FastEndpoints (ADR 0009), each binds `IRepository<Project>`

The three endpoints stay pure transport. Per ADR 0009 they are FastEndpoints classes
discovered via `ProjectsModule.EndpointsAssembly` — no `static Map`, no MediatR. The
`Project → ProjectDto` map is a trivial 1:1 today (drops nothing), so it is **inlined**;
a dedicated `ProjectMapping` earns its place only when `Path` arrives and the map
starts dropping a field (the same trigger 04/05 give).

```csharp
// List/ListProjectsEndpoint.cs — bodyless GET, no rules → read + map. (Refactor of 05's endpoint.)
public sealed class ListProjectsEndpoint(IRepository<Project> store)
    : EndpointWithoutRequest<IReadOnlyList<ProjectDto>>
{
    public override void Configure() { Get("/projects"); AllowAnonymous(); }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var all = await store.GetAll(ct);
        await Send.OkAsync([.. all.Select(p => new ProjectDto(p.Id, p.Name))], ct);
    }
}
```

```csharp
// Get/GetProjectEndpoint.cs — route param + the 404 path.
public sealed class GetProjectEndpoint(IRepository<Project> store)
    : Endpoint<GetProjectRequest, ProjectDto>
{
    public override void Configure() { Get("/projects/{id}"); AllowAnonymous(); }

    public override async Task HandleAsync(GetProjectRequest req, CancellationToken ct)
    {
        if (await store.GetById(req.Id, ct) is not { } project)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        await Send.OkAsync(new ProjectDto(project.Id, project.Name), ct);
    }
}
```

```csharp
// Add/AddProjectEndpoint.cs — the only one that orchestrates: presence guard → uniqueness → mint → persist → 201.
public sealed class AddProjectEndpoint(IRepository<Project> store)
    : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override void Configure() { Post("/projects"); AllowAnonymous(); }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? string.Empty;

        // Provisional field guard so a blank name is a clean 400, not a 500 from the model's throw.
        // Migrates to a Step (ADR 0009); the model keeps Require() as the last line of defense.
        if (name.Length == 0)
        {
            AddError(r => r.Name, "Project name is required.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Uniqueness needs the store → orchestration, lives here, not on the model. The one project
        // query today; per 05 #2 it does not yet justify a named sub-interface (see The deferred seam).
        var all = await store.GetAll(ct);
        if (all.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            AddError(r => r.Name, "A project with that name already exists.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var project = Project.Create(name);          // intrinsic invariant enforced by the model
        await store.Add(project, ct);
        await Send.CreatedAtAsync<GetProjectEndpoint>(
            new { id = project.Id }, new ProjectDto(project.Id, project.Name), ct);
    }
}
```

The three-way split is literal: the **endpoint** orchestrates (presence, uniqueness,
persist, HTTP shape) and binds the floor; **`Project`** is business logic (can't exist
nameless) with zero storage knowledge; **`JsonRepository<Project>`** (05) is infra.
Reads stay flat — no service, no handler, no MediatR. We deliberately do **not** add an
`IProjectService` pass-through: for reads it is an identity wrapper. A service earns its
place only when Project grows a multi-step workflow.

## Contracts — every request the typed client builds is a wire shape

The typed Blazor client constructs requests, so request DTOs are wire shapes and live
in `Contracts` alongside the response. `GetProjectRequest` exists for FastEndpoints
route binding (`{id}` → `req.Id`); the client's typed surface is still a `Guid` arg.

```csharp
// ABox.Features.Projects.Contracts
public sealed record ProjectDto(Guid Id, string Name);           // exists (04/05)
public sealed record CreateProjectRequest(string Name);          // new
public sealed record GetProjectRequest(Guid Id);                 // new — FE route binding
```

Requests stay **anemic** — no behavior, no validation attributes (validation is a Step;
the intrinsic invariant is on `Project`). **No `ListProjectsRequest`** ships — paging
is speculative until the UI needs it (YAGNI); `GET /projects` stays bodyless and grows a
typed request only when filters/pagination become real.

## The deferred seam — `IProjectRepository`, when the second query lands

05 decision #2 is the rule: **don't widen the generic base, and don't introduce a named
sub-interface until a second real query needs it.** Today there is one (uniqueness),
handled inline. When a second project-specific query or a multi-step workflow arrives
("projects by tag", "archive + reindex"), introduce:

```csharp
// ABox.Domain.Projects/IProjectRepository.cs  — only when the 2nd query lands
public interface IProjectRepository : IRepository<Project>
{
    Task<Project?> GetByName(string name, CancellationToken ct = default);
}
```

and implement it by **composition over the injected `IRepository<Project>`**, *not* by
subclassing `JsonRepository<T>`:

```csharp
internal sealed class JsonProjectRepository(IRepository<Project> inner) : IProjectRepository
{
    public async Task<Project?> GetByName(string name, CancellationToken ct = default) =>
        (await inner.GetAll(ct)).FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    // GetAll/GetById/Add/Update/Remove delegate to inner
}
// DI: services.AddSingleton<IProjectRepository, JsonProjectRepository>();
```

`inner` resolves to the one open-generic `JsonRepository<Project>` singleton the seeder
already uses, so there is a **single load-once cache** — no coherence hazard. Crucially
this **keeps `JsonRepository<T>` sealed**: subclassing would force unsealing the floor
*and* a three-way DI alias to reconcile two caches over one `projects.json` — accidental
complexity this slice avoids entirely by not building the seam at all yet.

## Field validation lives in Steps (settled — not an open decision)

ADR 0009 already decided this: **we do not define `Validator<T>` / a FastEndpoints
validation pipeline; validators are Steps (R-SPINE).** So richer field rules (name length,
no path separators) attach as a **Step** ahead of the handler when they are needed. This
slice ships with two guards and no Step yet:

- the model's `Require()` non-empty invariant (defense-in-depth, the last line), and
- a **provisional** inline presence check in `AddProjectEndpoint` returning a clean `400`.

Both are superseded by the Step when it lands; the model guard stays. The inline check is
labeled provisional so it is never mistaken for the settled validation home.

## Scope

**In:** `Project` invariants (`Create` / `Rename`) + the record-shape flip; `ProjectSeeder`
updated to the new shape; `CreateProjectRequest` / `GetProjectRequest`; `GET /projects`
(refactored, behavior identical), `GET /projects/{id}` (new), `POST /projects` (new), all
as FastEndpoints.

**Out (deliberately):** `IProjectRepository` (deferred to the 2nd query — *The deferred
seam*); `PUT` / `DELETE` (`Rename` is on the model, no endpoint yet); the server-only
`Path` + flow-launch consolidation (deferred per 05); `ProjectMapping` (inlined until
`Path` makes the map non-trivial); Steps-based field validation (ADR 0009, later);
`ListProjectsRequest` / paging; re-keying `?project=` from name to id.

## Blast radius (edits to existing code)

1. `src/Domain/Projects/Project.cs` — positional record → init-property record +
   `Create` / `Rename`.
2. `src/Features/Projects/Module/ProjectSeeder.cs` — seed entries to object-initializer
   syntax (positional ctor is gone); fixed GUIDs unchanged.
3. `src/Features/Projects/List/ListProjectsEndpoint.cs` — unchanged behavior; touched only
   if the inline map is normalized.
4. **New:** `GetProjectEndpoint`, `AddProjectEndpoint`, `CreateProjectRequest`,
   `GetProjectRequest`.
5. `ProjectsModule` — no change: new endpoints are auto-discovered via `EndpointsAssembly`
   (ADR 0009); `AddProjects()` still registers only the seeder. **No new DI** — the
   endpoints bind the existing open-generic `IRepository<Project>`.
6. `ABox.slnx` / csprojs — touched only if the assembly collapse (Phase 4) is taken.

## Phased implementation plan

Each phase: warning-free build + green tests + one coherent commit, per the per-layer gate.

**Phase 1 — model.** `Project` invariants; update `ProjectSeeder` to the new shape. Unit
tests: `Create` / `Rename` reject blank and trim surrounding whitespace.

**Phase 2 — refactor List (no behavior change).** The existing `GET /projects` wire test —
now asserting against the seeded `IRepository<Project>` (per
[`test-authoring.md`](../test-authoring.md)) — stays green through the touch; it is the
regression guard.

**Phase 3 — Get + Add.** New endpoints + `Contracts` request DTOs. Wire tests, written per
[`test-authoring.md`](../test-authoring.md) (assert **state** against the seeded store,
swap doubles via `ConfigureTestServices`, AAA bodies): `GET /projects/{id}` hit + 404;
`POST /projects` creates (201 + `Location`), rejects blank name (400), rejects duplicate
(409); the created project round-trips through a subsequent `GET`.

**Phase 4 — assembly collapse (optional, recommended).** Mechanical: merge `List` / `Get`
/ `Add` / `Module` into one `ABox.Projects` assembly, update `.slnx` + the Host reference,
keep namespaces. Its own commit so the diff is a pure restructure.

## Assembly granularity (recommended: collapse)

Today Projects is three assemblies (`ABox.Projects.Contracts` / `.List` / `.Module`);
adding `Get` + `Add` per the existing per-verb split would make five for one CRUD feature.
The validated VSA reference closest to us — **ardalis/RiverBooks** — folds a whole bounded
context into **one** assembly, layered by folder + `internal`. Recommend collapsing
`List` / `Get` / `Add` / `Module` into a single `ABox.Projects` (folders inside,
namespaces unchanged: `ABox.Features.Projects.{List,Get,Add,Module}`), keeping
`ABox.Projects.Contracts` and `ABox.Domain.Projects` separate. The arch bands match on
namespace, so the layer rules are unaffected; ADR 0009's Host→`*.Module`-only rule is
preserved by keeping `ProjectsModule.EndpointsAssembly` pointing at the one feature
assembly. Skip the collapse and the rest of the slice still stands — it is orthogonal.

## Decisions captured

- **Endpoints bind `IRepository<Project>`** (05's floor); no domain-named persistence port
  this slice.
- **`IProjectRepository` is deferred** to the second project query (05 #2). When it lands,
  build it by **composition** over `IRepository<Project>` — never by subclassing the
  `sealed JsonRepository<T>` (which would force unsealing the floor + cache-coherence
  aliasing).
- **`Project` owns its invariants** (`Create` / `Rename`); uniqueness is **orchestration**
  in the `Add` handler (it needs the store), not a model invariant.
- **All request DTOs live in `Contracts`**; requests stay anemic. No speculative
  `ListProjectsRequest`.
- **Endpoints are FastEndpoints** (ADR 0009); reads stay flat — no service / handler /
  MediatR; no `IProjectService` pass-through.
- **Field validation lives in Steps** (ADR 0009), not `Validator<T>`; the model keeps the
  non-empty invariant, and `Add` carries a provisional inline presence guard (400) until
  the Step lands.
- **`Path` stays deferred** (05) until the flow-launch consolidation gives it a consumer;
  `ProjectMapping` arrives with it.

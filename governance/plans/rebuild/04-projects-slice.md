# Projects feature — vertical slice spec

Status: **built (2026-06-13).** Self-contained spec — readable without prior
context. Implements the first real end-to-end feature of the rebuild: a server
API that lists the projects the orchestrator can work on, and the model behind
it. Storage was deliberately stubbed (see The stub).

> **Superseded — storage (2026-06-13):** [`05-storage-repository.md`](05-storage-repository.md)
> replaced the stub with real persistence and **revised the "port in Domain"
> decision below.** Storage is now an *infrastructure* concern: the `IProjects`
> port and `StubProjects` are gone; `Project : IEntity` is persisted by the
> open-generic `IRepository<Project>` (JSON store) resolved in the endpoint. The
> Domain↔DTO split, the GUID-id reasoning, and the additive-feature framing all
> still hold; only the storage seam moved out of Domain.

## Why this feature

The rebuild needs one complete vertical slice to prove the **server ↔ client
(Blazor UI) seam** end to end, before any agent/flow machinery is involved.
"Projects" is the ideal first slice: it is genuinely independent (it returns a
list — no agents, no PTY, no flows), yet it produces the exact thing the UI is
blocked on: a **Contracts assembly of wire DTOs** the UI can reference, and a
working HTTP endpoint to call.

It also has a real future: every other feature (flows, git, tasks) is *scoped by
a project*, so the list this feature returns becomes the filter/selector the
rest of the UI hangs off. That wiring is **out of scope here** (see Scope).

## Scope

**In:**
- A new `Features/Projects/` slice: Contracts (wire DTO) → endpoint → module.
- A new `Domain/Projects` area: the `Project` model + the `IProjects` port.
- `GET /projects` returning the projects as wire DTOs.
- A **stub** implementation that produces the list in memory on demand.

**Out (deliberately, to keep the blast radius minimal):**
- **Persistence.** No file, no database. The stub fabricates the list; real
  storage is a later, isolated change behind the `IProjects` port.
- Migrating the flow-launch path (`StartEndpoint` → `Resolve`) onto this
  feature. It keeps using the legacy `IProjectRegistry` untouched.
- Re-keying the `?project=` parameter on git/flows endpoints from name to id.
- Any create / edit / delete of projects (read-only this slice).
- Any UI code (the UI is a sibling repo; it only consumes the Contracts DTO +
  the endpoint).

## The model — two types, by layer

```csharp
// Domain — the server-side model. IProjects returns this.
namespace ABox.Domain.Projects;
public sealed record Project(Guid Id, string Name);

// Contracts — the wire DTO. GET /projects returns this; the UI references it.
namespace ABox.Features.Projects.Contracts;
public sealed record ProjectDto(Guid Id, string Name);
```

They are identical *today*, yet they are two types — because they live in two
layers with a one-way dependency: **Domain must not reference a feature's
Contracts**, so the domain port returns a domain `Project`, and the endpoint
maps it to the wire `ProjectDto`. This is the same split Flows already uses
(`FlowSnapshot` → `FlowView` via `FlowMapping`). It also lets the two diverge:
when storage arrives, `Project` gains a server-only **`Path`** (where the server
finds the project on disk) that **never** reaches `ProjectDto` or the UI.

| Field  | Role                                   | Mutable? | On the wire? |
|--------|----------------------------------------|----------|--------------|
| `Id`   | stable reference / filter key (GUID)   | never    | yes          |
| `Name` | human display label                    | yes      | yes          |
| `Path` | where the server finds it — **later**, with storage | yes | **no** |

### Why a GUID id (not the name, not an integer)

- **Not the name:** names change; anything referencing a project by name would
  orphan on rename. The id must be stable and decoupled from the label.
- **GUID over integer:** a GUID needs no central counter — `Guid.NewGuid()` is
  collision-free with zero coordination, which fits a stub (and later a
  file-backed store) with no database. Integers want an auto-increment
  authority; they become the natural choice *only if* this graduates to a
  relational DB.

## Layering & responsibilities

```
Domain/Projects        Project (model) + IProjects (port)          ← the contract the feature depends on
Features/Projects
  Contracts            ProjectDto (+ Request/Response when needed) ← the leaf the UI references
  List                 endpoint: IProjects.List() → ProjectDto
  Module               StubProjects : IProjects  +  Add/MapProjects
```

- **`IProjects`** lives in **Domain**, alongside `IFlowHistory` /
  `IAgentFactory` (the codebase's convention for ports). It returns domain
  `Project`s:
  ```csharp
  public interface IProjects
  {
      IReadOnlyList<Project> List();
  }
  ```
  (A future `Resolve(Guid id) → path` method lands here too, once this feature
  absorbs the flow-launch path — server-side, never on the wire.)
- **`Contracts`** holds the wire shapes the UI binds to: `ProjectDto` now;
  Request/Response records join it as operations that need them are added (List
  needs neither — it's a bodyless GET returning an array).
- **`List`** is the endpoint; it depends on Domain (`IProjects`, `Project`) and
  Contracts (`ProjectDto`) and maps one to the other, dropping anything
  server-only.
- **`Module`** holds the stub impl and the DI + routing wiring.

## The stub

No persistence this slice. `StubProjects` implements `IProjects` by returning a
fixed in-memory list each call — the analogue of `StubPullRequests` behind Git's
port:

```csharp
internal sealed class StubProjects : IProjects
{
    public IReadOnlyList<Project> List() =>
    [
        new(Guid.Parse("3f2a8c10-9b4e-4d21-a7c6-1e0f5b8d2a44"), "Card Framework"),
        new(Guid.Parse("b71d4e92-0c3a-4f88-9a15-6d2e7c4b1f03"), "Scaffold"),
    ];
}
```

Fixed GUIDs (not `Guid.NewGuid()` per call) so the list is stable across
requests — the wire test and the UI see the same ids every time. These seed
entries are what make the slice demonstrate something end to end. Real storage
(file, then EF Core + SQLite) replaces this class behind the unchanged port;
that is when a JSON `JsonSerializerContext` or a `DbContext` enters the picture —
not now. Label the class clearly as a stub so it's never mistaken for settled
design.

## The API

```
GET /projects
200 → [ { "id": "3f2a8c10-…", "name": "Card Framework" }, … ]
```

No request body, no auth (transport is Tailscale-only per the feature map). The
client holds the `id` as the stable handle and shows the `name`.

## Assembly + file layout

Namespaces are `ABox.Domain.Projects` and `ABox.Features.Projects.*`; assembly
names drop the `Features` segment (`ABox.Projects.*`), per `Directory.Build.props`.

```
src/Domain/Projects/   ABox.Domain.Projects.csproj            (leaf — no refs)
  Project.cs           record Project(Guid Id, string Name)
  IProjects.cs         interface IProjects { IReadOnlyList<Project> List(); }

src/Features/Projects/
  Contracts/   ABox.Projects.Contracts.csproj                 (leaf — the UI references THIS)
    ProjectDto.cs        record ProjectDto(Guid Id, string Name)
  List/        ABox.Projects.List.csproj                       (refs Domain.Projects, Contracts)
    ListProjectsEndpoint.cs   GET / → IProjects.List() mapped to ProjectDto
  Module/      ABox.Projects.Module.csproj                     (refs Domain.Projects, List)
    StubProjects.cs      internal sealed class StubProjects : IProjects
    ProjectsModule.cs    AddProjects() + MapProjects()
```

### Reference endpoint + module (illustrative)

```csharp
public static class ListProjectsEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) =>
        projects.MapGet("/", (IProjects store) =>
            Results.Ok(store.List().Select(p => new ProjectDto(p.Id, p.Name))));
}

public static class ProjectsModule
{
    public static IServiceCollection AddProjects(this IServiceCollection services)
    {
        services.AddSingleton<IProjects, StubProjects>();
        return services;
    }

    public static void MapProjects(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/projects");
        ListProjectsEndpoint.Map(projects);
    }
}
```

## Blast radius (complete list of edits to existing code)

1. `src/Host/Program.cs` — delete the inline
   `app.MapGet("/projects", (IProjectRegistry …) …)` line; add `app.MapProjects();`.
2. `src/Host/Composition.cs` — add `services.AddProjects();`. **Leave** the
   existing `IProjectRegistry` registration — the flow-launch path still uses it.
3. `src/Host/ABox.Host.csproj` — add a `ProjectReference` to
   `ABox.Projects.Module`.
4. `ABox.slnx` — add the four new `.csproj` (one Domain, three Feature).
5. `tests/Tests/Wire/Support/WireApp.cs` — no new registration needed
   (`StubProjects` is deterministic); update the `/projects` assertion to expect
   the stub's `{id, name}` entries. The existing `FakeProjects`
   (`IProjectRegistry`) stays — flow tests still need it for `Resolve`.

Nothing in `Features/Flows`, `Features/Git`, `Features/Tasks`, or
`Infrastructure/Projects` changes.

## Tests

- **Wire (`tests/Tests/Wire`):** `GET /projects` over `WebApplicationFactory`
  returns the stub's entries as `ProjectDto[]`; assert on `id` + `name`. No fake
  needed — the stub is deterministic.
- **Endpoint/mapping (optional):** `IProjects.List()` (domain) maps to
  `ProjectDto` dropping nothing today; locks the mapping for when `Path` is added.

## Transitional state (and the path out)

This slice leaves the legacy `Infrastructure/Projects` (`IProjectRegistry` +
repo-root `projects.json`, `{ name: path }`) **in place and untouched** — it
still powers `StartEndpoint`'s `Resolve(name) → dir`. The new feature is purely
additive and shares nothing with it.

Consolidation, when chosen, is a contained follow-up:
1. Give `Project` its `Path`; add `Resolve(Guid id) → path` to `IProjects`,
   backed by real storage.
2. Re-key the `?project=` / `StartRunRequest.Project` wire param from name to id.
3. Point `StartEndpoint` at `IProjects`; delete `Infrastructure/Projects` and
   the root `projects.json`.

> **Done.** All three landed in [`07-flow-launch-consolidation.md`](07-flow-launch-consolidation.md)
> plus the follow-up that re-keyed `StartRunRequest` to `ProjectId` (Guid) and dropped the
> absolute-path passthrough — flow-launch now resolves a project strictly by id.

And the stub graduates on its own trigger: when **create/edit projects moves
into the UI**, the server generates `Guid.NewGuid()` on create and persists — a
real store (file, then EF Core + SQLite, with a `JsonSerializerContext` /
`DbContext` at that point) replaces `StubProjects` behind the unchanged port.

## Decisions captured

- **Port in Domain** (`IProjects` next to `IFlowHistory`), not in the UI-facing
  Contracts. Contracts holds wire shapes only (DTO + Request/Response).
- **Two record types** by layer: `Project` (Domain) ↔ `ProjectDto` (Contracts),
  bridged in the endpoint — the same split as `FlowSnapshot` ↔ `FlowView`.
- **GUID id**, name mutable; `Path` deferred until storage and kept server-only.
- **Stubbed storage** — list generated on demand; persistence is a later,
  isolated change behind `IProjects`.
- **Additive, independent feature**; legacy `Resolve` path left untouched.

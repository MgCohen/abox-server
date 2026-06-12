# Projects feature — vertical slice spec

Status: **planned, not yet built.** Self-contained spec — readable without prior
context. Implements the first real end-to-end feature of the rebuild: a server
API that lists the projects the orchestrator can work on, and the model + store
behind it.

## Why this feature

The rebuild needs one complete vertical slice to prove the **server ↔ client
(Blazor UI) seam** end to end, before any agent/flow machinery is involved.
"Projects" is the ideal first slice: it is genuinely independent (it reads a
file and returns a list — no agents, no PTY, no flows), yet it produces the
exact thing the UI is blocked on: a **Contracts assembly of wire DTOs** the UI
can reference, and a working HTTP endpoint to call.

It also has a real future: every other feature (flows, git, tasks) is *scoped
by a project*. So the list this feature returns becomes the filter/selector the
rest of the UI hangs off. But that wiring is **out of scope here** (see Scope).

## Scope

**In:**
- A new, self-contained `Features/Projects/` slice: Contracts → endpoint →
  module → model → store.
- `GET /projects` returning the projects as wire DTOs.
- A persisted, file-backed store keyed by a stable id.

**Out (deliberately, to keep the blast radius minimal):**
- Migrating the existing flow-launch path (`StartEndpoint` → `Resolve`) onto
  this feature's model. It keeps using the legacy registry untouched.
- Re-keying the `?project=` parameter on git/flows endpoints from name to id.
- Any create / edit / delete of projects (no write path — the store is
  read-only this slice).
- Any UI code (the UI lives in a sibling repo and only consumes the Contracts
  DTO + the endpoint).

## The model

Three roles, cleanly separated. **Two record types**, nothing more:

```csharp
// Stored / server-side model. Holds Path. Internal to the feature's Module —
// never crosses into Contracts, so Path never reaches the UI assembly.
public sealed record Project(Guid Id, string Name, string Path);

// Wire DTO. What GET /projects returns. Path is dropped at this boundary.
public sealed record ProjectDto(Guid Id, string Name);
```

| Field  | Role                                   | Mutable?           | On the wire? |
|--------|----------------------------------------|--------------------|--------------|
| `Id`   | stable reference / filter key          | never              | yes          |
| `Name` | human display label                    | yes (rename freely)| yes          |
| `Path` | where the server finds the project     | yes                | **no**       |

### Why a GUID id (not the name, not an integer)

- **Not the name:** names change; anything that referenced a project by name
  would orphan on rename. The id must be stable and decoupled from the label.
- **GUID over integer:** a GUID needs no central counter — `Guid.NewGuid()` is
  collision-free with zero coordination, which fits a file-backed store with no
  database. Integers want an auto-increment authority (a DB); hand-assigning
  them in a file is error-prone. Integers become the natural choice *only if*
  this store later graduates to a relational DB.

## The store

A JSON file keyed by the project id, with `name` + `path` in the value:

```json
{
  "3f2a8c10-9b4e-4d21-a7c6-1e0f5b8d2a44": {
    "id":   "3f2a8c10-9b4e-4d21-a7c6-1e0f5b8d2a44",
    "name": "Card Framework",
    "path": "C:/Unity/Card Framework"
  }
}
```

- **Location:** `~/.abox/projects.json` (the runtime data dir, alongside flow
  history). This is distinct from the repo-root `projects.json` the legacy
  registry reads — see Transitional state.
- **Keyed by id** so the JSON object's unique-key guarantee makes duplicate ids
  *structurally impossible* — no load-time dedupe needed.
- **Id is stored twice** (key + value) so the value deserializes into a
  complete `Project` in one pass (no "reattach id from key" step, which is what
  lets the model stay at two types). The one cost: a load-time guard that
  `key == value.id`, throwing an actionable error on mismatch.
- **Missing file** → empty list (no projects configured yet, not an error).
  **Malformed JSON / bad guid key** → throw with a message naming the file and
  the offending entry.
- **Read-only + cached** for the process lifetime; editing the file needs a
  restart to take effect (acceptable until a write path exists).

## The API

```
GET /projects
200 → [ { "id": "3f2a8c10-…", "name": "Card Framework" }, … ]
```

No request body, no auth (transport is Tailscale-only per the feature map).
The client holds the `id` as the stable handle and shows the `name`.

## Assembly + file layout

Mirrors the established per-feature pattern (cf. `Features/Git`): a Contracts
leaf, an endpoint assembly, a module that wires DI + routing. Namespaces are
`ABox.Features.Projects.*`; assembly names drop the `Features` segment
(`ABox.Projects.*`), per `Directory.Build.props`.

```
src/Features/Projects/
  Contracts/   ABox.Projects.Contracts.csproj      (leaf — no refs; the UI references THIS)
    ProjectDto.cs        record ProjectDto(Guid Id, string Name)
    IProjects.cs         interface IProjects { IReadOnlyList<ProjectDto> List(); }
  List/        ABox.Projects.List.csproj            (refs Contracts)
    ListProjectsEndpoint.cs   GET / → IProjects.List()
  Module/      ABox.Projects.Module.csproj          (refs Contracts, List)
    Project.cs           internal record Project(Guid Id, string Name, string Path)
    JsonProjectStore.cs  internal sealed class JsonProjectStore : IProjects  (reads the file)
    ProjectsModule.cs    AddProjects() + MapProjects()
```

- `IProjects` is the read port (in Contracts, returns the wire DTO). It earns
  its place as the DI/test seam — the wire test swaps a fake `IProjects`.
- `Project` (with `Path`) is **internal to Module**. Path never escapes the
  server.
- A future `Resolve(Guid id) → path` method belongs on `IProjects`, added when
  the flow-launch path migrates onto this feature (out of scope now).

### Reference endpoint (illustrative)

```csharp
public static class ListProjectsEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) =>
        projects.MapGet("/", (IProjects store) => Results.Ok(store.List()));
}
```

```csharp
public static class ProjectsModule
{
    public static IServiceCollection AddProjects(this IServiceCollection services)
    {
        services.AddSingleton<IProjects, JsonProjectStore>();
        return services;
    }

    public static void MapProjects(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/projects");
        ListProjectsEndpoint.Map(projects);
    }
}
```

## Blast radius (the complete list of edits to existing code)

1. `src/Host/Program.cs` — delete the inline
   `app.MapGet("/projects", (IProjectRegistry …) …)` line; add `app.MapProjects();`.
2. `src/Host/Composition.cs` — add `services.AddProjects();`. **Leave** the
   existing `IProjectRegistry` registration — the flow-launch path still uses it.
3. `src/Host/ABox.Host.csproj` — add a `ProjectReference` to
   `ABox.Projects.Module` (next to the Flows/Git/Tasks module refs).
4. `ABox.slnx` — add the three new `.csproj` under the `/src/` folder.
5. `tests/Tests/Wire/Support/WireApp.cs` — register a fake `IProjects` so the
   `/projects` wire assertion is deterministic; update the assertion to expect
   `{id, name}`. The existing `FakeProjects` (which fakes `IProjectRegistry`)
   stays — the flow tests still need it for `Resolve`.

Nothing in `Features/Flows`, `Features/Git`, `Features/Tasks`, or
`Infrastructure/Projects` changes.

## Tests

- **Wire (`tests/Tests/Wire`):** `GET /projects` over `WebApplicationFactory`
  with a fake `IProjects` returns the seeded entries as `ProjectDto[]`; assert
  on `id` + `name`.
- **Store unit:** `JsonProjectStore` parses the keyed shape into `Project`s;
  missing file → empty; mismatched `key`/`value.id` → throws; bad guid key →
  throws.

## Transitional state (and the path out)

This slice intentionally leaves a **second** notion of "project" in place:

| Source | Shape | Read by | Status |
|---|---|---|---|
| repo-root `projects.json` | `{ name: path }` | legacy `IProjectRegistry.Resolve`, used by `StartEndpoint` | L1 scaffolding, untouched |
| `~/.abox/projects.json` | `{ id: { id, name, path } }` | new `JsonProjectStore` | this feature |

That duplication is the price of a minimal blast radius now. The consolidation,
when we choose to do it, is a contained follow-up:

1. Add `Resolve(Guid id) → path` to `IProjects`.
2. Re-key the `?project=` / `StartRunRequest.Project` wire param from name to id.
3. Point `StartEndpoint` at `IProjects`; delete `Infrastructure/Projects` and
   the root `projects.json`.

And the store itself graduates on its own trigger: when **create/edit projects
moves into the UI**, the server generates `Guid.NewGuid()` on create and
persists — at which point a real store (EF Core + SQLite) replaces the JSON
file, behind the unchanged `IProjects` port.

## Decisions captured

- **Per-feature Contracts** (`ABox.Projects.Contracts`), matching Git/Tasks/Flows
  and R-ARCH-2 ("contracts live with the layer that owns them") — not a single
  shared `ABox.Contracts`.
- **Two record types** (`Project`, `ProjectDto`); the id-in-key-and-value store
  shape is what removes the need for a third "entry" type.
- **GUID id**, name mutable, path server-only and never on the wire.
- **Additive, independent feature**; legacy `Resolve` path left untouched.

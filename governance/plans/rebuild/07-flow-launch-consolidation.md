# Flow-launch consolidation — one canonical Project, the registry retires

Status: **built (2026-06-14).** Self-contained — readable without prior context.
Closes the dual-`Project` split that [06](06-projects-crud-slice.md) deliberately
left open: today there are **two** disjoint notions of "project," and only one of
them can launch a flow. This slice makes the stored [`Project`](../../../src/Domain/Projects/Project.cs)
entity the single canonical model, gives flow-launch its directory from that store,
and **deletes** the file-backed `IProjectRegistry`. It executes the "flow-launch
consolidation" that [05](05-storage-repository.md)/[06](06-projects-crud-slice.md)
named as the trigger for the deferred server-only `Path`.

## The problem — two "projects" that share only a word

| | `ABox.Domain.Projects.Project` | `ABox.Infrastructure.Projects` (registry) |
|---|---|---|
| Shape | `(Guid Id, string Name)` | `ProjectEntry(string Name, string Path)` |
| Store | `~/.abox/rebuild/project.json` via `IRepository<Project>` | repo-root `projects.json` (name→path map) |
| Written by | `POST /projects` | hand-edited file |
| Read by | `GET /projects`, `GET /projects/{id}` | [`StartEndpoint`](../../../src/Features/Flows/Start/StartEndpoint.cs) only |

A project created through `POST /projects` has no path and is invisible to flow
launching; a `projects.json` entry that *can* launch a flow can't be created or
listed through the API. The namespaces (`ABox.Domain.Projects` vs
`ABox.Infrastructure.Projects`) read as the same concept in two layers but are two
different things — the legibility hazard called out in review.

**What is actually live on the registry.** Only `Resolve(nameOrPath)` has a
production consumer ([`StartEndpoint.cs:16`](../../../src/Features/Flows/Start/StartEndpoint.cs)).
`List()` and `ProjectsFilePath` are used by **no** production code — only
`ProjectRegistryTests`. So consolidation has to preserve exactly one capability:
**resolve a project reference to the directory a flow runs in.** Everything else on
the registry is dead surface that retires with it.

## The shape after

```
POST /projects {Name, Path}      ──▶  IRepository<Project>           (project.json: Id, Name, Path)
GET  /projects, /projects/{id}   ──▶  IRepository<Project>           (DTO now carries Path)
POST /flows {Project, …}         ──▶  ProjectDirectory.Resolve(ref)  ──▶ IProjectRepository.GetByName ──▶ Project.Path
```

One store. One `Project`. The registry, its interface, its `ProjectEntry`, and
`projects.json`-as-source are gone; existing `projects.json` data is **imported once**
into the store so nothing is lost.

## Why this is the moment the deferred seam lands

[06](06-projects-crud-slice.md) made two forward calls this slice cashes in:

1. **`Path` was deferred "until the flow-launch consolidation gives it a real
   consumer."** This is that consumer. `Path` lands on `Project` now, not speculatively.
2. **`IProjectRepository : IRepository<Project>` with `GetByName` was reserved for
   "the second project-specific query."** Flow-launch resolution *is* that second
   query, and uniqueness-on-create becomes its second caller. So we introduce the
   seam now, **by composition over `IRepository<Project>`** — never by subclassing the
   `sealed JsonRepository<T>` (06's rule: subclassing forces unsealing the floor and
   reconciling two caches over one file).

Nothing here is new architecture — it is the architecture 06 designed, triggered.

## Behavior lock

This is a **rebuild of internals, not behavior** (CLAUDE.md). Project resolution is
*not* a Tier-A oracle invariant; the `projects.json` mechanism is a prototype (Tier-B)
detail we are free to re-author. What must hold across the change:

- `POST /flows` with a known project launches the flow in that project's directory
  (the `--cd <projectDir>` the agent CLIs receive — oracle A8/A9).
- An unknown project reference → `400` with an actionable message (unchanged).
- An absolute path that exists is accepted directly (passthrough preserved — see
  *Resolution semantics*).

## The model — `Path` lands on `Project`

```csharp
// src/Domain/Projects/Project.cs
public sealed record Project : IEntity
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }

    public static Project Create(string name, string path) =>
        new() { Id = Guid.NewGuid(), Name = RequireName(name), Path = RequirePath(path) };

    public Project Rename(string name) => this with { Name = RequireName(name) };
    public Project MoveTo(string path) => this with { Path = RequirePath(path) };

    private static string RequireName(string name) => /* existing non-empty trim guard */ …;

    private static string RequirePath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Project path is required.", nameof(path));
        return System.IO.Path.GetFullPath(trimmed);   // normalize to absolute, mirrors the registry
    }
}
```

- **`Path` is required** and normalized to an absolute path at construction (the
  registry did `GetFullPath` at read-time; we do it once at the door instead).
- **Existence is *not* checked here.** The registry checked directory existence at
  `Resolve`-time, not when listing — a project row outlives a temporarily-missing
  directory, and deserialization must rehydrate rows without touching the disk.
  Existence is verified at resolve-time (below), where the failure is actionable.
- `MoveTo` ships for symmetry with `Rename` (same one-line guard, free); no `PUT`
  binds it yet — same posture 06 took with `Rename`.

## The seam — `IProjectRepository.GetByName` (06's deferred seam, by composition)

```csharp
// src/Domain/Projects/IProjectRepository.cs
public interface IProjectRepository : IRepository<Project>
{
    Task<Project?> GetByName(string name, CancellationToken ct = default);
}

// src/Infrastructure/Projects/JsonProjectRepository.cs — composition, NOT subclassing
internal sealed class JsonProjectRepository(IRepository<Project> inner) : IProjectRepository
{
    public Task<Project?> GetByName(string name, CancellationToken ct = default) =>
        inner.GetAll(ct).ContinueWith(t => t.Result.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)), ct);
    // GetAll/GetById/Add/Update/Remove delegate straight to inner
}
// DI: services.AddSingleton<IProjectRepository, JsonProjectRepository>();
```

`inner` is the one open-generic `JsonRepository<Project>` singleton the endpoints
already bind → a single load-once cache, no coherence hazard (06's analysis verbatim).

**Two consumers justify it immediately:** flow-launch resolution (new) and the
uniqueness check in `AddProjectEndpoint`, which collapses from an inline
`GetAll().Any(…)` scan to `await projects.GetByName(name, ct) is not null`. Both endpoints
move from `IRepository<Project>` to `IProjectRepository`.

## Resolution — `Resolve` survives, its backing store changes

The registry's `Resolve` is the one capability to preserve. It moves to a focused
service over the seam; `StartEndpoint` keeps its `try/Resolve/catch → 400` shape and
only swaps the dependency type.

```csharp
// src/Features/Flows/Start/ProjectDirectory.cs (lives with its one consumer)
public sealed class ProjectDirectory(IProjectRepository projects)
{
    public async Task<string> Resolve(string nameOrPath, CancellationToken ct = default)
    {
        // 1. existing absolute path → passthrough (behavior preserved; ad-hoc dirs still launchable)
        if (Path.IsPathRooted(nameOrPath) && Directory.Exists(nameOrPath))
            return Path.GetFullPath(nameOrPath);

        // 2. resolve by name against the canonical store
        if (await projects.GetByName(nameOrPath, ct) is not { } project)
            throw new InvalidOperationException($"Unknown project \"{nameOrPath}\". …add it via POST /projects…");

        // 3. the stored path must exist on disk now (registry's resolve-time check)
        if (!Directory.Exists(project.Path))
            throw new InvalidOperationException($"Project \"{nameOrPath}\" resolves to {project.Path} but it doesn't exist.");
        return project.Path;
    }
}
```

The three branches are the registry's three branches, re-pointed from a file map to
`GetByName`. `StartEndpoint` injects `ProjectDirectory` instead of `IProjectRegistry`;
its body is otherwise unchanged. `req.Project` stays the existing "name **or** absolute
path" string — **re-keying `?project=` from name to id stays out of scope** (06's list).

## Contracts — `Path` joins the wire shapes

```csharp
public sealed record ProjectDto(Guid Id, string Name, string Path);   // gains Path — UI shows/edits it
public sealed record CreateProjectRequest(string Name, string Path);  // gains Path — required to be launchable
public sealed record GetProjectRequest(Guid Id);                      // unchanged
```

`Project → ProjectDto` is still field-for-field 1:1 (`Id, Name, Path`), so it stays
**inlined** — `ProjectMapping` was triggered by the map "dropping a field," which has
not happened; defer it still. `AddProjectEndpoint` gains a provisional presence guard
for `Path` alongside the existing one for `Name` (same 400-not-500 rationale; both
migrate to the validation Step together per ADR 0009).

## One-time import — no data loss

A boot-time migration shim, modeled on the removed `ProjectSeeder` and **labeled
provisional**:

- An `IHostedService` that runs once on startup. **Guard:** only when the repo store
  is empty *and* `projects.json` exists — so it never fights `POST /projects` or
  re-imports on every boot.
- Parses `projects.json` (the same `Dictionary<string,string>` source-gen context the
  registry used, moved into the importer) and `Add`s each entry as
  `Project.Create(name, path)`. Relative paths normalize via `Create`'s `GetFullPath`.
- Removed at L12 with the rest of the migration scaffolding (it has a one-line *why*
  comment and is named `ProjectsJsonImport` so it is never mistaken for settled design).

`IOrchestratorPaths.ProjectsFile` is **kept** — it is now the importer's input rather
than the registry's. `RepoRoot.Find(… "projects.json")` stays as a root anchor.

## Scope

**In:** `Path` on `Project` (`Create`/`MoveTo` + invariant); `IProjectRepository.GetByName`
+ `JsonProjectRepository` (composition) + DI; `AddProjectEndpoint` uniqueness → `GetByName`
and a provisional `Path` presence guard; `Path` on `ProjectDto`/`CreateProjectRequest`;
`ProjectDirectory` resolver; `StartEndpoint` cutover off `IProjectRegistry`; one-time
`ProjectsJsonImport`; **delete** `IProjectRegistry`, `ProjectRegistry`, `ProjectEntry`,
`ProjectsJsonContext` (folded into the importer), `ProjectRegistryTests`, `FakeProjects`;
rewire `WireApp` + Start wire tests onto a stored `Project`.

**Out (deliberately):** re-keying `?project=` from name to id (06); `PUT`/`DELETE`
endpoints (`MoveTo`/`Rename` on the model, no endpoint yet); `ProjectMapping` (still 1:1,
inlined); Steps-based field validation (ADR 0009, later); paging/`ListProjectsRequest`.

## Blast radius (edits to existing code)

1. `src/Domain/Projects/Project.cs` — add `Path` + `RequirePath` + `MoveTo`.
2. **New:** `IProjectRepository`, `JsonProjectRepository`, `ProjectDirectory`,
   `ProjectsJsonImport`.
3. `src/Features/Projects/Add/AddProjectEndpoint.cs` — inject `IProjectRepository`,
   uniqueness via `GetByName`, set `Path`, provisional `Path` guard.
4. `src/Features/Projects/{Get,List}/*Endpoint.cs` — `ProjectDto` gains `Path` in the
   inline projection.
5. `src/Features/Flows/Start/StartEndpoint.cs` — `IProjectRegistry` → `ProjectDirectory`.
6. `src/Host/Composition.cs` — drop `IProjectRegistry`; add `IProjectRepository`,
   `ProjectDirectory`, the importer hosted service.
7. **Delete:** `IProjectRegistry.cs`, `ProjectRegistry.cs` (and `ProjectEntry`),
   `ProjectRegistryTests.cs`, `Wire/Support/FakeProjects.cs`; `WireApp` registers a
   `Project` row instead of faking the registry.
8. **Docs:** flip 06's "Out" entry for `Path`/consolidation to *done here*; update
   03's current-state + the L1 "ProjectRegistry" infra note.

## Phased implementation plan

Each phase: warning-free build + green tests + behavior verified + one coherent commit
(the per-layer gate). Phases land in dependency order so the build stays green throughout.

**Phase 1 — model + contracts.** `Path` on `Project` (`Create`/`MoveTo`, `RequirePath`);
`Path` on `ProjectDto`/`CreateProjectRequest`; `AddProjectEndpoint`/`Get`/`List` set and
project `Path`. Unit Rules: `Create` requires + absolutizes `Path`; `MoveTo` re-guards.
Wire tests updated for the new field. Uniqueness still inline this phase.

**Phase 2 — seam.** `IProjectRepository` + `JsonProjectRepository` (composition) + DI;
`AddProjectEndpoint` uniqueness → `GetByName`. Behavior identical; existing Add wire
tests are the regression guard. Unit Rule: `GetByName` is case-insensitive, null-missing.

**Phase 3 — resolve + cutover.** `ProjectDirectory` over `IProjectRepository`;
`StartEndpoint` swaps `IProjectRegistry` → `ProjectDirectory`. Start wire tests arrange a
stored `Project` (drop the registry fake). Unit Rules: passthrough, unknown-name throw,
missing-dir throw — ported 1:1 from `ProjectRegistryTests`.

**Phase 4 — import + delete.** `ProjectsJsonImport` hosted service (empty-store guard);
delete `IProjectRegistry`/`ProjectRegistry`/`ProjectEntry`/`ProjectsJsonContext`/
`ProjectRegistryTests`/`FakeProjects`; `WireApp` rewired. Full suite green; boot once
against a real `projects.json` and confirm entries appear via `GET /projects` and a flow
launches by name (behavior-verify gate).

## Decisions captured

- **One canonical `Project`** (Id, Name, Path) in the repo store; the file-backed
  registry retires. Only `Resolve` survives the registry — `List`/`ProjectsFilePath`
  had no production consumer.
- **`Path` is required**, absolutized at construction; **existence is checked at
  resolve-time**, not construction (rows outlive a missing dir; deserialization is
  disk-free) — matching the registry's split.
- **`IProjectRepository.GetByName` is the seam 06 deferred**, introduced now by
  composition over `IRepository<Project>` (never subclassing `sealed JsonRepository<T>`),
  earned by two consumers (resolution + uniqueness).
- **Resolution keeps the absolute-path passthrough** (behavior lock); `?project=`
  stays name-keyed (id rekey deferred). *(Followed up immediately after 07: `StartRunRequest`
  re-keyed to `ProjectId` (Guid), resolution moved to `GetById`, and the passthrough dropped —
  flow-launch now resolves strictly by id. `ProjectDirectory` → `ProjectResolver`.)*
- **Existing `projects.json` is imported once** via a provisional, empty-store-guarded
  hosted service, removed at L12.
- **`ProjectDto` exposes `Path`** (the UI creates and shows it); the map stays inlined
  (still 1:1).

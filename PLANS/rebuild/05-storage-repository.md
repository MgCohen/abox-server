# Storage — generic repository + JSON store

Status: **built — Phases 1–2 (2026-06-13).** Self-contained — readable without prior context.
Defines the persistence pattern for the rebuild: an open-generic
`IRepository<T>` in Infrastructure backed by a JSON file store, replacing the
Projects slice's stubbed storage and serving as the default home for every
future entity collection. Storage is an **infrastructure** concern, not a domain
one.

## Why this, why now

The [Projects slice](04-projects-slice.md) shipped with a deliberate stub
(`StubProjects` returns a fixed in-memory list). The next step is real, durable
storage so projects can be listed, added, and edited. We take that step by
settling the **persistence pattern for the whole rebuild**, not just for
Projects — because "where data lives and how use cases reach it" is
repo-defining infrastructure, and the [architecture
proposal](../architecture-proposal.md) already converged on the answer:

> Infrastructure is the floor — depends on nothing, anything depends on it;
> holds the generic `IRepository<T>`, `SubprocessSession`, `Result<T>`.

So this is **executing a converged decision**, not inventing one. The Projects
stub graduates onto it; nothing else has to move.

### Why a generic repository is the right call *here*

A generic `IRepository<T>` is contested on the web — but every critique targets
**generic-repo-wrapped-around-EF-Core**, for two reasons that do not apply to a
JSON store:

1. **Redundant wrapping.** EF's `DbSet<T>` already is a repository and
   `DbContext` already is a Unit-of-Work, so a second generic layer is pure
   indirection. We have **no ORM underneath** to redundantly wrap.
2. **`IQueryable` leaking.** A repo that returns `IQueryable` cannot be swapped,
   because no non-EF backend can honor an arbitrary expression tree. Our repo
   loads entities into memory and returns **materialized domain types** — never
   `IQueryable`.

Over an in-memory/document store the pattern is unusually *clean*: everything is
already in memory, so the contract is honest and the swap seam (file → embedded
DB later) is real.

## The shape

```csharp
// Infrastructure — the floor. The key constraint + the generic contract.
namespace ABox.Infrastructure.Storage;

public interface IEntity { Guid Id { get; } }

public interface IRepository<T> where T : IEntity
{
    Task<IReadOnlyList<T>> GetAll(CancellationToken ct = default);
    Task<T?>               GetById(Guid id, CancellationToken ct = default);
    Task                   Add(T entity, CancellationToken ct = default);
    Task                   Update(T entity, CancellationToken ct = default);
    Task                   Remove(Guid id, CancellationToken ct = default);
}
```

```csharp
// DI — ONE open-generic line covers every current and future entity type.
services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
```

```csharp
// A use case resolves the closed type directly.
projects.MapGet("/", async (IRepository<Project> repo, CancellationToken ct) =>
    Results.Ok((await repo.GetAll(ct)).Select(p => new ProjectDto(p.Id, p.Name))));
```

The open-generic registration is the **"won't hurt us" guarantee**: adding a new
persisted entity costs **zero wiring** — make it `: IEntity`, and
`IRepository<NewThing>` resolves. The domain keeps the model and its business
rules; the *storage interface* leaves the domain entirely.

### Storage holds the full server-side entity

`JsonRepository<Project>` persists the domain `Project` — including the
server-only `Path` it gains with storage (where the orchestrator finds the
project on disk). The endpoint maps `Project → ProjectDto`, dropping `Path`; the
wire shape stays clean. Same split the rest of the repo uses
(`FlowSnapshot → FlowView`).

## The JSON store

`JsonRepository<T>` is the one implementation behind the interface.

- **One file per collection**, named by entity type, under the orchestrator data
  root (`IOrchestratorPaths` → `~/.abox/rebuild/`): e.g. `projects.json` holding
  a JSON array of `Project`. (One-file-per-collection, not per-entity — the
  collections are tiny and a single array is simplest to reason about.)
- **In-memory dictionary, loaded once, write-through.** `Dictionary<Guid, T>`
  guarded by a `SemaphoreSlim(1,1)` (async-friendly). Reads serve from memory;
  every mutation persists the whole array.
- **Atomic, durable writes** — the one thing `FileFlowHistory.Persist`
  (`File.WriteAllText`) gets wrong (torn write on crash). The correct pattern,
  atomic on both NTFS and POSIX:

  ```csharp
  var tmp = path + ".tmp";
  await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
  {
      await JsonSerializer.SerializeAsync(fs, items, WireJson.Options, ct);
      fs.Flush(flushToDisk: true);            // fsync before the swap — crash durability
  }
  if (File.Exists(path)) File.Replace(tmp, path, null);  // atomic rename
  else                   File.Move(tmp, path);           // first write: destination must exist for Replace
  ```

  Write the temp file in the **same directory** (same volume) so the rename
  stays on the atomic path.
- **Corrupt/unreadable file is non-fatal** — start empty, same posture as
  `FileFlowHistory.Load`.
- Marked **provisional**: it is the deliberate "simple now" backend, swappable
  for LiteDB / EF Core + SQLite behind the unchanged `IRepository<T>` when a real
  trigger arrives (§Upgrade path).

## The three decisions that keep it clean

1. **Identity — `IEntity { Guid Id }` marker in Infrastructure.** The repo must
   know each entity's key; a one-property marker is the standard answer.
   `Project : IEntity`. Domain referencing Infra for the marker is allowed —
   Infra is the floor everything sits on.

2. **Query surface — CRUD only; no `Find` on the generic base.** This is the one
   place the pattern has a real ceiling: a `Find(Func<T,bool>)` predicate cannot
   translate to SQL if we ever move to SQLite — it would silently degrade to
   load-all-and-filter. So when a real query arrives ("flows for project X",
   "projects by name"), **don't** widen the generic base; introduce a named
   sub-interface on that *second* use:

   ```csharp
   public interface IProjectRepository : IRepository<Project>
   {
       Task<Project?> GetByName(string name, CancellationToken ct = default);
   }
   ```

   Day one needs none of it.

3. **Async + `CancellationToken`, even though the file store is sync
   underneath.** The signatures cost nothing now and make the LiteDB / SQLite
   swap a drop-in with no caller changes (those APIs are async-native). This is
   the deliberate divergence from `FileFlowHistory`'s sync `List()`.

## What does NOT move

`FileFlowHistory` stays exactly as-is — and not merely to avoid churn. It is a
**capped log** (LRU, max 50 snapshots), not an entity collection: it has no
`GetById`/`Update`/`Remove` semantics, and forcing it onto `IRepository<T>`
would be the wrong abstraction. The rule:

> **Entity collections → `IRepository<T>`. Bespoke stores (capped logs, etc.)
> keep their own port.**

No existing flow/git/tasks code changes. The open-generic registration means new
entities cost nothing; old patterns are left untouched.

## Phased implementation plan

Each phase is a warning-free build + green tests + one coherent commit, per the
repo's per-layer gate.

**Phase 1 — the mechanism (Infrastructure). ✅ done.**
- Added `ABox.Infrastructure.Storage`: `IEntity`, `IRepository<T>`,
  `JsonRepository<T>` (atomic temp→fsync→`File.Replace`, `SemaphoreSlim`,
  load-once cache), and a `StorageRoot` value (`~/.abox/rebuild/`, overridable)
  rather than threading `IOrchestratorPaths` (whose `Root` is the *repo* root) —
  keeps the storage layer self-contained and trivially faked.
- Unit tests (`JsonRepositoryTests`) against a temp dir: CRUD round-trip;
  reload-from-disk; corrupt-file-starts-empty; **concurrent writers don't tear**.

**Phase 2 — Projects onto the repository. ✅ done.**
- `Project : IEntity`. **`Path` deferred** — adding a server-only `Path` now
  would mean committing consumer-less, machine-specific seed paths; it lands with
  the flow-launch consolidation that gives it a real consumer (as 04 originally
  framed). The Domain↔DTO split already stands on its own.
- Deleted `StubProjects` and the `IProjects` port (storage is no longer a domain
  concern); `ListProjectsEndpoint` resolves `IRepository<Project>` directly.
- Registered the open generic + root in `Composition`:
  `AddSingleton(StorageRoot.Default)` and
  `AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>))`.
- Seeding is a provisional `ProjectSeeder : IHostedService` (in the Projects
  Module): on first run (empty store) it writes the two entries, then the file is
  the source of truth. A hosted service (not post-build code) so it runs under
  `WebApplicationFactory` too.
- Wire test: `WireApp` registers a temp `StorageRoot`; `GET /projects` now reads
  the seeded store end-to-end through the real Host (renamed rule: "lists the
  seeded projects").
- 04's "port in Domain" decision is superseded — see the note added to
  [04-projects-slice.md](04-projects-slice.md).

**Phase 3 — Add / edit (when the UI needs it).**
- `POST /projects` (create: server mints `Guid.NewGuid()`, persists) and
  `PUT /projects/{id}` (rename / set path) — each a one-call use case over
  `IRepository<Project>.Add` / `.Update`.
- Request DTOs join `Contracts`.
- Gated on the UI actually growing create/edit — not built speculatively.

**Phase 4 (opportunistic, not scheduled) — retrofit `FileFlowHistory`'s write.**
- Replace its `File.WriteAllText` with the shared atomic-write helper to fix the
  torn-write bug. Only when we next touch flow history; it stays a capped log,
  *not* an `IRepository<T>`.

## Upgrade path (behind the unchanged `IRepository<T>`)

- **Need indexed/ad-hoc queries or the file creaks** → write
  `LiteDbProjectRepository` (single-file embedded, document model — same mental
  model), swap the DI line.
- **Need relational joins / multi-entity transactions / a cloud relational
  target** → EF Core 10 + SQLite, accept migrations; `DbContext` becomes the
  Unit-of-Work for free (we do not hand-roll one).

In every case the domain, use cases, and wire contracts are untouched — only one
Infrastructure class and one DI line change. That is the payoff for the single
abstraction the swap requirement justifies. **Unit-of-Work is YAGNI** until a
second store commits in one transaction.

## Decisions captured

- **Storage is infrastructure, not domain.** The generic `IRepository<T>` +
  `IEntity` live in `ABox.Infrastructure.Storage`; use cases resolve
  `IRepository<T>` from DI. Supersedes 04's "`IProjects` port in Domain."
- **Open-generic registration** — `AddSingleton(typeof(IRepository<>),
  typeof(JsonRepository<>))`; new entity types cost zero wiring.
- **CRUD-only generic base; named sub-interface for real queries** (second-use
  rule). No `Find(Func<>)` on the base — it can't translate to SQL later.
- **JSON file store, atomic temp→fsync→`File.Replace`**, one file per
  collection, provisional and swappable.
- **`FileFlowHistory` is left untouched** — a capped log, not an entity
  collection; its atomic-write fix is opportunistic.
- **Async-first signatures** with `CancellationToken`, future-proofing the swap.

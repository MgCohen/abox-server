# Structure tests

Two structural test *types*, grouped in one project because they share a reference profile (load
assemblies from disk + scan the filesystem; no production project reference of their own). Both are
Rulebook-governed — see [`../Harness/README.md`](../Harness/README.md) for the convention.

- **`Arch/`** — the **reference graph** (who depends on whom), via ArchUnitNET over the *loaded*
  assemblies. `Support/ArchitectureModel` defines the layer bands + the allow-graph the down-only rule is
  derived from.
- **`Structure/`** — **placement** on disk, via a filesystem scan. `Support/SourceTree` sees every project
  folder under `src/`/`tests/` (compiled or not); `Support/HomeFolders` is the agreed-folder model.

A third surface — *namespace mirrors folder* — is not a test here: it's the SDK analyzer **IDE0130**,
enforced at compile time (`/.editorconfig`, scoped to `src/` and `tests/`).

## How to extend

| Want to… | Do this |
|----------|---------|
| Add an Arch rule | append a `###` block to `Arch/Rulebook/rules.md` + a `[Rule("<header>")]` test in `Arch/Tests/RuleTests.cs` |
| Add a Structure rule | append a `###` block to `Structure/Rulebook/rules.md` + a `[Rule("<header>")]` test in `Structure/Tests/StructureTests.cs` |
| Add a production assembly / feature / slice | **nothing** — the csproj globs `src\**\RemoteAgents.*.csproj`, so a new `RemoteAgents.*` project is discovered and governed automatically |
| Add a layer band | add one `IObjectProvider<IType>` band + a `Layer` entry (with its `MayDependOn`) in `Arch/Support/ArchitectureModel`; the down-only rule covers it automatically |
| Evict a pending folder | drop it from `HomeFolders.PendingEviction`; the staleness check fails once the folder is gone, as the reminder to do so |

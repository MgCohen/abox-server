# Tests — one assembly, six Rulebook types

The single test assembly for the repo. Every test *type* is a Rulebook with the same folder shape
(`<Type>/Rulebook/`, `<Type>/Tests/`, `<Type>/Support/`) — see [`../Harness/README.md`](../Harness/README.md)
for the convention and the parity discipline. They coexist here because `ParityGuard` scopes `[Rule]`
discovery by namespace, so each type's Rulebook is counted against its own tests only.

- **`Arch/`** — the **reference graph** (who depends on whom), via ArchUnitNET over the *loaded* assemblies.
  `Support/ArchitectureModel` defines the layer bands + the allow-graph the down-only rule is derived from.
  It discovers production assemblies from the output dir and excludes anything named `*.Tests.*` — so this
  merged assembly excludes itself.
- **`Structure/`** — **placement** on disk, via a filesystem scan. `Support/SourceTree` sees every project
  folder under `src/`/`tests/` (compiled or not); `Support/HomeFolders` is the agreed-folder model.
- **`Unit/`** — isolated behavior of one type or a small cluster.
- **`E2E/`** — a flow driven end to end through an injectable provider (`Support/FlowHarness`).
- **`Wire/`** — HTTP smoke over `WebApplicationFactory<Program>`.
- **`Live/`** — real-CLI smoke, gated behind `[LiveFact]` / `RUN_LIVE=1`.

A third structural surface — *namespace mirrors folder* — is not a test: it's the SDK analyzer **IDE0130**,
enforced at compile time (`/.editorconfig`, scoped to `src/` and `tests/`).

## How to extend

| Want to… | Do this |
|----------|---------|
| Add an Arch rule | append a `###` block to `Arch/Rulebook/rules.md` + a `[Rule("<header>")]` test in `Arch/Tests/RuleTests.cs` |
| Add a Structure rule | append a `###` block to `Structure/Rulebook/rules.md` + a `[Rule("<header>")]` test in `Structure/Tests/StructureTests.cs` |
| Add a behavioral rule (Unit/E2E/Wire/Live) | append a `###` block to that type's `Rulebook/rules.md` + a `[Rule("<header>")]` test under its `Tests/` (1:N — several cases per Rule allowed) |
| Add a production assembly / feature / slice | **nothing** — the csproj globs `src\**\RemoteAgents.*.csproj`, so a new `RemoteAgents.*` project is referenced and governed automatically |
| Add a layer band | add one `IObjectProvider<IType>` band + a `Layer` entry (with its `MayDependOn`) in `Arch/Support/ArchitectureModel`; the down-only rule covers it automatically |
| Evict a pending folder | drop it from `HomeFolders.PendingEviction`; the staleness check fails once the folder is gone, as the reminder to do so |

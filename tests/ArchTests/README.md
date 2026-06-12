# Architecture tests

Structure enforcement on three surfaces, so drift can't slip through any:

- **Reference graph** (who depends on whom) — ArchUnitNET over the *loaded* assemblies. Fails the build
  when the dependency DAG of the structure migration is violated.
- **Namespace matches folder** — the SDK analyzer **IDE0130**, at compile time, scoped to `src/`
  (`/.editorconfig`); `RootNamespace` is derived per slice in `src/Features/Directory.Build.props`.
  Keeps the namespace bands the reference-graph rules trust from drifting off their folder.
- **Project placement** — a filesystem scan of `src/` (`SourceTree`). Sees every project folder on disk,
  compiled or not, so excluded or uncompiled code can't hide a stray.

## Layout

Files are grouped by role, not by C# kind:

- **`Fixtures/`** (`rules.md`) — the spec: the architecture rules in natural language.
- **`Support/`** (`ArchitectureModel.cs`, `SourceTree.cs`, `RuleBook.cs`, `RuleAttribute.cs`) — the
  harness: the loaded architecture + the layer allow-graph, the on-disk project tree, the rule-book
  reflection, and the `[Rule]` attribute. No tests here.
- **`Tests/`** (`RuleTests.cs`, `StructureTests.cs`, `RuleBookTests.cs`) — the `[Fact]`s: the
  reference-graph assertions, the filesystem project-placement assertion, and the rule↔test drift guard.

## How it fits together

- **`Fixtures/rules.md`** is the **single source of truth** — each `###` header *is* a rule, stated as
  the constraint itself; the bullets under it carry the rationale. This is the rule book; read it first.
- **`Tests/RuleTests.cs`** + **`Tests/StructureTests.cs`** each carry one `[Rule("<header>")]` test per
  block — the executable assertion.
- **`Tests/RuleBookTests.cs`** fails if a block has no test, a test cites a missing/renamed block, or a
  rule is tested twice. The block and its test can't drift apart.
- **`Support/ArchitectureModel.cs`** loads the production assemblies once and defines the layer bands
  (Contracts, Infrastructure, Domain, Features, Host) by namespace convention. The **layer allow-graph**
  (`Layers`, each band's `MayDependOn`) is the source the blanket down-only rule is *derived* from — add
  a band and every prior edge updates for free, with no hand-listed denylist to leave stale.

## How to extend

| Want to… | Do this |
|----------|---------|
| Add a rule | append a `###` block to `Fixtures/rules.md`, add a `[Rule("<that name>")]` test in `RuleTests.cs` or `StructureTests.cs` |
| Add a production assembly / feature / slice | **nothing** — the csproj globs `src\**\RemoteAgents.*.csproj` and `ArchitectureModel` loads them from the output dir, so a new project named `RemoteAgents.*` is discovered and governed automatically (Web is the one deliberate exclude) |
| Add a layer band | add one `IObjectProvider<IType>` band + a `Layer` entry (with its `MayDependOn`) in `ArchitectureModel.cs`; the down-only rule covers it automatically |
| Evict a pending folder | drop it from `PendingEvictionFolders`; the staleness check fails once the folder is gone, as the reminder to do so |

## Structure guards (filesystem + analyzer)

`StructureTests` reads `src/` directly, so it governs project placement before code ever compiles:

- **Every project lives under an agreed home folder** — the top-level `src/` folder must be a home
  (`Infrastructure`, `Domain`, `Features`, `Host`) or an explicit `PendingEvictionFolders` entry
  (currently empty — Morph and Web both evicted to the web repo). Any *new* stray fails; a staleness
  check fails when a listed folder is gone, so the allow-list shrinks as they leave instead of rotting.

**Namespace matches folder** is *not* a test here — it's the SDK analyzer **IDE0130**, enforced at
compile time as a build error (`/.editorconfig`, scoped to `src/`; a pending-eviction folder would opt
out via its own `.editorconfig`). Because our namespace keeps `.Features.` while the assembly name drops it,
`RootNamespace` is derived per slice in `src/Features/Directory.Build.props` so the analyzer agrees with
the convention. This replaced both a custom filesystem rule and the former *namespace orphan guard*.

## Not yet enforced (deliberate)

- **Per-feature `Contracts/` nested in a feature** — a future graduation; the Contracts band already
  matches any `*.Contracts` leaf, so the down-only rule activates the moment one lands. At that point the
  cross-feature rule also graduates to exclude peer Contracts (the legal channel).

## A note for future work

The remaining gap is an **agent that audits and authors the actual test behaviour** from each block —
today the `[Rule]` body is hand-written to match its block's prose. Parity keeps them linked; it does not
prove the assertion faithfully encodes the sentence. That review is deferred.

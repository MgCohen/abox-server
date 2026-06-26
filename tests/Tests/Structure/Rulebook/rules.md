---
docType: rulebook
testType: structure
template: ./template.md
harness: ../../../Harness/README.md
---

## Rules

### Every project lives under an agreed home folder
- **Why:** The agreed home folders (Infrastructure, Domain, Features, Host) are the only legal places
  production code may live. A folder under none of them escaped the structure — caught on disk, before it
  compiles, so an uncompiled-code blind spot can't hide it.

`HomeFolders.PendingEviction` is an explicit allow-list for folders tolerated under `src/` until they relocate;
the guard still rejects any new stray, and a staleness check fails if a listed folder is gone, so the list
shrinks instead of rotting. It is now empty: Morph and Web both evicted to the web repo. *Namespace mirrors
folder* is the companion guarantee, enforced at compile time by IDE0130 (`/.editorconfig`), not a test here.

### Each feature is one implementation project plus its Api/Contract leaves
- **Why:** The canonical slice (ADR 0011 D2, amended by the contract-publishing split) is exactly one
  implementation project per feature (verbs as folders, `Module` folded in) plus its leaves: at most one external
  `Api` leaf (client-facing) and at most one internal `Contract` leaf (cross-feature), at least one of the two — no
  per-verb, per-`Module`, or `Shared` sub-assemblies. "Every feature looks like this" is the strongest agent-first
  guardrail, and it is undecidable while granularity is a 2-to-9 judgment call. Read on disk so a stray sub-project
  is caught the moment it lands, before it compiles.

Decided from the csproj layout under `src/Features/<F>` (`SourceTree.ProjectsOf`): one implementation project +
the role leaves under `Api/` and/or `Contract/`. Projects (one impl + Api) satisfies it positively, so the rule is
non-vacuous from day one. `FeatureShape.PendingConsolidation` is an explicit allow-list for the not-yet-migrated
features (per-use-case Flows, per-verb Git/Tasks awaiting Gate 5); the guard still rejects any new non-canonical
feature, and a staleness check fails once a listed feature consolidates, so the list shrinks instead of rotting.

### Each verb folder declares its endpoint
- **Why:** The canonical slice is one endpoint per verb folder (ADR 0011); the only legal non-verb folders are the
  published `Contracts/` leaf and the folded-in `Module/`. A verb folder with no `*Endpoint.cs` is either a stray
  helper bucket (the `Shared/` sub-assembly the shape forbids) or a verb whose endpoint was misnamed or misplaced —
  the exact "feature doesn't follow the pattern" drift. Read on disk so the empty/odd folder is caught before it compiles.

Checked over the canonical features (`SourceTree.VerbFoldersWithoutEndpoint`): every immediate folder except the
leaves (`Api`/`Contract`) and `Module` must hold a `*Endpoint.cs`. Projects and Inbox satisfy it positively, so it
is non-vacuous from day one. The not-yet-migrated features (Flows/Git/Tasks, with `Shared/` and non-endpoint helpers
awaiting Gate 5) share the same `FeatureShape.PendingConsolidation` allow-list as the one-impl-plus-leaves rule;
consolidating a feature to the canonical shape removes its exemption, and the guard then requires every one of its
verb folders to conform.

### Requests, responses, and DTOs live in an Api or Contract leaf
- **Why:** The client binds a feature's `Api/` leaf and a peer slice binds its `Contract/` leaf — those are the only
  places the outside may name. A `*Request`/`*Response`/`*Dto`/`*View` type stranded in a verb folder is unbindable
  from outside the slice, the "what goes in a leaf vs the feature" mix-up. Read on disk by the type-naming convention
  the repo already uses (`CreateProjectRequest`, `ProjectDto`, `StartRunResponse`, `FlowView`), so a misplaced wire
  type is caught before it compiles.

Decided from file names under `src/Features` (`SourceTree.ContractTypeFilesOutsideLeaves`): any type whose name ends
`Request`/`Response`/`Dto`/`View` must sit under an `Api/` or `Contract/` leaf. The companion `…InLeaves` query asserts
the convention is live (the leaves do hold such types), so the rule polices a real population rather than passing
vacuously. It holds for every feature today, laggards included — even mid-migration, wire types already live in a leaf.

### Only the Api rollup is packable
- **Why:** Exactly one package ships off-box — `ABox.Api`, the rollup at `src/Api` that bundles every feature's `Api`
  leaf DLL into one `.nupkg`. If a feature project (an impl, a `Contract` leaf, or an `Api` leaf individually) also
  declared `IsPackable=true`, a stray solution-wide `dotnet pack` would publish a second, unmanaged package to the
  feed — exactly the "N packages" sprawl the single rollup exists to prevent.

`src/Features/Directory.Build.props` holds every feature project `IsPackable=false`; the rollup opts back in alone.
Checked on disk (`SourceTree.ApiRollupIsPackable` + `FeatureProjectsDeclaringPackable`): the rollup must declare
`<IsPackable>true</IsPackable>` + `<PackageId>ABox.Api</PackageId>`, and no feature csproj may re-declare it true.

### Every Api leaf is a self-contained bundle input
- **Why:** The rollup discovers Api leaves by a path+name wildcard (`Features/*/Api/*.Api.csproj`) and ships each
  one's DLL with no `<dependency>` entries. So a leaf placed off that path silently drops out of the package, and a
  leaf that declares a Project/PackageReference would ship a DLL whose dependency never travels — a runtime break on
  the client. Both failures are invisible at build time and only surface when the client restores; the disk check
  catches them in CI instead.

Checked on disk (`SourceTree.MisplacedApiLeaves` + `ApiLeavesWithDependencies`): every `*.Api.csproj` must sit at
`Features/<F>/Api/ABox.<F>.Api.csproj` (so the wildcard catches it) and declare no `<ProjectReference>`/
`<PackageReference>`. Projects' `Api` leaf satisfies it positively, so the rule is non-vacuous from day one.

### No build output lives under src or tests
- **Why:** `UseArtifactsOutput` + a pinned `ArtifactsPath` centralize every project's bin/obj into the repo-root
  `/artifacts`. A `bin`, `obj`, or `artifacts` folder under `src/` or `tests/` means a project escaped the root
  `Directory.Build.props` — the bug that scattered Features output into `src/Features/artifacts/`.

A filesystem scan (`SourceTree.StrayBuildOutput`) reports the top-most offending folder. The output is
gitignored and invisible to the reference graph, so only a disk scan can catch it.

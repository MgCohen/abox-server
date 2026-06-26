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

### Each feature is one implementation project plus one Contracts leaf
- **Why:** The canonical slice (ADR 0011 D2) is exactly one implementation project per feature (verbs as folders,
  `Module` folded in) + one Contracts leaf — no per-verb, per-`Module`, or `Shared` sub-assemblies. "Every feature
  looks like this" is the strongest agent-first guardrail, and it is undecidable while granularity is a 2-to-9
  judgment call. Read on disk so a stray sub-project is caught the moment it lands, before it compiles.

Decided from the csproj layout under `src/Features/<F>` (`SourceTree.ProjectsOf`): one implementation project +
one project under a `Contracts/` folder. Projects satisfies it positively, so the rule is non-vacuous from day one.
`FeatureShape.PendingConsolidation` is an explicit allow-list for the not-yet-migrated features (per-use-case
Flows, per-verb Git/Tasks awaiting Gate 5); the guard still rejects any new non-canonical feature, and a staleness
check fails once a listed feature consolidates to the canonical two, so the list shrinks instead of rotting.

### Each verb folder declares its endpoint
- **Why:** The canonical slice is one endpoint per verb folder (ADR 0011); the only legal non-verb folders are the
  published `Contracts/` leaf and the folded-in `Module/`. A verb folder with no `*Endpoint.cs` is either a stray
  helper bucket (the `Shared/` sub-assembly the shape forbids) or a verb whose endpoint was misnamed or misplaced —
  the exact "feature doesn't follow the pattern" drift. Read on disk so the empty/odd folder is caught before it compiles.

Checked over the canonical features (`SourceTree.VerbFoldersWithoutEndpoint`): every immediate folder except
`Contracts`/`Module` must hold a `*Endpoint.cs`. Projects and Inbox satisfy it positively, so it is non-vacuous from
day one. The not-yet-migrated features (Flows/Git/Tasks, with `Shared/` and non-endpoint helpers awaiting Gate 5) share
the same `FeatureShape.PendingConsolidation` allow-list as the one-impl-plus-Contracts rule; consolidating a feature
to the canonical shape removes its exemption, and the guard then requires every one of its verb folders to conform.

### Requests, responses, and DTOs live in the Contracts leaf
- **Why:** The client and peer slices bind a feature's `Contracts/` leaf — that is the only place the outside may
  name. A `*Request`/`*Response`/`*Dto`/`*View` type stranded in a verb folder is unbindable from outside the slice,
  the "what goes in Contracts vs the feature" mix-up. Read on disk by the type-naming convention the repo already uses
  (`CreateProjectRequest`, `ProjectDto`, `StartRunResponse`, `FlowView`), so a misplaced wire type is caught before it compiles.

Decided from file names under `src/Features` (`SourceTree.ContractTypeFilesOutsideContracts`): any type whose name ends
`Request`/`Response`/`Dto`/`View` must sit under a `Contracts/` folder. The companion `…InContracts` query asserts the
convention is live (the leaves do hold such types), so the rule polices a real population rather than passing
vacuously. It holds for every feature today, laggards included — even mid-migration, wire types already live in Contracts.

### No build output lives under src or tests
- **Why:** `UseArtifactsOutput` + a pinned `ArtifactsPath` centralize every project's bin/obj into the repo-root
  `/artifacts`. A `bin`, `obj`, or `artifacts` folder under `src/` or `tests/` means a project escaped the root
  `Directory.Build.props` — the bug that scattered Features output into `src/Features/artifacts/`.

A filesystem scan (`SourceTree.StrayBuildOutput`) reports the top-most offending folder. The output is
gitignored and invisible to the reference graph, so only a disk scan can catch it.

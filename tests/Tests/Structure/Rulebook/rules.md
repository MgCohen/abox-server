Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

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

### No build output lives under src or tests
- **Why:** `UseArtifactsOutput` + a pinned `ArtifactsPath` centralize every project's bin/obj into the repo-root
  `/artifacts`. A `bin`, `obj`, or `artifacts` folder under `src/` or `tests/` means a project escaped the root
  `Directory.Build.props` — the bug that scattered Features output into `src/Features/artifacts/`.

A filesystem scan (`SourceTree.StrayBuildOutput`) reports the top-most offending folder. The output is
gitignored and invisible to the reference graph, so only a disk scan can catch it.

# Structure Rulebook

Each Rule is one source-placement invariant over `src/` and `tests/`, read straight from disk so it governs
where production code lives before code compiles, and is the single source of truth for it. The test system's
own layout — the taxonomy and Rulebook format — is the Meta type's job, not this one. Convention, parity
discipline, and the Rule shape live in [`../../../Harness/README.md`](../../../Harness/README.md) and `template.md`.

---

### Every project lives under an agreed home folder
- **Why:** The agreed home folders (Infrastructure, Domain, Features, Host) are the only legal places
  production code may live. A folder under none of them escaped the structure — caught on disk, before it
  compiles, so an uncompiled-code blind spot can't hide it.

`HomeFolders.PendingEviction` is an explicit allow-list for folders tolerated under `src/` until they relocate;
the guard still rejects any new stray, and a staleness check fails if a listed folder is gone, so the list
shrinks instead of rotting. It is now empty: Morph and Web both evicted to the web repo. *Namespace mirrors
folder* is the companion guarantee, enforced at compile time by IDE0130 (`/.editorconfig`), not a test here.

### No build output lives under src or tests
- **Why:** `UseArtifactsOutput` + a pinned `ArtifactsPath` centralize every project's bin/obj into the repo-root
  `/artifacts`. A `bin`, `obj`, or `artifacts` folder under `src/` or `tests/` means a project escaped the root
  `Directory.Build.props` — the bug that scattered Features output into `src/Features/artifacts/`.

A filesystem scan (`SourceTree.StrayBuildOutput`) reports the top-most offending folder. The output is
gitignored and invisible to the reference graph, so only a disk scan can catch it.

# Structure Rulebook

The placement rulebook: filesystem invariants over `src/` and `tests/`, read directly from disk
(`Structure/Support/SourceTree`), so they govern project placement before code ever compiles. Each `###`
header **is** a Rule and the **single source of truth** for it; a `[Rule("<header>")]` test in
`Structure/Tests/` enforces it, and `ParityTests` (strict 1:1) fails the build on any header/test mismatch.
The home-folder model lives in `Structure/Support/HomeFolders`.

Template:
```markdown
### <subject> <must / must not> <placement constraint>
- **Why:** <the blind spot this closes on disk>
- **How / Note:** <the scan + any allow-list>
```

---

### Every project lives under an agreed home folder
- **Why:** The agreed home folders (Infrastructure, Domain, Features, Host) are the only legal places
  production code may live. A folder under none of them escaped the structure — caught on disk, before
  it ever compiles, so an uncompiled-code blind spot can't hide it.
- **Note:** `HomeFolders.PendingEviction` is an explicit, documented allow-list for folders tolerated under
  `src/` until they relocate. The guard still rejects any *new* stray, and a staleness check fails if a
  listed folder is gone — so the list shrinks as they leave instead of rotting into a silent hole. It is
  now empty: Morph and Web both evicted to the web repo.
- **Companion (not a test here):** *namespace mirrors folder* is enforced at **compile time** by the
  SDK analyzer **IDE0130** (`/.editorconfig`, `dotnet_diagnostic.IDE0130.severity = error`), scoped to
  both `src/` and `tests/`. Under `src/`, `RootNamespace` is derived per slice in
  `src/Features/Directory.Build.props`; under `tests/`, the type-folder taxonomy (`Arch/`, `Unit/`, …) is
  what it keeps honest. That replaced the former custom filesystem rule + the namespace orphan guard.

### No build output lives under src or tests
- **Why:** `UseArtifactsOutput` + a **pinned** `ArtifactsPath` centralize every project's bin/obj into the
  repo-root `/artifacts`. A `bin`, `obj`, or `artifacts` folder under `src/` or `tests/` means a project
  escaped the root `Directory.Build.props` — the exact bug that scattered Features output into
  `src/Features/artifacts/` when the slice's nested props shadowed the root's artifacts anchor.
- **How:** A filesystem scan (`SourceTree.StrayBuildOutput`) over `src/` and `tests/`, reporting the
  top-most offending folder. The output is gitignored and so invisible to the reference graph — only a
  disk scan can catch it, the same blind-spot-closing surface as the project-placement guard above.

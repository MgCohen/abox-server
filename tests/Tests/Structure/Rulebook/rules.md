# Structure Rulebook

The placement rulebook: filesystem invariants over `src/` and `tests/`, read directly from disk
(`Structure/Support/SourceTree`), so they govern project placement before code ever compiles. It also guards
the **test taxonomy's integrity** — that every test folder is a registered type, every test lives inside one,
and every run attribute is a registered marker — by reflecting over the test assembly, closing the escape
where a test sits outside the structure the per-type `ParityGuard` scopes to. Each `###` header **is** a Rule
and the **single source of truth** for it; a `[Rule("<header>")]` test in `Structure/Tests/` enforces it, and
`ParityTests` (strict 1:1) fails the build on any header/test mismatch. The home-folder model lives in
`Structure/Support/HomeFolders`; the test-type and marker registries in `Structure/Support/TestTypes` and
`Harness/TestMarkers`.

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

### Every folder under tests holds a registered test type
- **Why:** Each folder under `tests/Tests/` is a kind of guarantee — a Rulebook with its own `ParityGuard`.
  A folder that is none of the six registered types (and not shared `Support`) is a test kind that no parity
  fact scopes to, so its tests would run with their `[Rule]` citation unchecked. Caught on disk the moment the
  folder lands, before any test in it compiles.
- **How / Note:** `SourceTree.TestTypeFolders()` lists the immediate children of `tests/Tests/`; each must be
  in `TestTypes.Registered` or the `Support` allow-list. Standing up a new type means registering it here — the
  same deliberate gate as adding a home folder.

### Every test lives inside a registered test type
- **Why:** The per-type `ParityGuard` scopes `[Rule]` discovery to one `RemoteAgents.Tests.<Type>.Tests`
  namespace, so a test placed anywhere else — shared `Support`, a type's own `Support`, the root — runs but is
  never required to cite a Rule. This is the assembly-wide backstop that closes that escape: every method an
  attribute marks as a test must sit inside a registered type's `Tests` namespace.
- **How / Note:** Reflection over the test assembly, selecting `TestMarkers.Marks` methods whose namespace
  fails `TestTypes.ContainsTest`. Complements IDE0130 (namespace mirrors folder) and the disk folder guard:
  together they pin a test to a registered type's `Tests/` folder, where its citation is enforced.

### Every run attribute is a registered test marker
- **Why:** Test detection matches attributes by name (`TestMarkers.Names`) rather than by deriving from
  `FactAttribute`, which keeps the registry open to a foreign framework's marker — but at the cost that a new
  xUnit run attribute added without registering it would mark tests xUnit runs yet parity never sees. This
  guard is that cost's closing net: every `FactAttribute` subtype in the suite must be a registered marker.
- **How / Note:** Reflection over the test assembly for `Xunit.FactAttribute` subtypes; each name must be in
  `TestMarkers.Names`. Using the base type *here*, in the guard, is exactly the sanctioned place — it backstops
  the name list without coupling detection itself to xUnit's hierarchy. Infrastructure facts (`[ParityFact]`)
  live in the `Harness` assembly, outside this scan, so they stay exempt.

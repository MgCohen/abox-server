# Structure Rulebook

Each Rule is one filesystem/taxonomy invariant over `src/` and `tests/`, read straight from disk so it governs
placement before code compiles, and is the single source of truth for it. Convention, parity discipline, and
the Rule shape live in [`../../../Harness/README.md`](../../../Harness/README.md) and `template.md`.

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

### Every folder under tests holds a registered test type
- **Why:** Each folder under `tests/Tests/` is a kind of guarantee — a Rulebook with its own `ParityGuard`. A
  folder that is none of the six registered types (and not shared `Support`) is a test kind no parity fact
  scopes to, so its tests would run with their `[Rule]` citation unchecked.

`SourceTree.TestTypeFolders()` lists the immediate children of `tests/Tests/`; each must be in
`TestTypes.Registered` or the `Support` allow-list. Standing up a new type means registering it there — the
same deliberate gate as adding a home folder.

### Every test lives inside a registered test type
- **Why:** The per-type `ParityGuard` scopes `[Rule]` discovery to one `ABox.Tests.<Type>.Tests` namespace, so
  a test placed anywhere else — shared `Support`, a type's own `Support`, the root — runs but is never required
  to cite a Rule. This is the assembly-wide backstop that closes that escape.

Reflection over the test assembly selects `TestMarkers.Marks` methods whose namespace fails
`TestTypes.ContainsTest`. An attribute the name list does not yet know marks no test here, so an unregistered
marker is a patch-when-seen event: add the name.

### Every Rule matches its type's template
- **Why:** A type's `template.md` is the schema; without a check it is only aspirational. A rule missing its
  `**Why:**`, carrying a stray bold-label bullet not in the schema, or dropping the result arrow its template
  mandates is organizational drift that passes silently.

`RulebookFormat` reads each type's `template.md` for the field set and validates every `### ` rule in `rules.md`
against it — bullet-label set equal to the template's, header arrow iff the template header has one. Format
only, never placeholder content; a new test type is covered the moment its folder lands.

### Every Rulebook holds only rules
- **Why:** Format-checking the `### ` blocks it finds leaves the gaps unguarded — a `## Scratch` section or a
  loose section heading between rules would slip in and let `rules.md` rot into a dumping ground.

`RulebookFormat.Headings` over each `rules.md` allows only the single `# ` title and the `### ` rules; any other
heading level is rejected. Plain prose under a rule stays allowed (the one-line-comment allowance).

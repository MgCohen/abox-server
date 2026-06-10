# Structure guards — plan (folder + namespace enforcement)

Forward plan for the arch-test hardening that came out of testing the namespace
structure guard (`c4db847`). Companion to [`structure.md`](structure.md) (the
target layout) and [`structure-migration.md`](structure-migration.md) (the
migration). **Plan only — not built.**

## Why this exists

Testing the namespace structure guard surfaced two real gaps:

1. **Load blind spot.** ArchUnitNET only sees *compiled + loaded* assemblies.
   Three ways to be invisible to it: on the csproj `Exclude` list (`Web`),
   named something other than `RemoteAgents.*` (`Morph`), or outside `src/`.
   So "every type is in a home" really means "every *loaded* type is in a home."
2. **Namespace is a proxy for placement.** We check `t.Namespace.FullName`, but
   a file can sit in the wrong folder with a hand-edited namespace and the guard
   won't notice. We want to check the **real folder**, and enforce
   *namespace-follows-folder* so the namespace-based dependency rules stay honest.

## Principle: two kinds of guard, two mechanisms

| Concern | Mechanism | Sees |
|---------|-----------|------|
| **Reference graph** (who depends on whom) | ArchUnitNET, loaded assemblies | compiled type refs — inherently needs the build |
| **Physical structure** (placement, naming) | **filesystem scan of `src/`** | every folder/file on disk, instantly, loaded or not |

The filesystem scan is what closes gap 1 (sees `Web`/`Morph`/new folders the
moment they exist) and answers gap 2 (checks folders, not namespaces).

## Test inventory — keep every guard alive

| # | Rule | Mechanism | Status |
|---|------|-----------|--------|
| 1 | **Every project lives under an agreed home folder** | filesystem | **NEW** |
| 2 | **A type's namespace mirrors its folder** | filesystem + parse | **NEW** |
| 3 | Contracts must not depend on internal assemblies | ArchUnitNET | keep |
| 4 | Infrastructure must not depend on other internal assemblies | ArchUnitNET | keep |
| 5 | Nothing may depend on Host | ArchUnitNET | keep |
| 6 | Domain must not depend on Features | ArchUnitNET | keep |
| 7 | Features must not depend on each other | ArchUnitNET | keep |
| 8 | No code lives outside the agreed structure (namespace orphan guard) | ArchUnitNET | **RETIRE** once 1+2 green |
| 9 | Rule-book parity (block ↔ test) | reflection | keep (auto-covers 1, 2) |

**Why retire #8:** folder-home (1) + namespace-mirrors-folder (2) together imply
namespace-under-a-home-namespace. #8 becomes redundant. Keep it until 1+2 are
green so there's never a coverage gap, then delete its block + test (parity stays
green — it only checks the correspondence, not a count).

## The namespace ↔ folder rule (precise)

For every `.cs` file under `src/` that **declares** a namespace:

```
declaredNamespace  ==  "RemoteAgents." + folderPath(file, relative to src).replace('/', '.')
```

Examples (all current, all pass):
- `Features/Flows/Start/StartEndpoint.cs` → `RemoteAgents.Features.Flows.Start`
- `Domain/Flow/Operations/Operation.cs` → `RemoteAgents.Domain.Flow.Operations`
- `Infrastructure/Json/JsonLine.cs` → `RemoteAgents.Infrastructure.Json`

Notes:
- Reads the **declared** namespace from source (file-scoped — already a project
  standard), not csproj `RootNamespace`.
- **Skip files with no namespace** — `Program.cs` (top-level statements),
  `GlobalUsings.cs`. They declare nothing to check.
- **Assembly name is NOT folder-derived and is deliberately not checked.** The
  convention drops `.Features.`: folder `Features/Flows/Start` → assembly
  `RemoteAgents.Flows.Start`, namespace `RemoteAgents.Features.Flows.Start`. Only
  the namespace mirrors the folder; the assembly name is its own convention.

## Prerequisite decision — canonical home folder names

`structure.md` prescribes **bare** home folders. The tree has drifted:

| On disk | `structure.md` | Status |
|---------|----------------|--------|
| `src/Domain`, `src/Features`, `src/Infrastructure` | same | ✓ |
| `src/RemoteAgents.Host` | `src/Host` | ✗ prefixed |
| `src/RemoteAgents.Web` | `src/Web` | ✗ prefixed |
| `src/RemoteAgents.Contracts` | `Features/<F>/Contracts` | ✗ flat + wrong place |
| `src/Morph` | — | ✗ stray (out of scope) |

The namespace↔folder rule (2) **requires bare folders** — folder `RemoteAgents.Host`
would map to expected namespace `RemoteAgents.RemoteAgents.Host`. So the
recommendation is **bare** (matches `structure.md` and makes the mapping clean):

- `src/RemoteAgents.Host` → `src/Host`  (assembly + namespace stay `RemoteAgents.Host`)
- `src/RemoteAgents.Web` → `src/Web`  (then leaves for its own repo, per owner)
- `src/RemoteAgents.Contracts` → decomposed into `Features/Flows/Contracts` (gone)
- `src/Morph` → out of this repo

Folder renames are cosmetic to the build (assembly `AssemblyName` / namespace are
set independently of folder name) — they only realign disk with the agreed shape.

**Agreed home folders (the allow-list, folder form):** `Host`, `Web`, `Features`,
`Domain`, `Infrastructure`.

> **RESOLVED (owner):** **bare folders.** `src/RemoteAgents.Host` → `src/Host`,
> `src/RemoteAgents.Web` → `src/Web`. Rule 2's mapping has no exceptions; assembly
> and namespace are unchanged (folder rename only).

## Test project organization — no new project

Keep the single `tests/ArchTests` (the `structure.md` whole-solution singleton).
Filesystem tests are plain xUnit `[Fact]`s; they don't need ArchUnitNET, but
co-locating keeps **one rule book + one parity surface**. Add:

```
tests/ArchTests/
  Fixtures/rules.md            + 2 blocks (rules 1, 2); 1 block removed (rule 8) at the end
  Support/
    ArchitectureModel.cs       (unchanged — ArchUnitNET loaded model, namespace bands)
    SourceTree.cs              NEW — locate src root, enumerate projects + .cs, parse namespaces
    RuleBook.cs, RuleAttribute.cs
  Tests/
    RuleTests.cs               ArchUnitNET dependency rules (drop rule 8 at the end)
    StructureTests.cs          NEW — rules 1 + 2, [Rule]-tagged
    RuleBookTests.cs           parity — auto-covers the new [Rule] methods
```

`SourceTree` responsibilities:
- Locate the repo root by walking up from `AppContext.BaseDirectory` to a marker
  (`RemoteAgents.slnx`); resolve `src/`.
- Enumerate `src/**/*.csproj` (project dirs) and `src/**/*.cs` (skip
  `artifacts/`, `bin/`, `obj/`).
- Parse the file-scoped `namespace X;` declaration per file.
- **Throw if the root isn't found or zero projects are seen** — mirror the
  existing empty-assemblies guard so a broken locator can't go vacuously green.

## Rule-book blocks to add

```
### Every project lives under an agreed home folder
- **Why:** The agreed home folders (Host, Web, Features, Domain, Infrastructure)
  are the only legal places production code may live. A folder under none of them
  escaped the structure — caught on disk, before it ever compiles, so the Web/Morph
  load blind spot can't hide it.

### A type's namespace mirrors its folder
- **Why:** The dependency rules band types by namespace; if a namespace can drift
  from its folder, those bands lie. Pinning namespace = RemoteAgents + folder path
  keeps placement and namespace in sync, so folder enforcement and graph enforcement
  agree. (Assembly name is a separate convention and is not folder-derived.)
```

## Sequencing

0. **Contracts decomposition** (the in-flight task) — move the 8 Flow DTOs to
   `Features/Flows/Contracts/`, `ProjectInfo` → Host, `WireJson` →
   `Infrastructure/Json`; push domain→DTO mapping into handlers. Turns the
   committed namespace guard (#8) **green**, and pre-satisfies folder rule #1
   (flat `Contracts` folder is gone). Do this first.
1. **Decide** canonical home-folder names (open decision above).
2. **Rename** home folders to bare; move `Morph` (and later `Web`) out of repo.
3. **Add** `SourceTree` + `StructureTests` + the 2 rule blocks → go green.
4. **Retire** rule #8 (delete block + test) — now subsumed.
5. **Commit** as one coherent green change (plus the README "Not yet enforced"
   row for `Web → Contracts` can drop "Web isn't loaded" once folder rule #1
   governs Web's placement; the *dependency* edge stays deferred).

## What this does NOT cover (still deferred)

- **`Web → Contracts only`** dependency edge — still needs Web *loaded* into
  ArchUnitNET (separate from folder placement, which rule #1 now does cover).
- **`PtySession` internal to `Domain.Agents`** — the spawn wall.
- **Assembly-name convention** (`RemoteAgents.*`, `.Features.` dropped) — not
  enforced; would be a third rule if the Morph-style mis-naming recurs.

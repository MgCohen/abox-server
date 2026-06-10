# Structure guards — plan (folder + namespace enforcement)

Forward plan for the arch-test hardening that came out of testing the namespace
structure guard (`c4db847`). Companion to [`structure.md`](structure.md) (the
target layout) and [`structure-migration.md`](structure-migration.md) (the
migration). **Plan only — not built.**

## Why this exists

Testing the namespace structure guard surfaced two real gaps; a third is carried
over from the thermo-nuclear review of the suite (finding **F1**, deferred at the
time because it was small):

1. **Load blind spot.** ArchUnitNET only sees *compiled + loaded* assemblies.
   Three ways to be invisible to it: on the csproj `Exclude` list (`Web`),
   named something other than `RemoteAgents.*` (`Morph`), or outside `src/`.
   So "every type is in a home" really means "every *loaded* type is in a home."
2. **Namespace is a proxy for placement.** We check `t.Namespace.FullName`, but
   a file can sit in the wrong folder with a hand-edited namespace and the guard
   won't notice. We want to check the **real folder**, and enforce
   *namespace-follows-folder* so the namespace-based dependency rules stay honest.
3. **The layer rules are a hand-maintained denylist (F1).** Each dependency rule
   hand-lists every forbidden target (`.NotDependOnAny(InfrastructureBand)`
   `.AndShould().NotDependOnAny(DomainBand)…`). The categorical half landed (bands
   auto-join their member assemblies), but adding a 6th band means editing *every*
   prior rule to add `.NotDependOnAny(NewBand)` — and a missed edit is a silent
   hole that fails nothing. `Web` and a future `Domain.Kernel` are exactly the
   bands that would trip this. The fix is a single allow-graph the denylist is
   *derived* from — see "Collapse the layer denylists" below.

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
| 3 | Contracts must not depend on internal assemblies | ArchUnitNET | keep (**dormant** — empty since Step 0 dissolved flat Contracts; runs `WithoutRequiringPositiveResults`, band matches any `*.Contracts` leaf, auto-activates on the first per-feature leaf) |
| 4 | Infrastructure must not depend on other internal assemblies | ArchUnitNET | keep |
| 5 | Nothing may depend on Host | ArchUnitNET | keep |
| 6 | Domain must not depend on Features | ArchUnitNET | keep |
| 7 | Features must not depend on each other | ArchUnitNET | keep |
| 8 | No code lives outside the agreed structure (namespace orphan guard) | ArchUnitNET | **RETIRE** once 1+2 green |
| 9 | Rule-book parity (block ↔ test) | reflection | keep (auto-covers 1, 2) |

Rules 3–7 keep their headers and behavior, but their *implementation* gets
rewritten once — from five hand-listed denylists into one allow-graph (F1, below).
The rule book stays the same; only the C# behind it collapses.

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
    ArchitectureModel.cs       MODIFIED — add the Band allow-graph (F1); bands already exist
    SourceTree.cs              NEW — locate src root, enumerate projects + .cs, parse namespaces
    RuleBook.cs, RuleAttribute.cs
  Tests/
    RuleTests.cs               MODIFIED — collapse 5 denylists → 1 derived rule (F1); drop rule 8 at the end
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

## Collapse the layer denylists into one allow-graph (F1)

The dependency rules (3–7) today each hand-enumerate their forbidden
targets. That's readable at five bands but has the maintenance hole in gap 3:
add a band, edit every prior rule, miss one silently. Replace the denylists with
a single **allow-graph** that mirrors the README's "allowed edges" table — the
real source of truth — and *derive* "depend down only" from it:

```csharp
sealed record Band(string Name, string Ns, params Band[] MayDependOn);

static readonly Band Contracts      = new("Contracts",      ContractsNs);  // empty MayDependOn: depends on nothing internal
static readonly Band Infrastructure = new("Infrastructure", InfrastructureNs);
static readonly Band Domain         = new("Domain",         DomainNs,    Infrastructure, Contracts);
static readonly Band Features       = new("Features",       FeaturesNs,  Domain, Infrastructure, Contracts);
static readonly Band Host           = new("Host",           HostNs,      Features, Domain, Infrastructure, Contracts);

// one rule, derived: every band forbids every other band it does not list
[Theory, MemberData(nameof(AllBands))]
public void DependsDownOnly(Band band)
{
    foreach (var target in AllBands.Where(b => b != band && !band.MayDependOn.Contains(b)))
        Types().That().Are(band).Should().NotDependOnAny(target).Check(Architecture);
}
```

Now adding `Web` or `Domain.Kernel` is a one-line data change, the denylist can't
go stale, and `MayDependOn` reads as the allow-graph — which is what the
architecture *is*. Notes:

- **Keep the few genuinely directional rules explicit and named.** If a real
  intra-band invariant exists (e.g. a future `Domain.Flow ↛ Domain.Agents` once
  Domain splits into peer bands), that's an architecture *decision*, not
  mechanical floor/ceiling — it stays its own named `[Rule]`, not derived. The
  allow-graph covers the blanket down-only rules; named rules cover the decisions.
- **An empty band (Contracts today) needs the derived rule to run
  `WithoutRequiringPositiveResults`** — a dormant-but-valid band must not fail for
  matching zero types. The collapse must carry that tolerance per band.
- **Rule-book headers 3–7 are unchanged** — parity stays green. Only the C#
  collapses; the five constraint statements in `rules.md` still describe the same
  edges. (If the collapse merges them under one header, update `rules.md` to match
  and let parity confirm.)
- This is a `RuleTests.cs` + `ArchitectureModel.cs` rewrite — the same two files
  the filesystem guards touch — so it rides in Step 3, not a separate pass.

## Sequencing

0. **Contracts decomposition** — ✅ **DONE.** The 8 types did NOT all become a
   feature Contracts leaf (the plan's original assumption): 5 are the Flow
   domain's *working vocabulary* (`FlowSnapshot`, `OperationDto`, `DecisionDto`,
   `FlowPhase`, `OperationStatus`) and moved into `Domain/Flow` + `Domain/Flow/
   Operations` (Domain ↛ Features forbids a feature home). The genuinely-wire
   types went feature-local (`StartRunRequest`/`Response` → Start, `FlowInfo` →
   Catalog); `ProjectInfo` was **deleted** (it duplicated `Infrastructure.Projects.
   ProjectEntry` — Host now returns that directly); `WireJson` → `Infrastructure/
   Json`. Flat `RemoteAgents.Contracts` deleted; structure guard (#8) **green**.
   **Web (Blazor WASM) dropped from `RemoteAgents.slnx`** — it can't safely
   reference the server feature assemblies under warnings-as-errors; UI/Domain
   separation deferred (Web is leaving the repo). **Consequence not foreseen:**
   rule #3 (*Contracts must not depend on internal assemblies*) now governs an
   empty set, and ArchUnit rejects an empty-subject rule. It was **kept dormant**
   (not retired): `WithoutRequiringPositiveResults()` makes the empty period an
   honest pass, and its band was repointed to match any `*.Contracts` leaf so it
   auto-activates when the first per-feature leaf lands. Net −48 lines; build 0
   warnings; arch 7/7; FlowTests 8/8.
1. **Decide** canonical home-folder names (open decision above).
2. **Rename** home folders to bare; move `Morph` (and later `Web`) out of repo.
3. **Add** `SourceTree` + `StructureTests` + the 2 rule blocks → go green.
   **In the same pass, collapse the layer denylists into the allow-graph (F1)** —
   same two files, one coherent rule-model rewrite.
4. **Retire** rule #8 (delete block + test) — now subsumed.
5. **Commit** as one coherent green change (plus the README "Not yet enforced"
   row for `Web → Contracts` can drop "Web isn't loaded" once folder rule #1
   governs Web's placement; the *dependency* edge stays deferred).

## Thermo-nuclear review findings — disposition

The arch-test review (session `e184aca0`) raised four findings. For the record:

- **F1** (hand-maintained layer denylist) — **carried into Step 3** here, as the
  allow-graph collapse above.
- **F2** (orphan-type guard) — **done**, then evolved into the `AgreedHomes` /
  `IsOutsideStructure` structure guard (`c4db847`); subsumed by rules 1+2 at Step 4.
- **F3** (band regexes need a `(\.|$)` boundary anchor) — **done**; every band in
  `ArchitectureModel.cs` carries the anchor.
- **F4** (`Web` ungoverned) — **deferred by decision**; see below.

## What this does NOT cover (still deferred)

- **`Web → Contracts only`** dependency edge (F4) — still needs Web *loaded* into
  ArchUnitNET (separate from folder placement, which rule #1 now does cover).
- **`PtySession` internal to `Domain.Agents`** — the spawn wall.
- **Assembly-name convention** (`RemoteAgents.*`, `.Features.` dropped) — not
  enforced; would be a third rule if the Morph-style mis-naming recurs.

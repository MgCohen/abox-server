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
| 1 | **Every project lives under an agreed home folder** | filesystem | ✅ **DONE** |
| 2 | **A type's namespace mirrors its folder** | ~~filesystem~~ → **IDE0130** | ✅ **DONE then SUPERSEDED** — the custom filesystem rule caught 4 drifts, then was retired in favour of the SDK analyzer **IDE0130** (compile-time, see "Namespace rule → IDE0130" below) |
| 3–6 | Dependencies flow down the layer graph only (Contracts/Infra no-internal, Domain↛Features, nothing↛Host) | ArchUnitNET | ✅ **DONE** — the 4 blanket denylists collapsed into one derived allow-graph rule; empty Contracts band runs `WithoutRequiringPositiveResults` and auto-activates on the first `*.Contracts` leaf |
| 7 | Features must not depend on each other | ArchUnitNET | keep (intra-band — stayed its own named rule) |
| 8 | No code lives outside the agreed structure (namespace orphan guard) | ArchUnitNET | ✅ **RETIRED** — subsumed by 1+2 |
| 9 | Rule-book parity (block ↔ test) | reflection | keep (auto-covers 1, 2) |

Rules 3–6 kept their *constraint* but their five headers + denylists collapsed into
**one** header (*Dependencies flow down the layer graph only*) + one derived rule;
rule 7 (intra-band) stayed named. Parity confirmed the merge (4 headers ↔ 4 tests).

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
| `src/Host` | `src/Host` | ✅ renamed bare (was `RemoteAgents.Host`) |
| `src/RemoteAgents.Web` | `src/Web` | ✗ prefixed — out of slnx, deferred (own repo) |
| `src/RemoteAgents.Contracts` | `Features/<F>/Contracts` | ✅ dissolved (Step 0) |
| `src/Morph` | — | ✗ stray — out of slnx, deferred (own repo); live dev watch |
| `src/RemoteAgents.Core` | — | ✅ deleted (empty untracked relic) |

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

## Namespace rule → IDE0130 (adopted the SDK analyzer)

The custom filesystem namespace rule (#2) did its job — caught the 4 drifts — but the
**industry standard for namespace-matches-folder is the built-in Roslyn analyzer IDE0130**, not a
home-grown test. So #2 was retired and replaced by IDE0130, enforced as a **build error**:

- **`Directory.Build.props`** (root): `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` (run
  IDE analyzers in `dotnet build`) + `CompilerVisibleProperty` for `RootNamespace`/`ProjectDir` (so
  IDE0130 can compute the expected namespace on command-line builds, not just in VS).
- **`/.editorconfig`**: `dotnet_diagnostic.IDE0130.severity = error`, scoped to `[src/**.cs]` — the
  namespace convention is a *production* invariant; test projects keep their deliberate flat namespace.
- **`src/Features/Directory.Build.props`**: derives `<RootNamespace>` = `RemoteAgents.Features.` + the
  folder under `Features/` (via `MakeRelative`), because our namespace keeps `.Features.` while the
  assembly name drops it. One rule for every slice, so a new feature is correct by default and the
  namespace can't drift — IDE0130 then agrees with the convention. Domain/Infra/Host need no override
  (namespace already == project name).
- **`src/Morph/.editorconfig`** + **`src/RemoteAgents.Web/.editorconfig`**: `IDE0130.severity = none`
  (pending-eviction; protects Morph's live dev watch from this rule).

Why this is better than the custom rule: it's the supported tool, it bites at **compile time** (IDE
squiggle + build error, with an auto-fix) instead of only at test time, and it fixes the *root cause*
— `RootNamespace` now makes "Add new file" default to the correct namespace, which is how the drifts
happened. The custom rule only ever covered in-build code (it skipped Morph/Web), exactly IDE0130's
domain, so nothing was lost. Filesystem rule **#1** (project-under-home) stays — IDE0130 does not do
project placement, and #1 is the Web/Morph blind-spot closer.

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
1. **Decide** canonical home-folder names — ✅ **DONE** (owner: bare folders).
2. **Rename** home folders to bare — ✅ **DONE.** `src/RemoteAgents.Host` →
   `src/Host` (folder-only `git mv`, history preserved; assembly + namespace stay
   `RemoteAgents.Host`; slnx + the one `RemoteAgents.Tests` ProjectReference
   repointed). Empty untracked `src/RemoteAgents.Core` (Paths/, Projects/ — relics
   of the dissolved monolith, no csproj, referenced nowhere) **deleted**. `Morph`
   and `Web` were **not** physically evicted (no destination repo yet, and `Morph`
   has the live port-5210 dev watch): per owner, **`Morph` dropped from
   `RemoteAgents.slnx`** (matching how `Web` was handled at Step 0) so both are
   build-inert, folders left on disk. The morph dev server is unaffected — `dotnet
   watch` targets `src/Morph/Morph.csproj` directly, not the solution. Build 0
   warnings; arch 7/7; FlowTests 8/8.
   **→ Step 3 consequence:** `src/Morph` + `src/RemoteAgents.Web` still sit under
   non-home folders, so the filesystem guard (rule #1) must carry an explicit,
   documented known-pending-eviction list for those two — or they relocate to
   their own repos before Step 3 — so the guard passes *honestly*, not vacuously.
3. **Add** `SourceTree` + `StructureTests` + the 2 rule blocks → ✅ **DONE.**
   `Support/SourceTree.cs` (locate root via `RemoteAgents.slnx` marker, enumerate
   `src/**/*.csproj` + `*.cs` skipping bin/obj/artifacts, parse file-scoped
   namespaces; throws on no-root/zero-projects). `Tests/StructureTests.cs` carries
   rules #1 + #2, both skipping `PendingEvictionFolders` (Morph, Web) — an explicit,
   documented allow-list, plus a **staleness check** that fails if a listed folder
   is gone (so the list shrinks as they leave, never rots). **The allow-graph
   collapse (F1) landed in the same pass:** `ArchitectureModel.Layers` (each band's
   `MayDependOn`) + `ForbiddenEdges()`; `RuleTests` replaced its 4 hand-listed
   denylists (Contracts, Infrastructure, Host-sink, Domain↛Features) with one derived
   `DependenciesFlowDownOnly` (`WithoutRequiringPositiveResults` per edge → the empty
   Contracts band passes honestly). Features↛each-other stayed its own named rule.
   **Rule #2 immediately earned its keep:** it caught **4 real namespace/folder
   drifts** the plan had assumed clean — `PtySession` + `SubscriptionGuard` declared
   `RemoteAgents.Infrastructure.CommandLine` while living in `Domain/Agents` (the
   spawn-wall code masquerading as the floor in the dependency graph), and the
   provisional `DelayArgs`/`DelayOperation` declared `Domain.Flow.Operations` while
   living in `Features/Flows/Definitions`. All four realigned namespace→folder
   (behavior-preserving; graph stayed green). Both filesystem guards negative-tested
   (rule #1 catches a non-excepted Morph; rule #2 catches a corrupted namespace).
4. **Retire** rule #8 (namespace orphan guard) — ✅ **DONE.** Deleted its block +
   `NoCodeOutsideTheAgreedStructure` test; removed now-dead `IsOutsideStructure`,
   `OursPrefix`, and the namespace-form `AgreedHomes` (collapsed to a bare
   `AgreedHomeFolders = {Infrastructure, Domain, Features, Host}` literal — the
   filesystem guard is its only remaining consumer). README brought current
   (two-mechanism framing, new files, allow-graph, retired orphan guard).
5. **Commit** as one coherent green change. Build 0 warnings; ArchTests 5/5 (was
   7/7 with more rules — fewer, stronger); full suite 163 passed / 12 skipped.

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
- ~~**`PtySession` internal to `Domain.Agents`** — the spawn wall.~~ ✅ **DONE** —
  `PtySession` sealed `internal` (only caller `ClaudeProvider`, same assembly); the
  rule *PtySession is internal to Domain.Agents* (`BeInternal()`) enforces it,
  negative-tested. Closes the M1.2 done-when spawn-wall item.
- **Assembly-name convention** (`RemoteAgents.*`, `.Features.` dropped) — not
  enforced; would be a third rule if the Morph-style mis-naming recurs.

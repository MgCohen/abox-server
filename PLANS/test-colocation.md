# Feature-co-located tests (the move from type-major to owner-major)

**Status:** 🟡 proposed — 2026-06-26. Design locked; not built. Successor to
[`test-structure.md`](test-structure.md) (which stood up the type-major `tests/Tests/<Type>/`
layout this plan re-homes). Supersedes nothing in behavior — the Rulebook discipline, ParityGuard,
and the Meta self-suite are **kept**; only *where the tests physically live* changes.

**One-line intent:** a test lives **with the thing it guarantees** — a feature's tests inside the
feature folder, the doc-engine's tests inside the doc-engine — while a single central harness still
enforces the same templates, rules, and parity across all of them, and `dotnet test` stays one command.

---

## Why move

Today every test type is a folder under one central tree (`tests/Tests/Unit/`, `…/Wire/`, …) inside
one assembly (`ABox.Tests`). That gives a clean enforcement point but a real cost: **a feature's tests
live far from the feature.** Adding a test for the doc-engine means editing the central tree; the
feature is not self-contained; and the central tree is a protected path, so every feature test crosses
a governance wall. We want feature tests to be **local and self-contained** without losing the central
guarantee that they follow the standard.

The two things people assume are one knob are actually two:

| Axis | Question | This plan's answer |
|---|---|---|
| **Location** | Where does the test *code* sit? | With its owner — the feature / tool folder |
| **Enforcement** | What keeps tests to the standard? | One central harness (engine + templates + Meta), discovered, not co-located |

A harness is a **contract, not a folder.** It must *discover and enforce*, not *store*. So we keep the
contract central and distribute the tests.

---

## The organizing rule: ownership, not blast radius

A test is **central** only if **no single feature owns its guarantee.** Otherwise it co-locates with
its owner — *however many features it transitively exercises.* "Touches many features" never promotes
a test to central.

| Test | Owns the guarantee? | Home |
|---|---|---|
| **Arch** (dependency graph), **Structure** (placement), **Meta** (the test system) | the repo / the test system — **no feature** | **central** (`tests/`) |
| **Unit** (one slice) | the feature | `src/Features/<F>/Tests/Unit/` |
| **Wire** (a feature's endpoint) | the feature | `src/Features/<F>/Tests/Wire/` |
| **E2E** (a flow, even though it drives agents + steps + git) | **Flows** | `src/Features/Flows/.../Tests/E2E/` — co-located |
| **Live** (a real CLI turn) | the agent/provider feature | co-located there |
| Doc-engine unit tests | the doc-engine tool | `tools/doc-engine/Tests/` |

The only residue is a genuinely **ownerless** endpoint (e.g. `GET /health` if no feature owns the
health surface). That isn't an exception — it resolves to "whatever owns it; central *only* if nothing
does." No type is split by name; the line is purely **owner vs. ownerless.**

---

## Final shape

The end state, on disk:

```
tests/                              → central — OWNERLESS guarantees only
  Harness/                            ABox.Tests.Harness  (the contract; referenced by ALL test asms)
    Rule.cs                             the [Rule("…")] attribute
    ParityGuard.cs                      load rules + reflect [Rule]s + assert parity (per assembly × type)
    RepoTree.cs                         multi-root discovery: central tree + every src|tools **/Tests/
    TestTypes.cs                        the type catalog + central-vs-feature classification
    Templates/                          the TYPE CONTRACTS, once each (what a Unit/Wire/E2E/Live test is)
      unit.template.md   wire.template.md   e2e.template.md   live.template.md
    Support/                            shared per-type fixtures promoted here (FlowHarness, WebApp base,
                                        ScriptedProvider, [LiveFact]) — referenced by feature test asms
    README.md                           the test-wide convention

  Arch/        ┐  ABox.Tests.Central — the ownerless types (no production-feature reference)
    Rulebook/  │    Arch:      reference-graph invariants (ArchUnitNET)
    Tests/     │    Structure: placement invariants (filesystem scan)
  Structure/   ┘
    Rulebook/  Tests/

  Meta/                                ABox.Tests.Meta — the self-suite (unchanged in spirit; reflects
    Rulebook/  Tests/                  over ALL *.Tests assemblies now, not just one)

src/Features/Tasks/
  …feature code…
  Tests/                              → ABox.Tasks.Tests  (co-located, discovered by glob)
    Unit/
      Rulebook/ rules.md                THIS feature's Unit guarantees (template lives central)
      <…>Tests.cs
    Wire/
      Rulebook/ rules.md
      <…>Tests.cs
    Parity.cs                           one line per type: ParityGuard.For(thisAssembly, "Unit").Assert()

tools/doc-engine/
  …tool code (standalone, own YamlDotNet, not in ABox.slnx)…
  Tests/                              → ABox.DocEngine.Tests
    Unit/   Rulebook/ rules.md  <…>Tests.cs      the engine's own behavior
    Docs/   Rulebook/ rules.md  <…>Tests.cs      validate every out/*.md instance (shells to the CLI)
```

What each piece owns, stated once:

| Piece | Owns |
|---|---|
| `Harness/` | the engine (`Rule`, `ParityGuard`, `RepoTree`, `TestTypes`), the **type templates** (the standard), and shared per-type **fixtures**. Zero-dependency + AspNetCore test base only — never references a feature. |
| `tests/{Arch,Structure}/` (`ABox.Tests.Central`) | the ownerless graph/placement invariants |
| `tests/Meta/` | the self-suite that polices taxonomy + parity across **every** test assembly |
| `src/Features/<F>/Tests/` (`ABox.<F>.Tests`) | that feature's tests (types as internal subfolders) + that feature's **`rules.md`** per type |
| `tools/<T>/Tests/` (`ABox.<T>.Tests`) | a standalone tool's tests, same shape |

**Template vs. rules — the split that makes "central standard, local guarantees" work:**

- **Template = central, once per type.** "What a Unit test must look like; its criteria." Lives in
  `Harness/Templates/`. This is the *standard* (your point: enforce template/standards).
- **`rules.md` = local, per feature × type.** "The specific guarantees *this* feature makes." Lives in
  the feature's `Tests/<Type>/Rulebook/`. Parity bridges these local rules to the local `[Rule]` tests.

---

## How the harness still enforces everything

Four engine changes turn the centralized harness into a **discover-and-enforce** harness. All four are
in `tests/Harness/**` + `tests/Meta/**` — the protected enforcement surface — so they land via an
owner-reviewed PR, carefully, *with* the move (never after).

| # | Today | Becomes |
|---|---|---|
| 1 | `RepoTree.TestsRoot` = the single `tests/Tests/` dir | **Multi-root discovery.** `RepoTree` enumerates the central tree **plus** every `src/**/Tests/` and `tools/**/Tests/`. One method returns the full set of (assembly, type-subfolder, rulebook) triples. |
| 2 | `TestTypes.Registered` = 6 type names; namespace `ABox.Tests.<Type>.Tests` | Type catalog stays, **gains a classification**: `Central = {Arch, Structure}` vs `Feature = {Unit, Wire, E2E, Live}`. Namespace convention becomes per-assembly: `ABox.<Feature>.Tests.<Type>`. |
| 3 | `ParityGuard.For(asm, type)` scopes `[Rule]`s by namespace **within one assembly** | Unchanged in mechanism — it already takes `(assembly, scope, rulesPath)`. Now called **once per (feature assembly × type-subfolder)** against that subfolder's co-located `rules.md`. The scoping that already stops Arch/Structure bleeding is exactly what keeps Tasks.Unit from bleeding into Tasks.Wire. |
| 4 | Meta reflects over the single `ABox.Tests` product assembly | Meta **discovers and loads every `*.Tests` assembly** (the traversal set) and runs parity + taxonomy across all. **New load-bearing Rule:** *every feature `Tests/` folder has ≥1 paired Rule* — the backstop that stops a feature shipping untested once tests are no longer behind the central protected wall. |

> ADR-0013 guard preserved: *the Harness MUST NOT depend on the doc-engine.* Co-locating the
> doc-engine's tests does **not** break this — `Harness` depends on nothing; `ABox.DocEngine.Tests`
> depends on `Harness` (arrow points out of the spine, the right way). Co-location is therefore an
> *upgrade* for the doc-engine: its internal unit tests join the real harness instead of only being
> reachable by shell-out.

---

## Zero hand-wiring (the hard constraint)

Adding a test, or a whole new feature's `Tests/` folder, must require **no manual wiring**. Two
mechanisms together deliver that:

1. **A traversal/glob build.** A traversal project (MSBuild `Microsoft.Build.Traversal`, or a
   scaffold-maintained solution) globs `**/*.Tests.csproj`. A new `Tests/` folder is picked up
   automatically; `dotnet test` over the traversal root stays **one command**. Adding a feature's
   tests touches **no** central file (`ABox.slnx`, no harness registration).
2. **A scaffold skill** (`new-feature-tests`) stamps the `Tests/` folder, a ~5-line stub
   `ABox.<F>.Tests.csproj` (all real config inherited from `Directory.Build.props`), the per-type
   `Rulebook/` skeleton from the central template, and the one-line `Parity.cs`.

So wiring *exists* but **no human types it** — the agent-first resolution. The only thing a human
authors is the test body and its `### ` Rule, which is the contract, not wiring.

**Assembly granularity = per feature/tool**, because that is the unit of co-location and isolation
(a feature's tests build and reference independently; the doc-engine keeps its YamlDotNet world to
itself). Assembly *count* scales with features, but the *cost* of a new one is paid by the scaffold,
not by hand — and Meta fails the build if a feature has a `Tests/` folder with no registered assembly.

---

## Governance remap

Co-location moves protected surfaces, so `governance/protected-paths` must follow:

| Path | Change |
|---|---|
| `tests/**/Rulebook/**` | narrow to the central rulebooks (`tests/{Arch,Structure,Meta}/Rulebook/**`) |
| `src/**/Tests/Rulebook/**`, `tools/**/Tests/Rulebook/**` | **add** — feature/tool rules are guarantees, still protected |
| `tests/Harness/**`, `tests/Meta/**` | unchanged (still critical) |
| `Harness/Templates/**` | **add** (critical) — the type contracts are the standard |
| CODEOWNERS | regenerate from the above |

A feature's **test code** (`Tests/<Type>/*.cs`) is *not* protected — feature authors own it. Only the
**rules** and the **templates** stay behind the wall. The guarantee that a feature can't ship a weak or
missing test moves from "the central tree is protected" to "the Meta self-suite enforces coverage +
parity" — which is why Meta change #4 is load-bearing and lands with the move.

---

## Build order (done-when gates)

**Phase 0 — Discovery seam (no test moves yet).**
Generalize `RepoTree` from the single `tests/Tests/` root to multi-root discovery (central +
`src|tools **/Tests/`); add the traversal/glob build; keep the current type-major tree in place and
prove the new discovery finds exactly today's tests.
*Done when:* `dotnet test` over the traversal root runs the identical suite, green; `RepoTree`
returns the same (assembly, type, rulebook) set it does today; build warning-free.

**Phase 1 — Split Harness: engine + templates + fixtures.**
Move the type **templates** into `Harness/Templates/` (central, once each); promote shared per-type
fixtures (`FlowHarness`, the `WebApplicationFactory` base, `ScriptedProvider`, `[LiveFact]`) into
`Harness/Support/`. Leave feature `rules.md` to move with their tests in Phase 3.
*Done when:* every type's template resolves centrally; the central `Arch`/`Structure` parity still
green; no fixture is duplicated.

**Phase 2 — Central assembly = ownerless only.**
Reshape the central tree to `Arch` + `Structure` (+ `Meta` self-suite) — the `ABox.Tests.Central`
assembly. The behavioral types (`Unit/E2E/Wire/Live`) stay temporarily but are now *marked* feature
candidates.
*Done when:* `ABox.Tests.Central` references no production feature; Arch/Structure/Meta green.

**Phase 3 — Move one feature, prove the pattern end-to-end.**
Pick one in-solution feature (e.g. `Tasks`). Create `src/Features/Tasks/Tests/` with the internal
type subfolders, its co-located `rules.md` per type, the one-line `Parity.cs`; migrate its existing
Unit/Wire tests out of the central tree (git-rename, namespaces → `ABox.Tasks.Tests.<Type>`).
*Done when:* `ABox.Tasks.Tests` builds via the scaffold stub, its parity is green per type, the
traversal picks it up with no `ABox.slnx`/harness edit, and the central tree no longer holds Tasks tests.

**Phase 4 — Meta over N assemblies + the coverage Rule.**
Teach Meta to discover and reflect over **every** `*.Tests` assembly; add the *"every feature `Tests/`
folder has paired Rules"* guard. Prove it red→green (a feature folder with no Rule fails the build).
*Done when:* Meta parity holds across all assemblies; the coverage guard bites; default + `RUN_LIVE=1`
both behave.

**Phase 5 — Migrate the rest + the doc-engine.**
Move the remaining features' tests to co-located homes (one feature per commit). Bring
`tools/doc-engine/Tests/` under the harness: its `Unit` tests join directly; its instance-validation
guarantee becomes a co-located `Docs` test (keep the CLI shell-out as the mechanism, per ADR 0013).
*Done when:* no behavioral test remains in the central tree; the central tree is ownerless-only; the
doc-engine's own tests run under `dotnet test`; full suite green & warning-free.

**Phase 6 — Governance remap + scaffold skill.**
Apply the `protected-paths` changes, regenerate CODEOWNERS, ship the `new-feature-tests` scaffold skill.
*Done when:* protected paths match the new homes; adding a feature's tests via the skill needs zero
manual wiring and lands behind the right review wall.

---

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| The Harness→multi-assembly Meta refactor silently weakens parity (a feature slips in untested) | The coverage Rule (Phase 4) lands **with** the move and is proven red→green; parity is asserted per assembly, not globally |
| Assembly explosion (~25 csproj) raises maintenance | `Directory.Build.props` carries all config; each csproj is a scaffold-stamped ~5-line stub |
| Traversal/glob misses or double-counts an assembly | Meta cross-checks the discovered set against the traversal set and fails on mismatch |
| Doc-engine co-location re-introduces a spine→engine dependency | It doesn't — `Harness` stays zero-dep; only `ABox.DocEngine.Tests` references `Harness`. Verified by an Arch/Meta check that no `*.Tests` reference flows the wrong way |
| `GET /health`-style ownerless tests have no obvious home | Rule is explicit: central *only* if no feature owns it; default is the owning feature (Host) |

---

## Decisions taken

- **Ownership, not blast radius** — central is for ownerless guarantees (Arch/Structure/Meta); a flow
  E2E belongs to **Flows** and co-locates even though it drives other features.
- **Template central, rules local** — the type contract is the central standard; each feature's `rules.md`
  states that feature's own guarantees, parity-bridged to its co-located tests.
- **Per-feature/tool assembly** — the unit of co-location and isolation; cost paid by a scaffold, not by hand.
- **Zero manual wiring** — traversal/glob build + scaffold skill; `dotnet test` stays one command.
- **Meta becomes load-bearing** — the guarantee that no feature ships untested moves from "central tree is
  protected" to "Meta enforces coverage + parity across all assemblies."
- **Doc-engine co-locates** — its internal unit tests join the harness directly; its instance-validation
  stays a CLI shell-out (ADR 0013 dependency arrow preserved).

## Non-goals

- **No change to behavior, taxonomy meaning, or the Rulebook/parity discipline** — only test *location*.
- **No per-feature reinvention of test machinery** — shared fixtures stay central in `Harness/Support/`;
  features hold test *bodies* + *rules*, never the engine.
- **No global "all Wire in one place" grouping** — type-major grouping is deliberately subordinated to
  ownership; "all Wire across features" is a query, not a folder.
- **No move without its guard** — every phase that relocates a protected surface lands its Meta/parity
  guard in the same PR.

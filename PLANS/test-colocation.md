# Feature-co-located tests (the move from type-major to owner-major)

**Status:** 🟡 proposed — 2026-06-26. Design locked; not built. Successor to
[`test-structure.md`](test-structure.md) (which stood up the type-major `tests/Tests/<Type>/`
layout this plan re-homes). **Based on the post-#98 tree** — the Rulebook ⇄ doc-engine stack has
landed: rulebooks are now doc-engine **documents**, a **`Docs`** test type validates them by shelling
out, `RulebookFormat` is deleted, and the dependency-arrow decision is [ADR 0015](../design/adr/0015-rulebook-as-document.md).
Supersedes nothing in behavior — the Rulebook discipline, ParityGuard, and the Meta self-suite are
**kept**; only *where the tests physically live* changes.

**One-line intent:** a test lives **with the thing it guarantees** — a feature's tests inside the
feature folder, the doc-engine's tests inside the doc-engine — while a single central harness still
enforces the same doctypes, templates, and parity across all of them, and `dotnet test` stays one command.

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
| **Enforcement** | What keeps tests to the standard? | One central harness (engine + doc-engine doctypes + `Docs` + Meta), discovered, not co-located |

A harness is a **contract, not a folder.** It must *discover and enforce*, not *store*. So we keep the
contract central and distribute the tests. Post-#98 the repo already proves the enabling primitive: the
doc-engine validates a document **in place, in its home folder** (there is no global `out/`). A
co-located rulebook is therefore validated *where it lives* — co-location is already a first-class idea
in the engine; this plan extends it to the test *code*.

---

## The organizing rule: ownership, not blast radius

A test is **central** only if **no single feature owns its guarantee.** Otherwise it co-locates with
its owner — *however many features it transitively exercises.* "Touches many features" never promotes
a test to central.

| Type | Owns the guarantee? | Home |
|---|---|---|
| **Arch** (dependency graph), **Structure** (placement), **Meta** (the test system) | the repo / the test system — **no feature** | **central** (`tests/`) |
| **Docs** (every doc-engine instance validates; catalog self-consistent) | the *document standard* — repo-wide, **no feature** | **central** (validates co-located rulebooks in place) |
| **Unit** (one slice) | the feature | `src/Features/<F>/Tests/Unit/` |
| **Wire** (a feature's endpoint) | the feature | `src/Features/<F>/Tests/Wire/` |
| **E2E** (a flow, even though it drives agents + steps + git) | **Flows** | `src/Features/Flows/.../Tests/E2E/` — co-located |
| **Live** (a real CLI turn) | the agent/provider feature | co-located there |
| Doc-engine unit tests | the doc-engine tool | `tools/doc-engine/Tests/` |

**Docs is the instructive case:** it stays central (the *document standard* is ownerless), yet it
reaches *into* feature folders to validate their co-located rulebooks. That's the whole model in one
type — **validation-of-the-standard is central; the guarantees themselves are local.**

The only residue is a genuinely **ownerless** endpoint (e.g. `GET /health` if no feature owns the
health surface). That isn't an exception — it resolves to "whatever owns it; central *only* if nothing
does." No type is split by name; the line is purely **owner vs. ownerless.**

---

## Final shape

The end state, on disk:

```
tools/doc-engine/                   → the catalog IS the central standard (already standalone, not in slnx)
  kinds/  blocks/  doctypes/          doctypes rulebook + test-template = the schema every rulebook obeys
  Tests/                             → ABox.DocEngine.Tests (the tool's own tests join the harness)
    Unit/  Rulebook/ rules.md  <…>Tests.cs

tests/                              → central — OWNERLESS guarantees only
  Harness/                            ABox.Tests.Harness  (the contract; referenced by ALL test asms)
    Rule.cs                             the [Rule("…")] attribute
    ParityGuard.cs                      load rules + reflect [Rule]s + assert parity (per assembly × type)
    RepoTree.cs                         multi-root discovery: central tree + every src|tools **/Tests/
    TestTypes.cs                        the type catalog + central-vs-feature classification
    Support/                            shared per-type fixtures (FlowHarness, WebApp base, ScriptedProvider,
                                        [LiveFact], the DocEngine shell-out) — referenced by feature test asms
    README.md                           the test-wide convention (rulebook front-matter `harness:` target)

  Arch/   Structure/   ┐  ABox.Tests.Central — ownerless types (no production-feature reference)
    Rulebook/ Tests/   │    Arch:      reference-graph invariants (ArchUnitNET)
  Docs/                ┘    Structure: placement invariants (filesystem scan)
    Rulebook/ Tests/        Docs:      catalog `check` + `validate` EVERY rulebook instance repo-wide
    Support/DocEngine.cs                (discovered via RepoTree, shells to the docengine CLI — ADR 0015)

  <Type>/Rulebook/template.md        the per-type test-template INSTANCE — central, ONE per type
                                      (the criteria; what a Unit/Wire/E2E/Live test must be)

  Meta/                              ABox.Tests.Meta — the self-suite (reflects over ALL *.Tests asms now)
    Rulebook/  Tests/

src/Features/Tasks/
  …feature code…
  Tests/                            → ABox.Tasks.Tests  (co-located, discovered by glob)
    Unit/
      Rulebook/ rules.md              a `rulebook` INSTANCE — THIS feature's guarantees; front-matter
                                      `template:` → the central per-type template, `harness:` → Harness
      <…>Tests.cs
    Wire/
      Rulebook/ rules.md
      <…>Tests.cs
    Parity.cs                         one line per type: ParityGuard.For(thisAssembly, "Unit").Assert()
```

What each piece owns, stated once:

| Piece | Owns |
|---|---|
| `tools/doc-engine/{doctypes,blocks,kinds}` | the **standard** — the `rulebook` + `test-template` doctypes every rulebook validates against (one central catalog) |
| `tests/Harness/` | the engine (`Rule`, `ParityGuard`, `RepoTree`, `TestTypes`) + shared per-type **fixtures**. Zero-dependency + AspNetCore test base only — never references a feature **or the doc-engine** (ADR 0015) |
| `tests/{Arch,Structure}/` (`ABox.Tests.Central`) | the ownerless graph/placement invariants |
| `tests/Docs/` | the repo-wide document guarantee — validates every rulebook instance **in place** by shelling to `docengine` |
| `tests/<Type>/Rulebook/template.md` | the per-type **criteria** — one central `test-template` instance per type |
| `tests/Meta/` | the self-suite policing taxonomy + parity across **every** test assembly |
| `src/Features/<F>/Tests/` (`ABox.<F>.Tests`) | that feature's tests (types as internal subfolders) + that feature's **`rules.md`** per type |
| `tools/<T>/Tests/` (`ABox.<T>.Tests`) | a standalone tool's tests, same shape |

**The standard / criteria / guarantees split (how "central standard, local guarantees" lands post-#98):**

| Layer | What it is | Doc-engine role | Home |
|---|---|---|---|
| **Doctype** (`rulebook`, `test-template`) | the schema — what *any* rulebook/template must look like | the catalog | **central** (`tools/doc-engine/doctypes/`) |
| **template.md** | the per-*type* criteria ("what a Unit test is") | a `test-template` **instance** | **central**, one per type (not duplicated per feature) |
| **rules.md** | this *feature's* guarantees | a `rulebook` **instance**, `template:`→central template | **co-located** with the feature |

The doc-engine validates every instance against its doctype **wherever it lives**; ParityGuard bridges
the `### ` headers in a co-located `rules.md` to the `[Rule]` tests beside it. Standard central, criteria
central-per-type, guarantees local.

---

## How the harness still enforces everything

Four engine changes turn the centralized harness into a **discover-and-enforce** harness. All four are
in `tests/Harness/**` + `tests/Meta/**` — the protected enforcement surface — so they land via an
owner-reviewed PR, carefully, *with* the move (never after).

| # | Today (post-#98) | Becomes |
|---|---|---|
| 1 | `RepoTree.TestsRoot` = the single `tests/Tests/` dir; `RepoTree.RulebookFolders()` enumerates `tests/Tests/*/Rulebook` | **Multi-root discovery.** Enumerates the central tree **plus** every `src/**/Tests/` and `tools/**/Tests/`. This one method already feeds **two** consumers — `ParityGuard` *and* the `Docs` type's `Instances_validate` — so generalizing it co-locates both at once. |
| 2 | `TestTypes.Registered` = `{Arch,Structure,Unit,E2E,Wire,Live,Docs}`; namespace `ABox.Tests.<Type>.Tests` | Catalog stays, **gains a classification**: `Central = {Arch, Structure, Docs}` vs `Feature = {Unit, Wire, E2E, Live}`. Namespace convention becomes per-assembly: `ABox.<Feature>.Tests.<Type>`. |
| 3 | `ParityGuard.For(asm, type)` scopes `[Rule]`s by namespace within one assembly | Unchanged mechanism — it already takes `(assembly, scope, rulesPath)`. Now called **once per (feature assembly × type-subfolder)** against that subfolder's co-located `rules.md`. The scoping that already stops Arch/Structure bleeding is exactly what keeps Tasks.Unit from bleeding into Tasks.Wire. |
| 4 | Meta reflects over the single `ABox.Tests` product assembly | Meta **discovers and loads every `*.Tests` assembly** and runs parity + taxonomy across all. **New load-bearing Rule:** *every feature `Tests/` folder has ≥1 paired Rule* — the backstop that stops a feature shipping untested once tests are no longer behind the central protected wall. |

> **No `RulebookFormat` to worry about** — #98 deleted it; the intra-document shape checks now live in
> the doc-engine doctypes and are enforced by the `Docs` type. So the harness's only jobs are **parity**
> (test-side, `ParityGuard`) and **discovery** (`RepoTree`); document *shape* is the doc-engine's job,
> invoked through `Docs`. This is exactly the ADR-0015 split, and co-location doesn't disturb it.

> **ADR-0015 guard preserved:** *the Harness MUST NOT depend on the doc-engine.* Co-locating the
> doc-engine's own tests does **not** break this — `Harness` depends on nothing; `ABox.DocEngine.Tests`
> depends on `Harness` (arrow points out of the spine, the right way). Co-location is therefore an
> *upgrade* for the doc-engine: its internal unit tests join the real harness instead of only being
> reachable by the `Docs` shell-out.

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
   `rules.md` skeleton with correct front-matter (`docType: rulebook`, `template:`→central template,
   `harness:`→Harness README), and the one-line `Parity.cs`.

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
| `tests/**/Rulebook/**` | narrow to the central rulebooks (`tests/{Arch,Structure,Docs,Meta}/Rulebook/**`) + the central per-type `template.md` instances |
| `src/**/Tests/Rulebook/**`, `tools/**/Tests/Rulebook/**` | **add** — feature/tool `rules.md` are guarantees, still protected |
| `tests/Harness/**`, `tests/Meta/**` | unchanged (still critical) |
| `tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` | confirm protected — the doctypes are now the central *standard* the whole repo validates against |
| CODEOWNERS | regenerate from the above |

A feature's **test code** (`Tests/<Type>/*.cs`) is *not* protected — feature authors own it. Only the
**rules**, the **templates**, and the **doctypes** stay behind the wall. The guarantee that a feature
can't ship a weak or missing test moves from "the central tree is protected" to "the Meta self-suite
enforces coverage + parity, and the `Docs` type validates every rulebook in place" — which is why Meta
change #4 is load-bearing and lands with the move.

---

## Build order (done-when gates)

**Phase 0 — Discovery seam (no test moves yet).**
Generalize `RepoTree` from the single `tests/Tests/` root to multi-root discovery (central +
`src|tools **/Tests/`), feeding both `ParityGuard` and the `Docs` type's `RulebookFolders()` scan; add
the traversal/glob build; keep the current type-major tree in place and prove the new discovery finds
exactly today's tests + rulebook instances.
*Done when:* `dotnet test` over the traversal root runs the identical suite, green; `RepoTree` returns
the same (assembly, type, rulebook) set it does today; `Docs` validates the same instance set; build
warning-free.

**Phase 1 — Central assembly = ownerless only; classify the types.**
Reshape the central tree to `Arch` + `Structure` + `Docs` (+ `Meta` self-suite) — the
`ABox.Tests.Central` assembly. Add the `Central`/`Feature` classification to `TestTypes`. The per-type
`template.md` instances stay central, one per type; behavioral types (`Unit/E2E/Wire/Live`) are marked
feature candidates but not yet moved.
*Done when:* `ABox.Tests.Central` references no production feature; Arch/Structure/Docs/Meta green; the
classification is asserted by a Meta test.

**Phase 2 — Move one feature, prove the pattern end-to-end.**
Pick one in-solution feature (e.g. `Tasks`). Create `src/Features/Tasks/Tests/` with the internal type
subfolders, its co-located `rules.md` per type (front-matter pointing at the central template), the
one-line `Parity.cs`; migrate its existing Unit/Wire tests out of the central tree (git-rename,
namespaces → `ABox.Tasks.Tests.<Type>`).
*Done when:* `ABox.Tasks.Tests` builds via the scaffold stub, its parity is green per type, `Docs`
validates the relocated `rules.md` **in place**, the traversal picks it up with no `ABox.slnx`/harness
edit, and the central tree no longer holds Tasks tests.

**Phase 3 — Meta over N assemblies + the coverage Rule.**
Teach Meta to discover and reflect over **every** `*.Tests` assembly; add the *"every feature `Tests/`
folder has paired Rules"* guard. Prove it red→green (a feature folder with no Rule fails the build).
*Done when:* Meta parity holds across all assemblies; the coverage guard bites; default + `RUN_LIVE=1`
both behave.

**Phase 4 — Migrate the rest + the doc-engine.**
Move the remaining features' tests to co-located homes (one feature per commit). Bring
`tools/doc-engine/Tests/` under the harness: its `Unit` tests join directly (it references `Harness`,
never the reverse). The repo-wide `Docs` guarantee stays central and now also covers the doc-engine's
co-located rulebook.
*Done when:* no behavioral test remains in the central tree; the central tree is ownerless-only
(`Arch`/`Structure`/`Docs`/`Meta`); the doc-engine's own tests run under `dotnet test`; full suite green
& warning-free.

**Phase 5 — Governance remap + scaffold skill.**
Apply the `protected-paths` changes, regenerate CODEOWNERS, ship the `new-feature-tests` scaffold skill.
*Done when:* protected paths match the new homes; adding a feature's tests via the skill needs zero
manual wiring and lands behind the right review wall.

---

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| The Harness→multi-assembly Meta refactor silently weakens parity (a feature slips in untested) | The coverage Rule (Phase 3) lands **with** the move and is proven red→green; parity is asserted per assembly, not globally |
| Assembly explosion (~25 csproj) raises maintenance | `Directory.Build.props` carries all config; each csproj is a scaffold-stamped ~5-line stub |
| Traversal/glob misses or double-counts an assembly | Meta cross-checks the discovered set against the traversal set and fails on mismatch |
| `Docs` (central) reaching into feature folders re-introduces a spine→engine coupling | It doesn't — `Docs` shells out to the CLI (ADR 0015), and only the `Docs` *Support* helper touches the tool; `Harness` stays zero-dep. Verified by the existing `[det]` confirmation in ADR 0015 |
| Per-type `template.md` drifts from the central doctype | Already an accepted, tracked cost in ADR 0015 ("a Meta test can later assert they agree"); keep them central + single-per-type so there's one to reconcile |
| `GET /health`-style ownerless tests have no obvious home | Rule is explicit: central *only* if no feature owns it; default is the owning feature (Host) |

---

## Decisions taken

- **Ownership, not blast radius** — central is for ownerless guarantees (Arch/Structure/Docs/Meta); a
  flow E2E belongs to **Flows** and co-locates even though it drives other features.
- **Standard central, guarantees local** — the doc-engine **doctype** and the per-type **template.md**
  stay central; each feature's `rules.md` (a `rulebook` instance) co-locates, parity-bridged to its tests.
- **Docs stays central but validates in place** — the document standard is ownerless; it reaches into
  feature folders to validate their co-located rulebooks, leaning on the engine's "no global `out/`" primitive.
- **Per-feature/tool assembly** — the unit of co-location and isolation; cost paid by a scaffold, not by hand.
- **Zero manual wiring** — traversal/glob build + scaffold skill; `dotnet test` stays one command.
- **Meta becomes load-bearing** — the guarantee that no feature ships untested moves from "central tree is
  protected" to "Meta enforces coverage + parity across all assemblies."
- **Doc-engine co-locates** — its internal unit tests join the harness directly (arrow out of the spine,
  ADR 0015 preserved).

## Non-goals

- **No change to behavior, taxonomy meaning, or the Rulebook/parity discipline** — only test *location*.
- **No per-feature reinvention of test machinery** — shared fixtures stay central in `Harness/Support/`;
  features hold test *bodies* + *rules*, never the engine, doctypes, or templates.
- **No global "all Wire in one place" grouping** — type-major grouping is deliberately subordinated to
  ownership; "all Wire across features" is a query, not a folder.
- **No move without its guard** — every phase that relocates a protected surface lands its Meta/parity
  guard in the same PR.
- **No merge of the doc-engine into the harness** — ADR 0015 stands; we co-locate tests, we do not invert
  the dependency arrow.

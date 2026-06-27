# Feature-co-located tests (the move from type-major to owner-major)

**Status:** 🟢 implemented — 2026-06-27. The migration shipped: central suites live in `ABox.Tests.Central`,
every feature owns a co-located `ABox.<Owner>.Tests` (including the doc-engine's own `ABox.DocEngine.Tests`),
and the Meta self-suite — `CoverageTests` plus the co-located taxonomy sweeps — polices them. Successor to
[`test-structure.md`](test-structure.md), which stood up the type-major `tests/Tests/<Type>/` layout
this plan re-homes. The Rulebook discipline, ParityGuard, the `Docs` type, and the Meta self-suite are
all **kept** — only *where the tests physically live* changes. Canonical references for the machinery it
builds on: [`tests/README.md`](../tests/README.md) (the test taxonomy),
[`tests/Harness/README.md`](../tests/Harness/README.md) (the Rulebook convention), and
[ADR 0015](../design/adr/0015-rulebook-as-document.md) (a Rulebook is a doc-engine document; the
dependency arrow points out of the enforcement spine).

**One-line intent:** a test lives **with the thing it guarantees** — a feature's tests inside the
feature folder, the doc-engine's tests inside the doc-engine — while one central harness still enforces
the same doctypes, templates, and parity across all of them, and `dotnet test` stays a single command.

---

## Vocabulary (for a cold reader)

| Term | One-line meaning |
|---|---|
| **Test type** | one of eight kinds of guarantee: `Arch`, `Structure`, `Unit`, `E2E`, `Wire`, `Live`, `Docs` (product) + `Meta` (the self-suite). |
| **Rulebook** (`rules.md`) | a markdown file whose `### ` headers each state one guarantee a type makes. |
| **`[Rule("…")]`** | an xUnit attribute on a test method, naming the `### ` header it proves. |
| **ParityGuard** | the engine that fails the build if a `### ` Rule has no `[Rule]` test, or a `[Rule]` test cites no Rule. |
| **doc-engine** | a standalone CLI (`tools/doc-engine/`) that validates structured markdown. A **doctype** is a schema; an **instance** is a document obeying it. A Rulebook is now an instance of the `rulebook` doctype; a per-type `template.md` is an instance of the `test-template` doctype. |
| **`Docs` type** | the test type that runs the doc-engine over every rulebook/template instance, so document *shape* is CI-checked without the harness depending on the engine (ADR 0015). |
| **Meta self-suite** | tests that police the test system itself — taxonomy, parity — from a separate assembly. |

---

## Why move

Today every test type is a folder under one central tree (`tests/Tests/Unit/`, `…/Wire/`, …) inside one
assembly (`ABox.Tests`). That gives a clean enforcement point but a real cost: **a feature's tests live
far from the feature.** Adding a test for the doc-engine means editing the central tree; the feature is
not self-contained; and the central tree is a protected path, so every feature test crosses a governance
wall. We want feature tests **local and self-contained** without losing the central guarantee that they
follow the standard.

The two things people assume are one knob are actually two:

| Axis | Question | This plan's answer |
|---|---|---|
| **Location** | Where does the test *code* sit? | With its owner — the feature / tool folder |
| **Enforcement** | What keeps tests to the standard? | One central harness (engine + doc-engine doctypes + `Docs` + Meta), discovered, not co-located |

A harness is a **contract, not a folder.** It must *discover and enforce*, not *store*. So we keep the
contract central and distribute the tests. The enabling primitive already exists: the doc-engine
validates a document **in place, in its home folder** — there is no global output directory. A co-located
rulebook is therefore validated *where it lives*; this plan extends that same locality to the test *code*.

---

## The organizing rule: ownership, not blast radius

A test is **central** only if **no single feature owns its guarantee.** Otherwise it co-locates with its
owner — *however many features it transitively exercises.* "Touches many features" never promotes a test
to central.

| Type | Owns the guarantee? | Home |
|---|---|---|
| **Arch** (dependency graph), **Structure** (placement), **Meta** (the test system) | the repo / the test system — **no feature** | **central** (`tests/`) |
| **Docs** (every doc instance validates; catalog self-consistent) | the *document standard* — repo-wide, **no feature** | **central** (validates co-located rulebooks in place) |
| **Unit** (one slice) | the feature | `src/Features/<F>/Tests/Unit/` |
| **Wire** (a feature's endpoint) | the feature | `src/Features/<F>/Tests/Wire/` |
| **E2E** (a flow, even though it drives agents + steps + git) | **Flows** | `src/Features/Flows/Tests/E2E/` — co-located |
| **Live** (a real CLI turn) | the agent/provider feature | `src/Domain/Agents/Tests/Live/` |
| Doc-engine unit tests | the doc-engine tool | `tools/doc-engine/Tests/` |

**Docs is the instructive case:** it stays central (the *document standard* is ownerless), yet it reaches
*into* feature folders to validate their co-located rulebooks. That's the whole model in one type —
**validation-of-the-standard is central; the guarantees themselves are local.**

The only residue is a genuinely **ownerless** endpoint (e.g. `GET /health` if no feature owns the health
surface). That isn't an exception — it resolves to "whatever owns it; central *only* if nothing does." No
type is split by name; the line is purely **owner vs. ownerless.**

---

## Final shape

The complete end state, using the repo's real features and assembly names. Three zones: **the standard**
(doc-engine catalog), **central tests** (ownerless only), and **co-located feature tests**.

```
abox-server/
│
├── tools/doc-engine/                         ← THE STANDARD (standalone tool, not in ABox.slnx)
│   ├── kinds/  blocks/  _schema/
│   ├── doctypes/
│   │   ├── rulebook.yaml                        schema every rules.md validates against
│   │   └── test-template.yaml                   schema every template.md validates against
│   ├── …engine code (Program.cs, DocValidator.cs; carries its own YamlDotNet)…
│   └── Tests/                                 → ABox.DocEngine.Tests   (the tool's own tests join the harness)
│       └── Unit/
│           ├── Rulebook/ rules.md               front-matter: template→…, harness→Harness README
│           └── DocValidatorTests.cs
│
├── tests/                                     ← CENTRAL: ownerless guarantees only
│   ├── Harness/                              → ABox.Tests.Harness   (the contract; referenced by ALL test asms)
│   │   ├── Rule.cs                              the [Rule("…")] attribute
│   │   ├── ParityGuard.cs                       parity per (assembly × type)
│   │   ├── RepoTree.cs                          MULTI-ROOT discovery: central tree + every src|tools **/Tests/
│   │   ├── TestTypes.cs                         catalog + Central{Arch,Structure,Docs} vs Feature{Unit,Wire,E2E,Live}
│   │   ├── Support/                             shared fixtures: FlowHarness, WebApp base, ScriptedProvider,
│   │   │                                        [LiveFact], the DocEngine shell-out helper
│   │   └── README.md                            the `harness:` front-matter target
│   │
│   ├── Templates/                               EVERY per-type template (test-template instances), one home
│   │   ├── arch.template.md      structure.template.md   docs.template.md   meta.template.md
│   │   └── unit.template.md      wire.template.md        e2e.template.md    live.template.md
│   │
│   ├── Tests/                                 → ABox.Tests.Central   (ownerless product types; no feature ref)
│   │   ├── Arch/       Rulebook/ rules.md   Tests/    reference-graph invariants (ArchUnitNET)
│   │   ├── Structure/  Rulebook/ rules.md   Tests/    placement invariants (filesystem scan)
│   │   └── Docs/       Rulebook/ rules.md   Tests/    catalog `check` + `validate` EVERY rulebook, in place
│   │       └── Support/ DocEngine.cs                  shells to the docengine CLI (ADR 0015)
│   │
│   └── Meta/                                  → ABox.Tests.Meta   (reflects over ALL *.Tests assemblies)
│       ├── Rulebook/ rules.md
│       └── Tests/  ParityTests.cs  TaxonomyTests.cs  CoverageTests.cs ← new: "every feature Tests/ has Rules"
│
└── src/
    ├── Features/
    │   ├── Tasks/                              ← illustrative shape (Tasks has no Tests/ yet; Flows below is live)
    │   │   ├── Create/  Module/  Contracts/      …feature code…
    │   │   └── Tests/                          → ABox.Tasks.Tests   (co-located, discovered by dirs.proj)
    │   │       ├── Unit/
    │   │       │   ├── Rulebook/ rules.md         a `rulebook` instance — THIS feature's guarantees;
    │   │       │   └── TaskCreateTests.cs          front-matter template→ tests/Templates/unit.template.md
    │   │       └── Wire/
    │   │           ├── Rulebook/ rules.md         parity is enforced centrally by Meta (CoverageTests +
    │   │           └── TasksEndpointTests.cs       the co-located taxonomy sweeps) — no per-feature Parity.cs
    │   │
    │   ├── Flows/
    │   │   ├── Start/ List/ Get/ Cancel/ Watch/ Catalog/ Module/ Shared/ Contracts/
    │   │   └── Tests/                          → ABox.Flows.Tests
    │   │       ├── Unit/   Rulebook/ rules.md   <…>Tests.cs
    │   │       ├── Wire/   Rulebook/ rules.md   <…>Tests.cs
    │   │       └── E2E/    Rulebook/ rules.md   FlowPingTests.cs   ← flow E2E lives HERE (Flows owns it)
    │   │
    │   ├── Git/        … └── Tests/ → ABox.Git.Tests
    │   ├── Projects/   … └── Tests/ → ABox.Projects.Tests
    │   ├── Inbox/      … └── Tests/ → ABox.Inbox.Tests
    │   └── Decisions/  … └── Tests/ → ABox.Decisions.Tests
    │
    ├── Domain/
    │   └── Agents/     … └── Tests/ → ABox.Agents.Tests   (owns Live: Tests/Live/ → real-CLI turns)
    ├── Infrastructure/ …
    └── Host/                                     owns ownerless infra endpoints
        └── Tests/ → ABox.Host.Tests              e.g. GET /health → Host/Tests/Wire/
```

The four rules that tree encodes:

| Question | Answer |
|---|---|
| **What's central?** | only ownerless guarantees: `Arch`, `Structure`, `Docs` (under `tests/Tests/`) + `Meta` (`tests/Meta/`) — plus the shared `tests/Harness/` and `tests/Templates/`. |
| **Where do a feature's tests go?** | `src/Features/<F>/Tests/`, types as **internal subfolders**, one assembly `ABox.<F>.Tests`. |
| **Standard vs. guarantees?** | doctype (catalog) + every `template.md` (`tests/Templates/`) = **central**; each feature's `rules.md` = **co-located**. |
| **Odd cases?** | flow E2E → **Flows**; live CLI → **Agents**; `/health` → **Host** (ownerless infra). |

**Assembly + namespace naming.** A feature or tool owns its assembly: `ABox.<Owner>.Tests` (`Tasks` →
`ABox.Tasks.Tests`; the hyphenated `doc-engine` → `ABox.DocEngine.Tests`), with namespaces
`ABox.<Owner>.Tests.<Type>`. The three **central** assemblies keep the `ABox.Tests.*` prefix —
`ABox.Tests.Harness`, `ABox.Tests.Central` (Arch + Structure + Docs), `ABox.Tests.Meta` — because the
test system itself, not any feature, owns them. So `ABox.Tests.*` = central; `ABox.<Owner>.Tests` = owned.

### The standard / criteria / guarantees split

| Layer | What it is | Doc-engine role | Home |
|---|---|---|---|
| **Doctype** (`rulebook`, `test-template`) | the schema — what *any* rulebook/template must look like | the catalog | **central** (`tools/doc-engine/doctypes/`) |
| **`<type>.template.md`** | the per-*type* criteria ("what a Unit test is") | a `test-template` **instance** | **central** (`tests/Templates/`), one per type |
| **`rules.md`** | this *feature's* guarantees | a `rulebook` **instance**, `template:`→central template | **co-located** with the feature |

The doc-engine validates every instance against its doctype **wherever it lives**; ParityGuard bridges
the `### ` headers in a co-located `rules.md` to the `[Rule]` tests beside it. Standard central, criteria
central-per-type, guarantees local.

---

## How the harness still enforces everything

Four engine changes turn the centralized harness into a **discover-and-enforce** harness. All four are in
`tests/Harness/**` + `tests/Meta/**` — the protected enforcement surface — so they land via an
owner-reviewed PR, carefully, *with* the move (never after).

| # | Current state (2026-06-26) | Becomes |
|---|---|---|
| 1 | `RepoTree.TestsRoot` = the single `tests/Tests/` dir; `RepoTree.RulebookFolders()` enumerates `tests/Tests/*/Rulebook` | **Multi-root discovery.** Enumerates the central tree **plus** every `src/**/Tests/` and `tools/**/Tests/`. This one method already feeds **two** consumers — `ParityGuard` *and* the `Docs` type's instance scan — so generalizing it co-locates both at once. |
| 2 | `TestTypes.Registered` = the eight type names; namespace `ABox.Tests.<Type>.Tests` | Catalog stays, **gains a classification**: `Central = {Arch, Structure, Docs}` vs `Feature = {Unit, Wire, E2E, Live}`. Namespace convention becomes per-assembly: `ABox.<Feature>.Tests.<Type>`. |
| 3 | `ParityGuard.For(asm, type)` scopes `[Rule]`s by namespace within one assembly | Unchanged mechanism — it already takes `(assembly, scope, rulesPath)`. Now called **once per (feature assembly × type-subfolder)** against that subfolder's co-located `rules.md`. The scoping that already stops Arch/Structure bleeding is exactly what keeps `Tasks.Unit` from bleeding into `Tasks.Wire`. |
| 4 | Meta reflects over the single `ABox.Tests` product assembly | Meta **discovers and loads every `*.Tests` assembly** and runs parity + taxonomy across all. **New load-bearing Rule:** *every feature `Tests/` folder has ≥1 paired Rule* — the backstop that stops a feature shipping untested once tests are no longer behind the central protected wall. |

> **There is no separate document-format guard to update** — the intra-document shape checks live in the
> doc-engine doctypes and are enforced by the `Docs` type. So the harness's only jobs are **parity**
> (test-side, `ParityGuard`) and **discovery** (`RepoTree`); document *shape* is the doc-engine's job,
> invoked through `Docs`. This is exactly the ADR-0015 split, and co-location doesn't disturb it.

> **ADR-0015 guard preserved:** *the Harness MUST NOT depend on the doc-engine.* Co-locating the
> doc-engine's own tests does **not** break this — `Harness` depends on nothing; `ABox.DocEngine.Tests`
> depends on `Harness` (arrow points out of the spine, the right way). Co-location is therefore an
> *upgrade* for the doc-engine: its internal unit tests join the real harness instead of only being
> reachable by the `Docs` shell-out.

---

## Zero hand-wiring (the hard constraint)

Adding a test, or a whole new feature's `Tests/` folder, must require **no manual wiring**. Two mechanisms
deliver that together:

1. **A traversal/glob build.** A traversal project (MSBuild `Microsoft.Build.Traversal`) globs
   `**/*.Tests.csproj` and is the `dotnet test` entry point — picking up a new `Tests/` folder
   automatically, so the suite stays **one command**. `ABox.slnx` remains the product/IDE solution
   (its `src/**` projects), but it is **no longer the test-discovery seam**: adding a feature's tests
   touches no central file — not `ABox.slnx`, not a harness registration.
2. **A scaffold skill** (`new-feature-tests`) stamps the `Tests/` folder, a ~5-line stub
   `ABox.<F>.Tests.csproj` (all real config inherited from `Directory.Build.props`) that stamps the
   `TestsSourceDir` metadata, and the per-type `rules.md` skeleton with correct front-matter
   (`docType: rulebook`, `template:`→`tests/Templates/<type>.template.md`, `harness:`→Harness README).
   No per-feature parity file is needed: Meta's `CoverageTests` drives `ParityGuard.ForColocated` over
   every discovered assembly × type, so a feature's parity is enforced centrally, not by local boilerplate.

So wiring *exists* but **no human types it** — the agent-first resolution. The only thing a human authors
is the test body and its `### ` Rule, which is the contract, not wiring.

**Assembly granularity = per feature/tool**, because that is the unit of co-location and isolation (a
feature's tests build and reference independently; the doc-engine keeps its YamlDotNet world to itself).
Assembly *count* scales with features, but the *cost* of a new one is paid by the scaffold, not by hand —
and Meta fails the build if a feature has a `Tests/` folder with no registered assembly.

---

## Governance remap

Co-location moves protected surfaces, so `governance/protected-paths` must follow:

| Path | Change |
|---|---|
| `tests/**/Rulebook/**` | narrow to the central rulebooks (`tests/Tests/{Arch,Structure,Docs}/Rulebook/**` + `tests/Meta/Rulebook/**`) |
| `tests/Templates/**` | **add** (critical) — the per-type criteria are the central standard |
| `src/**/Tests/Rulebook/**`, `tools/**/Tests/Rulebook/**` | **add** — feature/tool `rules.md` are guarantees, still protected |
| `tests/Harness/**`, `tests/Meta/**` | unchanged (still critical) |
| `tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` | confirm protected — the doctypes are the standard the whole repo validates against |
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
`src|tools **/Tests/`), feeding both `ParityGuard` and the `Docs` type's instance scan; add the
traversal/glob build; keep the current type-major tree in place and prove the new discovery finds exactly
today's tests + rulebook instances.
*Done when:* `dotnet test` over the traversal root runs the identical suite, green; `RepoTree` returns
the same (assembly, type, rulebook) set it does today; `Docs` validates the same instance set; build
warning-free.

**Phase 1 — Centralize templates; central assembly = ownerless only; classify the types.**
Move every per-type `template.md` into `tests/Templates/`; repoint each `rules.md` front-matter
`template:` at it. Reshape the central tree to `Arch` + `Structure` + `Docs` (+ `Meta`) — the
`ABox.Tests.Central` assembly. Add the `Central`/`Feature` classification to `TestTypes`. Behavioral
types (`Unit/E2E/Wire/Live`) are marked feature candidates but not yet moved.
*Done when:* every template resolves from `tests/Templates/`; `Docs` validates them in place;
`ABox.Tests.Central` references no production feature; Arch/Structure/Docs/Meta green; the classification
is asserted by a Meta test.

**Phase 2 — Move one feature, prove the pattern end-to-end.**
Pick one in-solution feature (e.g. `Tasks`). Create `src/Features/Tasks/Tests/` with the internal type
subfolders and its co-located `rules.md` per type (front-matter pointing at the central template); migrate
its existing Unit/Wire tests out of the central tree (git-rename, namespaces → `ABox.Tasks.Tests.<Type>`).
Parity is enforced centrally by Meta — no per-feature parity file.
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
*Done when:* protected paths match the new homes; adding a feature's tests via the skill needs zero manual
wiring and lands behind the right review wall.

---

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| The Harness→multi-assembly Meta refactor silently weakens parity (a feature slips in untested) | The coverage Rule (Phase 3) lands **with** the move and is proven red→green; parity is asserted per assembly, not globally |
| Assembly explosion (~25 csproj) raises maintenance | `Directory.Build.props` carries all config; each csproj is a scaffold-stamped ~5-line stub |
| Traversal/glob misses or double-counts an assembly | Meta cross-checks the discovered set against the traversal set and fails on mismatch |
| `Docs` (central) reaching into feature folders re-introduces a spine→engine coupling | It doesn't — `Docs` shells out to the CLI (ADR 0015); only the `Docs` Support helper touches the tool; `Harness` stays zero-dep. ADR 0015's deterministic (machine-checkable) confirmation already asserts `tests/Harness/**` references neither the doc-engine nor YamlDotNet |
| Per-type `template.md` drifts from the central doctype | Accepted, tracked cost in ADR 0015 ("a Meta test can later assert they agree"); one central copy per type means there's exactly one to reconcile |
| `GET /health`-style ownerless tests have no obvious home | Rule is explicit: central *only* if no feature owns it; default is the owning feature (Host) |

---

## Decisions taken

- **Ownership, not blast radius** — central is for ownerless guarantees (Arch/Structure/Docs/Meta); a flow
  E2E belongs to **Flows** and co-locates even though it drives other features.
- **Standard central, guarantees local** — the doc-engine **doctypes** and **every** per-type
  `template.md` (in `tests/Templates/`) stay central; each feature's `rules.md` co-locates, parity-bridged
  to its tests.
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

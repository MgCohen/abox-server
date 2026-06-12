# Test structure (server-only)

**Status:** 📋 planned — review before greenlight (no build without explicit go).

**Scope:** the test layout + workflow of this repo *after* the UI leaves and it becomes a
server-only orchestrator (Host + library driving `claude`/`codex` over ConPTY, API/SignalR out
to clients). Four moves:

1. **Strip the browser test luggage** (Playwright) — a server-only repo has no rendered surface.
2. **Name the test *types*** that today exist only implicitly (filename suffix + a hand-typed `Skip`).
3. **Make every type a *Rulebook*** — its guarantees written as natural-language Rules, parity-guarded
   against the tests that enforce them, exactly the way `tests/ArchTests` already guards `src/`.
4. **Make "API-down e2e" the backbone** — drive the real composition / HTTP surface, never a UI.

**Companion plans:** [`structure-guards.md`](structure-guards.md) and
[`structure-migration.md`](structure-migration.md) govern `src/` placement; this plan governs `tests/`.
The Rulebook engine this plan extracts is the same mechanism those rely on.

---

## Why Playwright goes (and what replaces it)

Playwright's entire value is a **rendered browser surface** — HTML to query, CSS to watch animate,
console errors to catch. A server-only orchestrator has none: the only thing with a DOM now lives in the
separate web repo (`C:\Unity\web`). `tools/frontend-verify/` (a `playwright-core` harness over system
Chrome/Edge) exists for exactly one job — *"a green build doesn't prove a Blazor UI renders; drive a real
browser to see it."* With no Blazor here, there is nothing to point it at. It was on loan for the Morph
spike phase; removing it **finishes the extraction**, it isn't a loss.

What a server-only repo verifies is browser-free and Playwright-irrelevant: ConPTY choreography,
anti-zombie teardown, subscription key-scrub, Claude JSONL parsing, Flow/Step orchestration, the CLI
subprocess lifecycle, the HTTP/SignalR wire. All of that is `dotnet test` with xUnit + local fakes.

> A cross-seam browser E2E (this server + a real browser client over SignalR) *could* exist, but it spans
> both repos, so its home is the web repo (which owns the UI) — never here, where there is no client to
> render. If a maintained browser suite is ever wanted there, the .NET-native choice is `Microsoft.Playwright`
> in C#, not a JS harness.

---

## The test types

Six types. Each is a folder under `tests/` and (this is the new part) each carries its own **Rulebook** —
the natural-language statement of what that type guarantees.

| Type | Guarantees (what its Rules say) | Drives | Gating |
|------|----------------------------------|--------|--------|
| **Arch** | dependency invariants — *"Domain must not depend on Features"* | ArchUnitNET over the loaded assemblies | always |
| **Structure** | placement invariants — *"every project lives under a home folder"* | filesystem scan of `src/` + `tests/` | always |
| **Unit** | expected results — *"Reverse of empty returns empty"*; also seam contracts (a Step / Feature module with fakes) | one type / one slice + local fakes | always |
| **E2E** | flow guarantees — *"claude-ping completes with PONG"* | real `Composition` from `FlowLauncher` down, scripted provider | always |
| **Wire** | endpoint contracts — *"`/health` returns ok; a flow streams snapshots"* | real HTTP via `WebApplicationFactory<Program>` | always |
| **Live** | real-CLI guarantees — *"claude edits the project on disk"* | the **real** `claude`/`codex` CLI + subscription | opt-in (`RUN_LIVE=1`), skipped in CI |

> **Unit absorbs "Component."** A separate seam-level type (one slice's public seam vs one type in
> isolation) was considered and folded in: the line is blurry in the existing tests and six types is
> already plenty. "Component" splits out later only if seam tests grow enough to earn their own Rulebook
> (promote on the second need).

**Arch** and **Structure** are today fused inside one `ArchTests` project (reference-graph rules over
loaded assemblies *and* a filesystem placement scan). Splitting them is part of this plan — two distinct
surfaces, two Rulebooks. **Unit/E2E/Wire/Live** today live flat in one `RemoteAgents.Tests` project,
separated only by filename.

**Priority order** = the order above is roughly fastest-and-most-fundamental first. A change ships when
**Unit + E2E + Wire** are green; **Live** is the manual confidence pass before a real run; **Arch +
Structure** gate every commit.

---

## Every type is a Rulebook

This is the organizing idea, and it generalizes what `ArchTests` already does to all six types.

**A *Rulebook*** is a natural-language file (`Rulebook/rules.md`) listing what a test type guarantees.
**A *Rule*** is one `### ` entry in it. What a Rule *means* varies by type — an Arch Rule is an
enforcement invariant, a Unit Rule is an expected result — but the **file shape, formatting, location, and
parity discipline are identical across every type.** Open any Rulebook and you learn the format from its
own header.

### Uniform file shape

Every Rulebook opens with the same self-teaching preamble — a quick explanation + the template for writing
one of *that type's* Rules — then the Rules themselves:

```markdown
# Unit Rulebook

Each Rule is one behavioral guarantee. Each `###` header IS the rule, stated as the guarantee
itself; the bullets carry its rationale. A `[Rule("<header>")]` test in Tests/ enforces it, and
ParityGuard fails the build if a Rule has no test or a test cites no Rule.

Template:
### <Subject> <condition> → <expected result>
- Why: <the contract this protects>

---

### Reverse of empty returns empty
- Why: boundary callers depend on the empty case not throwing.

### Reverse of a single element returns the same element
- Why: the identity case anchors the recursion.
```

An Arch Rulebook is the *same shape* with a different template (`### <subject> must not <forbidden>`) and
invariant-style Rules — which is exactly what `ArchTests/Fixtures/rules.md` already is.

### Adoption is staged (the model is universal; the authoring is not up-front)

The *model* applies to all six types from day one. The *authoring* does not:

- **Arch + Structure adopt fully now** — their Rules already exist (the current `rules.md`), so they ship
  complete Rulebooks immediately, strict 1:1.
- **Unit / E2E / Wire / Live adopt going-forward** — they get the folder shape now, but their Rulebook is
  populated as a **convention**: every *new* behavioral test lands with its Rule; existing tests are
  backfilled opportunistically, never in one swept pass. Retro-authoring a `###` line for every existing
  unit assertion is pure documentation at swept-codebase scale — not worth paying up front.

So a behavioral Rulebook starts small (or empty) and grows with the tests. ParityGuard still holds for
whatever Rules *are* declared — it just isn't fed the whole back catalogue on day one.

### ParityGuard — the shared engine

One small engine (today's `RuleBook.cs`, renamed) keeps each Rulebook honest:

- loads the `### ` headers from a Rulebook = the **declared** Rules;
- reflects the `[Rule("…")]` attributes in scope = the **enforced** Rules;
- fails the build on any mismatch.

Made reusable from a referenced library by two changes to the current hardcoded version: it takes the
**Rulebook path** and the **owning assembly** as parameters (the current code hardcodes `Fixtures/rules.md`
and `GetExecutingAssembly()`), and it scopes `[Rule]` discovery to the **type's namespace** so multiple
Rulebooks can live in one project without bleeding into each other's parity (Arch and Structure share an
assembly — their Rules must not count against each other).

```csharp
// One line in each type's Tests/, scoped to that type's namespace:
public class Parity
{
    [Fact] public void RulebookMatchesTests() =>
        ParityGuard.For(typeof(Parity)).Assert("Rulebook/rules.md");
}
```

### Cardinality differs by type (the one knob)

The parity *rule* is not identical for every type, because a Rule means different things:

- **Arch / Structure → strict 1 : 1.** One invariant, one sweeping assertion. A Rule tested twice, or a
  test with no Rule, is an error.
- **Unit / E2E / Wire / Live → 1 : N.** One guarantee may be realized by several case tests (e.g. a
  `[Theory]` with rows, or a cluster of `[Fact]`s). A *Rule* is a **behavioral contract**, not a
  micro-assertion.

So the universal parity contract is:

> **every Rule has ≥1 enforcing test; every `[Rule]` test cites a real Rule; no Rule is undocumented.**

…with **strict uniqueness (1:1)** as a per-type opt-in that Arch and Structure turn on and the behavioral
types leave off. This keeps the no-drift discipline everywhere without forcing a natural-language line per
assertion in high-volume unit suites.

---

## Layout

A deliberately **thin** shared Harness (engine + docs only) plus per-type folders that own everything
specific to themselves. Nothing is over-shared: a type's tooling stays with the type until a *second* type
genuinely needs it (the repo's "promote on the second use" rule).

```
tests/
  Harness/                  → RemoteAgents.Tests.Harness   (thin: shared engine + the convention docs)
    Rule.cs                   the [Rule("…")] attribute
    ParityGuard.cs            For(type).Assert(rulebookPath) — load + reflect + parity, namespace-scoped
    README.md                 how a Rulebook-governed type works (the test-wide convention)

  Arch/                     reference-graph invariants
    Rulebook/  rules.md
    Tests/
    Support/                  ArchitectureModel (bands + layer allow-graph)
  Structure/                filesystem placement invariants
    Rulebook/  rules.md
    Tests/
    Support/                  SourceTree (project scan) + the repo-root locator

  Unit/  E2E/  Wire/  Live/
    Rulebook/  rules.md       the type's guarantees (grows going-forward), 1:N cardinality
    Tests/
    Support/                  optional, type-local — ScriptedProvider, FlowHarness, [LiveFact], doubles
```

**Per-type roles, uniform everywhere:** `Rulebook/` (the Rules) · `Tests/` (the enforcing facts) ·
`Support/` (optional, type-local tooling/doubles).

### Renames (settle the vocabulary)

| Today | Becomes | Why |
|-------|---------|-----|
| `Fixtures/rules.md` | `Rulebook/rules.md` | "Fixtures" collides with the testing sense (doubles/setup); "Rulebook" is the prose, per type |
| `RuleBook.cs` (engine) | `ParityGuard.cs` | the word *Rulebook* now names the file, so the engine gets its own name |
| `RuleAttribute` | `Rule` (the `[Rule]` attribute) | unchanged in behavior; reads as "this fact enforces a Rule" |
| `ArchTests/Support/` (engine + model) | engine → `Harness/`; model (`ArchitectureModel`/`SourceTree`) stays in each type's `Support/` | engine is shared; the model is type-local tooling. `Support/` is kept as the per-type tooling-folder name |

> Side evidence the naming wanted settling: `RuleAttribute.cs` already refers to `Rules/rules.md` while the
> file actually lives at `Fixtures/rules.md` — the name had drifted once already.

### Namespace = folder, enforced in `tests/` too

`src/` already enforces *namespace mirrors folder* at compile time via the **IDE0130** analyzer
(`/.editorconfig`). This plan **extends that guard to `tests/`**, so a test file whose namespace doesn't
match its type folder is a build error — the type-folder taxonomy can't silently drift:

```csharp
// File: tests/Unit/Tests/QuestionParserTests.cs
namespace RemoteAgents.Tests.E2E;   // ← IDE0130 build error: namespace ≠ folder
```

The one-time namespace sweep to align existing files folds into the Phase 2 move.

### Projects (assembly walls only where references diverge)

Per the repo's R-ARCH rule — *an assembly boundary exists only where it earns enforcement or reuse* —
group by **reference profile**, not one-project-per-type:

- **`RemoteAgents.Tests.Harness`** — the shared engine + docs. Referenced by all.
- **`RemoteAgents.Tests.Structure`** — Arch + Structure types. Profile: load-assemblies-from-disk +
  filesystem scan (ArchUnitNET), *no* production project reference. Two type-folders, two Rulebooks,
  namespace-scoped parity keeps them separate.
- **`RemoteAgents.Tests`** — Unit + E2E + Wire + Live. Profile: the spine + `Microsoft.AspNetCore.App`.
  Four type-folders, filtered by `[Trait("type", …)]`. They share references, so a wall between them earns
  nothing.

Net ≈ **3 projects**, not six.

---

## How we test now: the API-down e2e backbone

The "consumer" of a server-only repo is **an HTTP/SignalR client, not a page**, so e2e drives the API at
two depths — never a rendered UI.

**1. In-process E2E (the default, deterministic).** Boot the real composition, swap in a scripted provider,
drive a flow end to end. This is today's `LiveSmoke.RunAsync` generalized so the provider is injectable:

```csharp
// FlowHarness — boots the real Composition, lets a test override the provider seam.
var snap = await FlowHarness.RunAsync(
    flowId: "claude-ping",
    prompt: "…",
    provider: new ScriptedProvider("ask: secret?", "PONG"),   // no live CLI
    timeout: TimeSpan.FromSeconds(5));

Assert.Equal(FlowPhase.Done, snap.Phase);
Assert.Contains("PONG", snap.Operations.Single().Summary);
```

Real Steps, real Flow engine, real snapshot stream, real resolver wiring — only the agent's *mouth* (the
provider) is scripted. Fast, deterministic, CI-safe, exercises the whole spine below the HTTP layer.

> **Naming fix:** `LiveSmoke` is a misnomer — it boots in-process and is *not* live. Rename to `FlowHarness`
> (lives in `E2E/Support/`); reserve the word **Live** for the real-CLI type only.

**2. Wire smoke (the thin top).** Prove routing + serialization + the flow-stream contract with a real
`HttpClient` against `WebApplicationFactory<Program>` — `/health` returns ok, `/projects` lists, `MapFlows`
starts a flow and streams snapshots, scripted provider behind it. Keep it **thin**: it tests the wire, not
the spine (the spine is covered by in-process E2E).

### Live gating — a real gate, not a code edit

Today the live-CLI tests are disabled by a hand-typed `[Fact(Skip="…")]` const — you edit source to run
them. Replace it with a `[LiveFact]` attribute that auto-skips unless `RUN_LIVE=1` (and/or the CLI is on
`PATH`). Live tests then run by setting an env var, never by editing code, and CI behavior is explicit.

---

## Non-goals (decided)

- **No fake UI / no browser, here.** A server-only repo has no page to render; simulating one would re-test
  the web repo's job. The consumer we stand in for is an HTTP/SignalR **client**, driven directly with
  `HttpClient` / a SignalR client — never a rendered fake page.
- **No Playwright in any flavor** (`core`, full runner, MCP, .NET binding) — nothing here has a browser
  surface for it to act on.
- **No one-project-per-type.** Assembly walls only where reference profiles diverge (≈ 3 projects).
- **No over-shared test tooling.** The Harness holds *only* the Rulebook engine + docs; doubles, models, and
  harnesses stay in each type's `Support/` until a second type genuinely reuses them.
- **No up-front behavioral Rulebook backfill.** Behavioral Rules accrue going-forward, not in one sweep.

---

## Build order (done-when gates)

**Phase 0 — Land the extraction, evict the UI test luggage**

The deletions this phase describes **already exist as commits, unmerged, on two sibling branches** — so the
real task is consolidation, not re-deleting:

- `claude/jovial-sammet-30a622` — extracts `src/Morph` (`be7c614`) + `src/RemoteAgents.Web` (`bf705fb`).
- `claude/strange-jang-eaa3ec` — removes `tools/frontend-verify` (`8c9fa32`).
- Neither is on `main`; `main` still carries the full UI. **Build on post-extraction `main`, not on the
  stale `a93a6b3` worktree** (which predates all of it).

Then, on that consolidated base:
- Confirm `tools/frontend-verify/` (harness + probes) and `tests/Morph.Tests/` are gone; drop `Morph.Tests`
  from `RemoteAgents.slnx`.
- Remove the **"Verifying the frontend"** section from `CLAUDE.md`.
- Update memory: retire the `playwright-verification` pointer + the morph-spike pointers that name
  `frontend-verify`.
- *Done when:* no `playwright`/`frontend-verify` reference remains in `CLAUDE.md` or `tests/`; `dotnet build
  RemoteAgents.slnx` green; the `PendingEvictionFolders` staleness check drives the `src/Morph` +
  `src/RemoteAgents.Web` drop (tracked by `structure-guards`).

**Phase 1 — Extract the Harness + rename** *(architecture-defining infrastructure; earns its place now)*
- Create `RemoteAgents.Tests.Harness`: move the `[Rule]` attribute + the parity engine out of `ArchTests`.
- Rename `RuleBook.cs` → `ParityGuard.cs`; parameterize it by **(Rulebook path, owning assembly,
  type namespace)** so it works from a referenced library and scopes `[Rule]`s per type.
- Move `ArchTests/README.md` → `Harness/README.md` as the test-wide convention doc.
- *Done when:* `ArchTests` references the Harness and its parity still passes; build warning-free.

**Phase 2 — Split Arch / Structure, adopt the per-type folders**
- Split `ArchTests` into the `Structure` project's two type-folders: `Arch/` (reference graph,
  `Support/ArchitectureModel`) and `Structure/` (filesystem, `Support/SourceTree` + repo-root locator), each
  with its own `Rulebook/rules.md` and strict 1:1 parity.
- Reshape `RemoteAgents.Tests` into `Unit/ E2E/ Wire/ Live/`, each with `Tests/` + optional `Support/`, and
  a `Rulebook/` that starts small (behavioral Rules accrue going-forward, not backfilled). Doubles move into
  the consuming type's `Support/`.
- Extend IDE0130 to `tests/`; one-time namespace sweep so every test file's namespace mirrors its type folder.
- *Done when:* every test sits under a type folder; namespace = folder holds (build errors otherwise); Arch +
  Structure Rulebook parity is green.

**Phase 3 — Generalize the e2e harness**
- Rename `LiveSmoke` → `FlowHarness` (in `E2E/Support/`); add an injectable `provider` parameter (defaults to
  the live composition provider so existing live tests are unchanged).
- *Done when:* one in-process E2E test drives a flow end to end with `ScriptedProvider`, deterministic, green
  in CI; live tests still compile against the renamed harness.

**Phase 4 — Live gating**
- Add `[LiveFact]` (skips unless `RUN_LIVE=1`); convert the `Claude`/`Codex`/`Interactivity` live tests off
  the `Skip` const into `Live/`.
- *Done when:* default `dotnet test` skips all live tests with a clear reason; `RUN_LIVE=1 dotnet test` runs
  them; no `Skip="…"` const remains.

**Phase 5 — Wire smoke**
- Add the `Wire/` type: `WebApplicationFactory<Program>` + `HttpClient` over `/health`, `/projects`, and one
  `MapFlows` start-and-stream, scripted provider behind it.
- *Done when:* the wire Rules each have a passing test, deterministic in CI without a live CLI.

---

## Decisions taken

- **Six types:** Arch · Structure · Unit · E2E · Wire · Live. "Component" folded into Unit (splits out later
  only if seam tests earn it).
- **Every test type is a Rulebook** — uniform file shape (preamble + template + `###` Rules), parity-guarded,
  same place; the *meaning* of a Rule varies by type, the *format* does not.
- **Staged adoption** — Arch + Structure ship full Rulebooks now; behavioral Rulebooks accrue going-forward,
  no up-front backfill.
- **Cardinality:** a Rule is a behavioral contract — parity is *"every Rule has ≥1 test, no undocumented
  tests"*; strict 1:1 is a per-type opt-in (Arch + Structure on, behavioral off).
- **Thin Harness, type-local Support** — share only the engine + docs; no over-sharing.
- **Renames:** `Rulebook`/`Rule`/`ParityGuard`; `Fixtures→Rulebook`; `Support/` kept as the per-type
  tooling-folder name.
- **Namespace = folder enforced in `tests/`** — IDE0130 extended; one-time sweep in Phase 2.
- **Split Arch from Structure** — reference graph vs filesystem placement, two Rulebooks.
- **API-down e2e backbone** — in-process default + thin wire smoke; no fake UI; `LiveSmoke→FlowHarness`.
- **Live env-gated** via `[LiveFact]` / `RUN_LIVE=1`.
- **≈3 projects** — Harness, Structure (Arch+Structure), RemoteAgents.Tests (Unit+E2E+Wire+Live).
- **Base on post-extraction `main`** — Phase 0's deletions live on `jovial-sammet` + `strange-jang`;
  consolidate them first rather than re-deleting, and don't build on the stale `a93a6b3` worktree.

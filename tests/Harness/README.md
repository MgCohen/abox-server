# Test Harness — the Rulebook convention

The shared engine every test *type* in this repo is built on. It holds **only** two things — the `[Rule]`
attribute and the `ParityGuard` engine — plus this doc. Nothing type-specific lives here; a type's models,
doubles, and harnesses stay in that type's own `Support/` until a *second* type genuinely reuses them.

## Every test type is a Rulebook

A **Rulebook** (`<Type>/Rulebook/rules.md`) is the natural-language statement of what one test type
guarantees. A **Rule** is one `### ` header in it. What a Rule *means* varies by type —

- **Arch** Rule = a dependency invariant: *"Dependencies flow down the layer graph only"*
- **Structure** Rule = a placement invariant: *"Every project lives under an agreed home folder"*
- **Unit** Rule = an expected result: *"Reverse of empty returns empty"*
- **E2E / Wire / Live** Rule = a flow / endpoint / real-CLI guarantee

— but the **file shape, location, and parity discipline are identical across every type.** Learn the format
from any Rulebook's own header; write the next Rule from its template.

## The two pieces

- **`Rule.cs`** — `[Rule("<header>")]`, an xUnit `[Fact]` that also names the Rulebook header it enforces.
  A test can't enforce a Rule without citing it. (A guarantee realized by several cases is several
  `[Rule("<same header>")]` methods — see cardinality below.)
- **`ParityGuard.cs`** — keeps a Rulebook and its tests in lockstep. Each type drops in one parity fact:

  ```csharp
  public class Parity
  {
      [Fact] public void RulebookMatchesTests() =>
          ParityGuard.For(typeof(Parity)).Assert("Arch/Rulebook/rules.md");
  }
  ```

  `For(anchor)` scopes `[Rule]` discovery to the anchor type's **namespace**, so multiple Rulebooks coexist
  in one assembly without counting against each other (Arch and Structure share a project — their Rules must
  not bleed). `Assert(path)` loads the `### ` headers from the copied Rulebook and compares them to the
  `[Rule]`s in scope, failing the build on any mismatch.

## Stability contract — a Rulebook is a ratchet

Treat Rules as a one-way ratchet, and treat *this convention itself* as load-bearing. The two are
different risk levels:

- **Adding a Rule — safe, encouraged.** A new `### ` header + its `[Rule]` test only *tightens* the
  guarantees. This is the everyday move; do it freely whenever a new invariant or guarantee is worth
  pinning.
- **Editing, removing, or re-wording an existing Rule — dangerous.** Each Rule encodes a hard-won
  invariant some past failure paid for. Removing one silently drops a guarantee the codebase still
  relies on; re-wording one can quietly narrow it. Parity will force you to keep the test's `[Rule("…")]`
  string in lockstep with the header, but it **cannot** tell you the *guarantee* got weaker. So a change
  here is a **design decision**, not a cleanup: justify why the invariant no longer holds (or moved),
  the same bar as changing the thing the Rule protects. When in doubt, ask — don't quietly edit.
- **Changing the shape / template / format — most dangerous, and rarely warranted.** The `### `-heading
  scan, the fenced-block skip, the namespace-scoped discovery, the strict-vs-1:N cardinality, the
  `Rulebook/` + `Tests/` + `Support/` layout, the csproj copy glob — these are the engine's load-bearing
  assumptions, shared by **every** type at once. Reshaping the template or the parsing rules can make
  Rules silently stop being counted (enforcement evaporates with a *green* build) across the whole repo.
  Don't refactor the format casually; a change here is an architecture change to the test system, with the
  burden of proof to match.

The summary: **add Rules liberally; change or remove them deliberately; reshape the convention almost
never.**

## Cardinality (the one knob)

`ParityGuard.For(anchor)` defaults to **1:N** and `For(anchor, strict: true)` is **1:1**:

- **Arch / Structure → `strict: true`.** One invariant, one sweeping assertion. A Rule tested twice, or a
  test with no Rule, is an error.
- **Unit / E2E / Wire / Live → default (1:N).** One guarantee may be realized by several case tests. The
  contract is *every Rule has ≥1 test; every `[Rule]` test cites a real Rule; no Rule is undocumented* —
  duplicates allowed.

## Adoption is staged

The *model* applies to all types from day one; the *authoring* does not. Arch + Structure ship complete
Rulebooks (their Rules already exist). The behavioral types (Unit / E2E / Wire / Live) get the folder shape
now and accrue Rules **going-forward** — every new behavioral test lands with its Rule; existing tests are
backfilled opportunistically, never in one swept pass. A behavioral Rulebook starts small and grows.

## The uniform per-type layout

```
<Type>/
  Rulebook/  rules.md   the Rules — opens with a self-teaching preamble + this type's Rule template
  Tests/                the [Rule]-tagged facts that enforce them + one Parity fact
  Support/              optional, type-local: models, doubles, harnesses (no over-sharing)
```

`Rulebook/rules.md` must be copied to the output directory so `ParityGuard` can read it at runtime — each
type's csproj does this with a `None Include="**\Rulebook\*.md" CopyToOutputDirectory="PreserveNewest"`.
Namespace mirrors folder, enforced at compile time by IDE0130 (`/.editorconfig`, scoped to `tests/`), so the
type-folder taxonomy can't silently drift.

## Standing up a new test *type* (a new Rulebook)

A new type is rarer and weightier than a new Rule — it's a new *kind* of guarantee, so add one only when an
existing type genuinely can't host it (don't fork Unit into near-twins). Adding a Rule to an existing type
is almost always the right move instead. When a new type really is warranted, define its Rulebook by
following any existing one as the worked example — the shape is uniform on purpose:

1. **Create `tests/Tests/<Type>/`** with the three sub-folders: `Rulebook/`, `Tests/`, and (if needed)
   `Support/`. Namespace mirrors folder (`RemoteAgents.Tests.<Type>…`); IDE0130 enforces it.
2. **Write `<Type>/Rulebook/rules.md`** — copy the preamble + Rule template from a sibling Rulebook and adapt
   the one-line description of *what a Rule means for this type*. Don't invent a new template shape (see the
   stability contract); the template is shared structure, not per-type creativity.
3. **Add the Parity fact** in `<Type>/Tests/`: a single `[Fact]` calling
   `ParityGuard.For(typeof(Parity)[, strict: true]).Assert("<Type>/Rulebook/rules.md")`. Choose strictness
   deliberately — **1:1 (`strict: true`)** for invariant types where one Rule is one assertion (like Arch /
   Structure), **1:N (default)** for behavioral types where a guarantee may have several case tests.
4. **Write at least one `### ` Rule + its `[Rule("<header>")]` fact** so the type isn't an empty shell
   (an Arch/Structure-style type ships complete; a behavioral type may start with one Rule and grow — see
   *Adoption is staged*).
5. **No csproj edit is needed** — `Tests.csproj` already globs `**\Rulebook\*.md` to the output and compiles
   every `.cs` under the type. Just rebuild; the Parity fact proves the new Rulebook is wired correctly.

If the new type lives in the merged `RemoteAgents.Tests` assembly, its `Parity` anchor's namespace keeps its
Rules from bleeding into another type's parity — that's why each type carries its own `Parity` fact.

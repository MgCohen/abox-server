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

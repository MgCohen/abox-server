---
name: new-feature-tests
description: >-
  How to stand up a feature/tool's co-located test assembly (ABox.<Owner>.Tests) in this repo —
  its Tests/ folder, the thin csproj stub, and per-type Rulebooks. Use when a feature under
  src/ (or a tool under tools/) gains its first test, adds a new test type (Unit/Wire/E2E/Live),
  or when ABox.<Owner>.Tests needs creating. Tests live with their owner; one central harness still
  enforces rubrics + parity. See PLANS/test-colocation.md.
---

# A feature owns its tests — stand them up with zero central wiring

Tests live **with the thing they guarantee**: a feature's tests inside the feature folder, owned by an
`ABox.<Owner>.Tests` assembly, glob-discovered by `dirs.proj` and policed by the harness's own tests. No central
file is edited to add a feature's tests — not `ABox.slnx`, not a harness registration. You author the test
body and its `### ` Rule (the contract); everything else is the stamp below.

Read first: [`PLANS/test-colocation.md`](../../../PLANS/test-colocation.md) (the model),
[`tests/Harness/README.md`](../../../tests/Harness/README.md) (the Rulebook convention), and the
**test-rulebook** skill (what a Rule is, how parity works). This skill is the *placement procedure*.

## The ownership rule

- **Central** (`tests/`) is for **feature-independent** guarantees only: `Arch`, `Structure`, `Docs`,
  plus the shared `Harness` engine (with its own tests at `Harness/Tests/`), the per-type `Rubrics/`, and
  `Fixtures/` (generic engine helpers like `Op`).
- **A feature owns** its `Unit`/`Wire`/`E2E`/`Live` tests **and its fixtures** (fakes, harnesses). A
  cross-cutting case lives with the feature that owns *the case*, not central — "touches many features" never
  promotes a test to central.
- A test needing another feature's fixture either re-homes to that owner, or references the owner's
  `ABox.<Owner>.Tests`. Generic engine scaffolds (`Op`/`OpFlow`) come from `ABox.Tests.Fixtures`.

## The shape

```
src/<…>/<Owner>/Tests/                 → ABox.<Owner>.Tests   (Domain owner → src/Domain/<Owner>/Tests)
├── ABox.<Owner>.Tests.csproj           the thin stub (below)
├── Support/                            this feature's fixtures (only if it has any)
└── <Type>/                             one folder per type: Unit | Wire | E2E | Live
    ├── Rulebook.md              a `rulebook` instance — THIS feature's guarantees
    └── <Name>Tests.cs                  the [Rule]-cited facts, namespace ABox.<Owner>.Tests.<Type>
```

## The stamp

**1. `ABox.<Owner>.Tests.csproj`** — a thin stub; `TestProject.props` carries the shared config, the build
stamps `TestsSourceDir` so co-located parity finds its Rulebook. Depth of `..\` to `tests/` depends on the
folder (`src/Features/<F>/Tests` → `..\..\..\..`; `src/Domain/<F>/Tests` → `..\..\..\..`; `src/Host/Tests` →
`..\..\..`). Reference only the production assemblies the tests exercise.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\..\..\..\tests\TestProject.props" />
  <PropertyGroup>
    <RootNamespace>ABox.<Owner>.Tests</RootNamespace>
    <AssemblyName>ABox.<Owner>.Tests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyMetadata Include="TestsSourceDir" Value="$(MSBuildProjectDirectory)" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\tests\Harness\ABox.Tests.Harness.csproj" />
    <!-- + the production csproj(s) under test, and ..\..\..\..\tests\Fixtures\ABox.Tests.Fixtures.csproj if you use Op -->
  </ItemGroup>
</Project>
```

The feature needs **no parity file**: the harness's own `CoverageTests` runs `ParityGuard.For` over every
co-located suite × type it discovers (`Suites.Colocated()`), so parity is driven centrally — `ParityGuard` lives
in the engine assembly the feature never references. You stamp the csproj, the Rulebook, and the tests; the
harness does the rest.

**2. `<Type>/Rulebook.md`** — a `rulebook` instance pointing at the central rubric. The `../` depth
to `tests/` matches the csproj's; for `src/Features/<F>/Tests/<Type>/Rulebook` it is six levels:

```markdown
---
docType: rulebook
testType: unit
rubric: ../../../../../../tests/Rubrics/Unit.md
harness: ../../../../../../tests/Harness/README.md
---

## Rules

### <Subject> <does/does not> <…>
- **Why:** <the invariant this guarantees>
```

**3. The test** — `namespace ABox.<Owner>.Tests.<Type>;`, each fact `[Rule("<exact header>")]` + `[Fact]`.
Shared fixtures come via a per-csproj `<Using Include="ABox.<Owner>.Tests.Support" />` (and
`ABox.Tests.Fixtures` for `Op`).

## Gotchas these migrations hit

- **Namespace shadowing.** `ABox.<Owner>.Tests` makes `ABox.<Owner>` an ancestor namespace, which shadows a
  same-named domain type (`Inbox`, `Decisions`, `Git`, the `Agents` static class). Alias it:
  `<Using Include="ABox.Domain.<Owner>.<Type>" Alias="Domain<Owner>" />` (global) or a per-file
  `using DomainX = …;`, and use the alias at the constructor/return site.
- **Host internals.** A suite touching Host internals (Composition/ClaudeBox/Program for Wire) needs an
  `InternalsVisibleTo` entry in `src/Host/ABox.Host.csproj`.
- **Production must skip Tests/.** Handled once in `Directory.Build.props` (production projects under src/ that
  aren't `*.Tests` exclude `Tests\**`) — no per-feature action.

## Verify

`dotnet build dirs.proj -c Release` then `dotnet test dirs.proj -c Release` — the traversal discovers the new
assembly, the harness's `CoverageTests` and parity both go green, and `Docs` validates the new `Rulebook.md`
in place. No `ABox.slnx` or harness edit. If `CoverageTests` reports the folder "ships tests but no assembly",
the csproj name/`TestsSourceDir` is wrong.

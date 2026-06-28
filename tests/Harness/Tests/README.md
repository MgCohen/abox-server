# The harness's own tests

`ABox.Tests.Harness.Tests` — the tests that make sure **every other suite follows the harness contract**: each
test cites a `[Rule]`, sits where `ParityGuard` can see it, and is wired into coverage. They guard the **test
system itself**, not the product.

They sit beside the engine they police — `tests/Harness/` holds the engine (`Rule`, `ParityGuard`, `TestTypes`,
`RepoTree`, `TestMarkers`), `tests/Harness/Tests/` holds the tests of it — the same code-plus-`Tests/` shape a
feature has. It is its own assembly (the engine is a library every suite references; these reflect over the
suites from *outside*), validating every suite the way the Arch guards validate `src/`: it reflects over the
central assembly (through `ABox.Tests.SuiteAnchor`) and over every co-located `ABox.<Owner>.Tests` (discovered
from the build output by `Suites.Colocated()`), and reads the Rulebooks straight from the source tree
(`RepoTree`). Living apart is the point — the validator isn't inside the bag it checks.

**These tests are the enforcer, not part of the product taxonomy they enforce** — `Harness` is not a registered
type and their self-Rulebook is plain markdown, not a doc-engine instance. But they **do** eat their own dog
food: each cites a `[Rule]` in `Rulebook.md` beside them, parity-checked over their own namespace by
`ParityGuard.ForRulebook` — the enforcer held to the bar it enforces. (The protected-path gate, separately,
stops a guard being quietly deleted.) They are xUnit tests, run by `dirs.proj`, documented by this table.
"A test" below means any method carrying a marker in `TestMarkers.Markers` (today `FactAttribute` and everything
assignable to it — Fact, Theory, LiveFact), not `[Fact]` specifically.

| File | What it holds |
|---|---|
| `ParityTests.cs` | For each central type **and the harness's own tests**: every `### ` Rulebook header has a test citing it, and every cited test names a real header. |
| `TaxonomyTests.cs` | Every folder under `tests/Tests/` is a known type (or `Support`) — no stray folder. |
| `TaxonomyTests.cs` | Every marked test in the **central** assembly sits under `ABox.Tests.<Type>`. |
| `TaxonomyTests.cs` | Every marked test in a **feature** assembly sits under `ABox.<Owner>.Tests.<Type>`, never the assembly root. |
| `TaxonomyTests.cs` | Every folder in a feature's `Tests/` is a known type **with** a `Rulebook.md` (or `Support`) — none silently skipped by coverage parity. |
| `TaxonomyTests.cs` | Every rulebook's `testType` front-matter equals its folder — the harness owns the type set, the doc-engine no longer pins it to a list. |
| `CoverageTests.cs` | Every co-located `Tests/` folder on disk maps to a built `ABox.<Owner>.Tests`, and parity runs over each (assembly × type) — the backstop that replaces the central protected wall once a feature's tests live beside the feature. |

`Suites.cs` here is **not** a test — it discovers the co-located assemblies from the build output for the
guards above.

Two related guards live **elsewhere**, by design:

- **Rulebook *format*** (a `Rulebook.md` / rubric is well-formed) is enforced by the **`Docs`** type, which
  shells out to the doc-engine (ADR 0015) — not here. These tests own the Rule↔test correspondence; the
  doc-engine owns intra-document shape.
- **The harness takes no dependency on the engine it shells out to** (ADR 0015 `[det]`) is a csproj/disk check,
  so it lives with the **`Structure`** type (`The test harness depends on nothing it shells out to`).

# Meta — the test-system self-suite

A separate assembly (`ABox.Tests.Meta`) whose Rules guard the **test system itself**, not the product. Same
Rulebook shape as every product type (`Rulebook/`, `Tests/`) — see [`../README.md`](../README.md)
for the convention.

It **nests under `Harness/`** because it is how the Harness enforces itself: every guard below checks that a
test actually follows the Harness contract — cites a `[Rule]`, sits where `ParityGuard` can see it, is wired
into coverage. It is its own assembly, not part of the engine (the engine is a library every suite references;
this reflects over them from outside) — co-located with the engine it polices, the way a feature's tests sit
with the feature.

It validates every suite from **outside**, the way the Arch guards validate `src/`: it reflects over the
central assembly (through `ABox.Tests.SuiteAnchor`) and over every co-located `ABox.<Owner>.Tests` (discovered
from the build output by `Suites.Colocated()`), and reads the Rulebooks straight from the source tree
(`RepoTree`). Living apart is the point — the validator isn't inside the bag it checks.

Its guards — each row is one `[Rule]` under `Tests/`. "A test" means any method carrying a marker in
`TestMarkers.Markers` (today `FactAttribute` and everything assignable to it — Fact, Theory, LiveFact), not
`[Fact]` specifically:

| File | Rule | What it holds |
|---|---|---|
| `ParityTests.cs` | *Parity holds for every registered type* | For each central type **and Meta itself**: every `### ` Rulebook header has a test citing it, and every cited test names a real header. |
| `TaxonomyTests.cs` | *Every folder under tests holds a registered test type* | Every folder under `tests/Tests/` is a known type (or `Support`) — no stray folder. |
| `TaxonomyTests.cs` | *Every test lives inside a registered test type* | Every marked test in the **central** assembly sits under `ABox.Tests.<Type>.Tests`. |
| `TaxonomyTests.cs` | *Every co-located test lives inside a registered feature type* | Every marked test in a **feature** assembly sits under `ABox.<Owner>.Tests.<Type>`, never the assembly root. |
| `TaxonomyTests.cs` | *Every co-located type folder is a feature type carrying a Rulebook* | Every folder in a feature's `Tests/` is a known type **with** a Rulebook (or `Support`) — none silently skipped by coverage parity. |
| `TaxonomyTests.cs` | *Central and Feature types partition the registered types* | The `Central` and `Feature` lists are disjoint and together cover every registered type. |
| `CoverageTests.cs` | *Every co-located feature Tests folder is policed by a built assembly* | Every `Tests/` folder on disk maps to a built `ABox.<Owner>.Tests`, and parity runs over each (assembly × type) — the backstop that replaces the central protected wall once a feature's tests live beside the feature. |

`Suites.cs` here is **not** a test — it discovers the co-located assemblies from the build output for the
guards above.

Two related guards live **elsewhere**, by design:

- **Rulebook *format*** (a `rules.md`/`template.md` is well-formed) is enforced by the **`Docs`** type, which
  shells out to the doc-engine (ADR 0015) — not by Meta. Meta owns the Rule↔test correspondence; the doc-engine
  owns intra-document shape.
- **The harness takes no dependency on the engine it shells out to** (ADR 0015 `[det]`) is a csproj/disk check,
  so it lives with the **`Structure`** type (`The test harness depends on nothing it shells out to`).

`Meta` is deliberately **not** in `TestTypes.Registered`: that list is the product taxonomy it checks, and a
validator doesn't enrol itself in the set it validates.

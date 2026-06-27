# Meta — the test-system self-suite

A separate assembly (`ABox.Tests.Meta`) whose Rules guard the **test system itself**, not the product. Same
Rulebook shape as every product type (`Rulebook/`, `Tests/`) — see [`../Harness/README.md`](../Harness/README.md)
for the convention.

It validates every suite from **outside**, the way the Arch guards validate `src/`: it reflects over the
central assembly (through `ABox.Tests.SuiteAnchor`) and over every co-located `ABox.<Owner>.Tests` (discovered
from the build output by `Suites.Colocated()`), and reads the Rulebooks straight from the source tree
(`RepoTree`). Living apart is the point — the validator isn't inside the bag it checks.

Its four guards:

- **Parity** — every central type's Rulebook headers and its `[Rule]`-cited tests stay in lockstep, then Meta
  holds itself to the same bar.
- **Coverage** — every co-located feature `Tests/` folder maps to a built `ABox.<Owner>.Tests` assembly, and
  `ParityGuard.ForColocated` runs the same parity over each (assembly × type). This is the backstop that
  replaces the central protected wall once a feature's tests live beside the feature.
- **Taxonomy** — every folder under `tests/Tests/` is a registered type; every central test lives inside one;
  and every co-located test lives under `ABox.<Owner>.Tests.<Type>` (not the assembly root) with each feature
  type folder carrying a Rulebook — so none escapes a parity scope.
- **Rulebook format** — every Rule matches its type's `template.md`, each `rules.md` holds nothing but its
  Template/Harness pointers and Rules, and every `template.md` carries a `## Criteria` rubric for the judge.

`Meta` is deliberately **not** in `TestTypes.Registered`: that list is the product taxonomy it checks, and a
validator doesn't enrol itself in the set it validates.

# Meta — the test-system self-suite

A separate assembly (`ABox.Tests.Meta`) whose Rules guard the **test system itself**, not the product. Same
Rulebook shape as every product type — its definition in `governance/registry/Test/Meta/`, its tests in `Tests/` — see [`../Harness/README.md`](../Harness/README.md)
for the convention.

It validates the product suite from **outside**, the way the Arch guards validate `src/`: it reflects over
`ABox.Tests` (through `ABox.Tests.SuiteAnchor`) and reads the Rulebooks straight from the source tree
(`RepoTree`). Living apart is the point — the validator isn't inside the bag it checks.

Its three guards:

- **Parity** — every product type's Rulebook headers and its `[Rule]`-cited tests stay in lockstep, then Meta
  holds itself to the same bar.
- **Taxonomy** — every folder under `tests/Tests/` is a registered type, and every product test lives inside
  one (so none escapes a parity scope).
- **Rulebook format** — every Rule matches its type's `template.md`, each `rules.md` holds nothing but its
  Template/Harness pointers and Rules, and every `template.md` carries a `## Criteria` rubric for the judge.

`Meta` is deliberately **not** in `TestTypes.Registered`: that list is the product taxonomy it checks, and a
validator doesn't enrol itself in the set it validates.

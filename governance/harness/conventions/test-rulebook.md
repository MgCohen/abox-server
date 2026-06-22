# Tests are Rulebooks

The test system has six types (Arch, Structure, Unit, E2E, Wire, Live), each a
*Rulebook* whose definition (`template.md` + `rules.md`) lives in the artifact
registry at `governance/registry/Test/<Type>/` and whose tests live in
`tests/Tests/<Type>/Tests/`; its `### ` headers are guarantees enforced 1:1/1:N by
`[Rule]` facts and a `ParityGuard` — a test never lands without the Rule it proves.
Adding or moving a test? Use the **`test-rulebook`** skill; the front door is
[`tests/README.md`](../../../tests/README.md).

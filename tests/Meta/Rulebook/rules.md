---
docType: rulebook
testType: meta
template: ../../Templates/meta.template.md
harness: ../../Harness/README.md
---

## Rules

### Parity holds for every registered type
- **Why:** Each type's Rulebook headers and its `[Rule]`-cited tests must stay in lockstep — every Rule
  enforced by a test, every test citing a real Rule. This is the one parity guard for the whole repo, driven
  over every registered type, so a Rule with no test (or a test citing a missing Rule) fails the build.

One data-driven check reads `TestTypes.Registered` and scopes `ParityGuard` to each `ABox.Tests.<Type>.Tests`
namespace in the product assembly, requiring every marked test to cite a Rule — then runs once more over
Meta's own Rulebook and tests, so the self-suite holds itself to the same bar.

### Every folder under tests holds a registered test type
- **Why:** Each folder under `tests/Tests/` is a kind of guarantee — a Rulebook with its own parity scope. A
  folder that is none of the registered types (and not shared `Support`) is a test kind no parity scope covers,
  so its tests would run with their `[Rule]` citation unchecked.

`RepoTree.TestTypeFolders()` lists the immediate children of `tests/Tests/`; each must be in
`TestTypes.Registered` or the `Support` allow-list. Standing up a new type means registering it there.

### Every test lives inside a registered test type
- **Why:** Parity scopes `[Rule]` discovery to one `ABox.Tests.<Type>.Tests` namespace, so a test placed
  anywhere else — shared `Support`, a type's own `Support`, the root — runs but is never required to cite a
  Rule. This is the suite-wide backstop that closes that escape.

Reflection over the product assembly (`ABox.Tests.SuiteAnchor`) selects `TestMarkers.Marks` methods whose
namespace fails `TestTypes.ContainsTest`. Meta's own tests are held in scope by Meta's self-parity instead. An
unregistered marker is a patch-when-seen event: add the name to `TestMarkers`.

### Central and Feature types partition the registered types

- **Why:** Co-location turns on one decision per type — does the repo own its guarantee (central) or does a
  feature (co-located). If a type were in both lists it would have two homes; if in neither, no home — and the
  build would silently pick one. Holding the two lists to an exact, disjoint cover of `TestTypes.Registered`
  means every type has exactly one home, and a newly registered type can't compile until it is classified.

`TestTypes.Central` and `TestTypes.Feature` are asserted disjoint and, unioned, equal to
`TestTypes.Registered` — the ownership split (`PLANS/test-colocation.md`) made machine-checkable.

### Every co-located feature Tests folder is policed by a built assembly

- **Why:** Co-location moves a feature's tests out from behind the central protected tree, so the guarantee
  that a feature can't ship untested can no longer be "the tree is protected." It moves here: every `Tests/`
  folder on disk must map to a built `ABox.<Owner>.Tests` assembly whose co-located Rulebook and `[Rule]` tests
  are in lockstep. A `Tests/` folder that ships tests with no assembly — the untested-feature escape — fails,
  and so does a co-located Rulebook drifting from the tests beside it.

`Suites.Colocated()` discovers every feature test assembly from the build output (those carrying the
`TestsSourceDir` metadata), cross-checks the set against `RepoTree.FeatureTestRoots()` on disk, then runs
`ParityGuard.ForColocated` for each (assembly × type) — the same parity the central types get, now across every
co-located suite, driven from outside them.

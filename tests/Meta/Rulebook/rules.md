# Meta Rulebook

Each Rule is one invariant about the **test system itself** — the taxonomy of test types, the Rulebook files,
and their parity with the enforcing tests. These guard the harness, not the product: they run in their own
assembly (`ABox.Tests.Meta`) and reflect over the product suite from outside (via `ABox.Tests.SuiteAnchor`),
the way the Arch guards reflect over `src`. Convention, parity discipline, and the Rule shape live in
[`../../Harness/README.md`](../../Harness/README.md) and `template.md`.

---

### Parity holds for every registered type
- **Why:** Each type's Rulebook headers and its `[Rule]`-cited tests must stay in lockstep — every Rule
  enforced by a test, every test citing a real Rule. This is the one parity guard for the whole repo, driven
  over every registered type, so a Rule with no test (or a test citing a missing Rule) fails the build.

One data-driven check reads `TestTypes.Registered` and scopes `ParityGuard` to each `ABox.Tests.<Type>.Tests`
namespace in the product assembly, applying `requireAllCited` for the complete types — then runs once more over
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

### Every Rule matches its type's template
- **Why:** A type's `template.md` is the schema; without a check it is only aspirational. A rule missing its
  `**Why:**`, carrying a stray bold-label bullet not in the schema, or dropping the result arrow its template
  mandates is organizational drift that would otherwise pass silently.

`RulebookFormat` reads each type's `template.md` for the field set and validates every `### ` rule in
`rules.md` against it — bullet-label set equal to the template's, header arrow iff the template header has one.
Format only, never placeholder content; a new test type is covered the moment its folder lands.

### Every Rulebook holds only rules
- **Why:** Format-checking the `### ` blocks it finds leaves the gaps unguarded — a `## Scratch` section or a
  loose section heading between rules would slip in and let `rules.md` rot into a dumping ground.

`RulebookFormat.Headings` over each `rules.md` allows only the single `# ` title and the `### ` rules; any other
heading level is rejected. Plain prose under a rule stays allowed (the one-line-comment allowance).

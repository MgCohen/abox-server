Template: [template.md](template.md)
Harness: [Rulebook convention](../../../../tests/Harness/README.md)

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

### Every Rule matches its type's template
- **Why:** A type's `template.md` is the schema; without a check it is only aspirational. A rule missing its
  `**Why:**`, carrying a stray bold-label bullet not in the schema, or dropping the result arrow its template
  mandates is organizational drift that would otherwise pass silently.

`RulebookFormat` reads each type's `template.md` for the field set and validates every `### ` rule in
`rules.md` against it — bullet-label set equal to the template's, header arrow iff the template header has one.
Format only, never placeholder content; a new test type is covered the moment its folder lands.

### Every Rulebook holds only rules
- **Why:** A `rules.md` is a pure rule list — its Template/Harness pointer lines then `### ` Rules, nothing
  else. A stray `## Scratch` section or a loose heading between rules would let it rot into a dumping ground;
  all context belongs in `template.md`.

`RulebookFormat.Headings` over each `rules.md` rejects any heading that is not a `### ` rule, and the opening
preamble must carry the Template and Harness links. Plain prose under a rule stays allowed.

### Every template carries judge criteria
- **Why:** Each `template.md` owns a `## Criteria` list — the per-type semantic rubric `/judge-rulebook` grades
  Rules against. Without it the judge has no rubric for that type, so the semantic layer (the checks the
  mechanical guards can't make) would silently go ungraded.

`RulebookFormat.Criteria` reads each `template.md` for `- **<id>:** <description>` bullets under `## Criteria`;
a template with none fails. Mechanical shape stays the other guards' job, so criteria carry only judgment.

### Every template declares a purpose
- **Why:** Each `template.md` opens with a `## Purpose` line — the when-to-use an agent reads to *select* the
  type. Without it a type is discoverable but not selectable: an agent can see it exists yet cannot tell when to
  reach for it over another, so the artifact floor's selection guarantee silently goes unmet.

`RulebookFormat.Purpose` reads the first line under each `template.md`'s `## Purpose`; a template with none
fails. Presence only — whether the line truly guides selection is the judge's call, not the guard's.

### Every artifact declares the floor
- **Why:** The artifact registry (`governance/registry/<Name>/artifact.yml`) is the single source of which
  agent-first types exist; each must declare the floor — a purpose (when to use it), an existing home, a known
  family, and a gate — or a type is registered that an agent can neither select nor the harness validate.

`Artifacts.All` reads each `governance/registry/*/artifact.yml`; the guard checks purpose/home/family/gate are
present and valid, and that `parity` is consistent with `family` (code-first binds a target, nl-first doesn't).
Template and criteria stay the per-type Rulebook guards' job; this owns the registry floor.

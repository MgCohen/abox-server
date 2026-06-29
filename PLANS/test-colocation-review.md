# Review backlog — docs/test-harness co-location refactor

**What this is.** A task-oriented backlog of every finding from the boundary review of the
docs + test-harness refactor (the move from a central type-major test tree to owner-major
co-located suites, plus the doc-engine-as-standard split). Reviews
[`test-colocation.md`](test-colocation.md). Each item is a self-contained task — read it
cold, do it, verify the expected outcome. Ordered by severity.

**Verification status.** Every finding below was re-checked against the real code by an
adversarial pass (each finding verified, then a second agent tried to *refute* it, then a
completeness critic hunted false positives + missed defects). Result: **zero false positives
on existence** — every card points at a real artifact — but the first-pass severities were
inflated and four fixes were over-reaching. This revision recalibrates severity (no item is
CRITICAL; F1 is the lone HIGH) and rewrites the four bad fixes (F2, F7, F9, F14). The
`fix` lines here are the *corrected* ones.

**How to read a card.** `Severity` → triage. `What` → the defect, with `file:line`. `Why`
→ the concrete consequence. `Fix` → the change to make. `Expected outcome` → the
observable, verifiable end state that closes the task.

**One-line state of the refactor.** The boundaries are right and built right *in code*; the
*docs* still describe the pre-migration world, and one real structural gap slipped in (a
co-located taxonomy hole, F1). Close F1, then one coherent doc-sweep. Detail in
[§ Deepest structural risk](#deepest-structural-risk).

**The three boundaries under review.**

| Zone | Owns | Home |
|---|---|---|
| Central tests | enforce **structure** (ownerless: Arch / Structure / Docs / Meta) | `tests/` |
| Co-located tests | a feature's **functionality** (Unit / Wire / E2E / Live + its `rules.md`) | `src/<…>/<Owner>/Tests/` |
| Doc-engine | enforce **format / standards** (doctypes, blocks, templates) | `tools/doc-engine/` |

**Scorecard.**

| Boundary | Verdict |
|---|---|
| Central = enforce structure | 🟢 Sound |
| Co-located = functionality | 🟡 Leaky (F1: root-namespace tests have no taxonomy backstop) |
| Doc-engine = format | 🟡 Leaky (validator untested; standard files unprotected) |
| Placement clarity | 🟠 Drifted (front doors route to the deleted tree — doc-only, code is correct) |
| Responsibility ownership | 🟢 Sound (the type-set "fork" is an intentional, harmless asymmetry — see F2) |

**Severity legend.** `HIGH` unguarded structural invariant with a real escape hatch ·
`MEDIUM` real defect, contained blast radius, no green-when-broken hole · `LOW` cosmetic /
local / doc-only · `PROTECT` confirmed-good, don't regress.

**Suggested execution order.** F1 → S5 → F3 → cheap one-line fixes (F11 / S8 / S6) →
the doc-command sweep (F6 / S2 / S3 / S4) as one PR → the LOW tail as cleanup.

---

## HIGH

### F1 — Co-located tests have no taxonomy backstop
- **Severity:** HIGH *(was CRITICAL — downgraded: it is a real escape hatch, but exploiting
  it requires deliberately authoring a test in a root namespace; no normal workflow trips it,
  and `CoverageTests` still fires on the slnx-only build path.)*
- **What:** Any marker-bearing method placed in a co-located assembly's **root** namespace
  (`ABox.<Owner>.Tests`, i.e. not under a registered `<Type>` sub-namespace) escapes *both*
  parity and taxonomy. `TaxonomyTests.cs:34` (`EveryTestInsideARegisteredType`) reflects only
  over `typeof(SuiteAnchor).Assembly` (= the central `ABox.Tests.Central`); `CoverageTests.cs`
  only runs `ForColocated` over type folders that *already* carry a `Rulebook`; `ParityGuard`
  scope is `{asm}.{type}` (`ParityGuard.cs:33-34`). Nothing reflects over a feature assembly
  to assert *every* test method lives inside a registered type. Note: do **not** frame the
  per-feature `Parity` class as "the violation" — it is a benign intentional instance; the
  defect is the *class* of escape (root namespace = uncitable), which `Parity` merely
  happens to occupy.
- **Why:** The whole point of co-location is that feature tests left the central protected
  wall — the backstop that replaces that wall is "every co-located test is cited." It does
  not exist, so a feature *could* ship an untested/uncited surface and CI stays green. This is
  the single load-bearing safety promise of the move, unmet.
- **Fix:** Add a Meta sweep over every assembly from `Suites.Colocated()` asserting each
  marker-bearing method's namespace is `{asm}.<RegisteredFeatureType>` (type ∈
  Unit/Wire/E2E/Live) or a sub-namespace. Place it in `TaxonomyTests` (mirror the central
  `EveryTestInsideARegisteredType`), not `CoverageTests`. **Build it together with F10** —
  they share a root cause (co-located assemblies have two uncovered surfaces: the root
  namespace *and* non-Rulebook'd type folders); one sweep over `Suites.Colocated()` can close
  both.
- **Expected outcome:** Adding an uncited `[Fact]` to any `ABox.<Owner>.Tests` root namespace
  fails Meta. Mutation check: move a real feature test out of its `<Type>` namespace → red.

---

## MEDIUM

### F3 — The doc-engine validator itself is untested
- **Severity:** MEDIUM *(was HIGH — real gap, but a validator regression is not silently
  green-when-broken across the repo: the Docs type still re-runs the CLI on every instance.)*
- **What:** The format/standards owner is the only zone with zero unit tests. No
  `*.Tests.csproj` exists under `tools/`. `DocEngineTests` only shells out and asserts
  `exit==0` on already-valid instances — the happy path. No test exercises the *reject* path
  (`DocValidator` / `SchemaChecker` refusing a malformed doc). `dirs.proj:9` already globs
  `tools/**/Tests/`, so the seam is ready.
- **Why:** A vacuous-pass regression in the validator (e.g. it stops rejecting) would weaken
  the guard on every Rulebook and template in the repo. The enforcer of format has no enforcer.
- **Fix:** Stand up `tools/doc-engine/Tests/` → `ABox.DocEngine.Tests` (via the
  **new-feature-tests** skill, per `test-colocation.md` Phase 4) with negative cases
  (malformed doc → non-zero exit / specific error) and positive cases for `DocValidator` and
  `SchemaChecker`.
- **Expected outcome:** `dotnet test dirs.proj` includes `ABox.DocEngine.Tests`; breaking the
  validator's reject logic turns it red.

### F4 — The doc-engine standard files are unprotected
- **Severity:** MEDIUM *(was HIGH — real protection gap, but doc-engine standard edits are
  rare and reviewed in practice, and the engine's own validation still runs against them.)*
- **What:** `tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` — the schema the entire
  repo's structured docs validate against — carry no `governance/protected-paths` entry.
  Meanwhile `governance/protected-paths:26` protects `tools/**/Tests/**/Rulebook/**`. Flagged
  at `test-colocation.md:247`; never applied.
- **Why:** The enforcement surface protects the *test* harness but not the *document*
  standard that is now equally load-bearing. An agent can silently weaken every doctype.
- **Fix:** Add `tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` to
  `governance/protected-paths`; regenerate CODEOWNERS.
- **Expected outcome:** Editing a doctype/block/kind/schema requires a reviewed PR; the
  policy-guard blocks an unreviewed change, same as for the harness.

### F5 — ADR 0015's load-bearing `[det]` invariant has no guard
- **Severity:** MEDIUM *(was HIGH — the invariant currently holds and ADR 0010 governance
  already gates harness-csproj edits; the test is cheap belt-and-suspenders, not the only
  thing standing between the repo and a broken boundary.)*
- **What:** "`tests/Harness/**` takes no reference on `ABox.DocEngine` or `YamlDotNet`" is
  documented as machine-checkable (`adr/0015:80`, marked `[det]`; repeated at
  `test-colocation.md:316`) but enforced by no test. The csproj-scan mechanism that could
  assert it already exists at `Structure/Support/SourceTree.cs:158-163`.
- **Why:** This zero-dependency arrow is the spine of the whole boundary model (the
  enforcement harness must not depend on the thing it shells out to). A stray
  `ProjectReference` would compile and pass.
- **Fix:** Point the existing csproj-scan at `ABox.Tests.Harness.csproj`: assert no
  `ProjectReference` to DocEngine and no `PackageReference` to YamlDotNet.
- **Expected outcome:** Adding either reference to the harness csproj fails a Structure/Arch
  test. ADR 0015's `[det]` claim becomes true.

### F6 — Every front-door doc + the `test-rulebook` skill describes the pre-migration world
- **Severity:** MEDIUM *(was HIGH — corrected: this is **not** a green-when-broken hole.
  `CoverageTests.EveryFeatureTestsFolderIsParityChecked` fails on a slnx-only build, so a
  newcomer running `dotnet test ABox.slnx` does not silently get a false green on missing
  co-located suites. The real defect is the wrong newcomer command + stale routing in the
  docs — a clarity problem, not a safety hole.)*
- **What:** The routing docs still describe the central type-major tree the refactor
  deleted, and tell a newcomer to run `dotnet test ABox.slnx` instead of `dotnet test
  dirs.proj`. Full edit list in [§ Doc sweep](#doc-sweep-the-drift-checklist) below.
- **Why:** Placement clarity (Q2) has drifted: a newcomer trusting any front door builds in
  the wrong zone and runs the wrong command. The code is correct and unread.
- **Fix:** One coherent doc-sweep PR (not scattered typo fixes) — rewrite the **bodies**, not
  just the banners, to the `dirs.proj` + central/co-located split. See the checklist.
- **Expected outcome:** Every front door names `dotnet test dirs.proj`, the central
  (`ABox.Tests.Central`) vs co-located (`ABox.<Owner>.Tests`) split, and routes each artifact
  kind to its real home. A newcomer following any single doc lands correctly.

### F8 — Two skills contradict on "does a feature add its own csproj?"
- **Severity:** MEDIUM
- **What:** `test-rulebook` SKILL `:118` says "No new test csproj is ever needed" (the old
  central-glob model — it also names the wrong project `ABox.Tests` and the wrong glob);
  `new-feature-tests` `:46-65` correctly describes the per-owner `ABox.<Owner>.Tests.csproj`
  (current reality).
- **Why:** A newcomer reading the two skills gets opposite instructions for the same task —
  placement ambiguity at the point of action.
- **Fix:** Surgical: rewrite `test-rulebook:118` (and the stale link `:32`, namespace `:83`,
  run command `:134`) to the co-location model, or scope the skill to central types only and
  defer all feature placement to `new-feature-tests`. Not a full section rewrite.
- **Expected outcome:** The two skills agree; one names the feature csproj, neither denies it.

### F11 — `Suites.cs` excludes a literal `"ABox.Tests"` that can't exist
- **Severity:** MEDIUM
- **What:** `tests/Meta/Tests/Suites.cs:41` filters out an assembly literally named
  `"ABox.Tests"` — but the central assembly is `ABox.Tests.Central`, and the real exclusion
  is the `SourceDir` filter on line 30. Dead clause; the comment (line 8) names a
  non-existent assembly.
- **Why:** Misleading dead code in the discovery seam; reads as if `ABox.Tests` is a live
  participant.
- **Fix:** Drop the dead clause; fix the comment to name `.Central` / `.Meta` and point at
  the `SourceDir` filter as the real mechanism.
- **Expected outcome:** `Suites.cs` references only assemblies that exist.

---

## LOW

### F2 — The test-type set differs between the taxonomy enum and the doctype enum
- **Severity:** LOW *(was HIGH — corrected: this is **not** an uncontrolled fork. The
  asymmetry is intentional and correct.)*
- **What:** Taxonomy owner `TestTypes.cs:11` lists **7** (`Arch, Structure, Unit, E2E, Wire,
  Live, Docs` — no `meta`); format owner `tools/doc-engine/doctypes/rulebook.yaml:7` and
  `test-template.yaml:7` list **8** (the same plus `meta`). They are *different axes*:
  `Registered` is the set of product test types (Meta is the self-suite, deliberately outside
  it); the doctype enum must include `meta` because `tests/Meta/Rulebook/rules.md:3` carries
  `testType: meta` and must validate. **Neither is wrong.**
- **Why:** The only real defect is that nothing *documents* why the two differ, so a future
  reader may "reconcile" them and break Meta's own Rulebook validation.
- **Fix:** Do **not** generate the doctype enum from code or assert set-equality — that would
  fight the intentional asymmetry and break Meta. Instead, add a one-line comment at
  `TestTypes.cs:6-7` stating `Registered` = product test types and Meta is the self-suite
  deliberately outside it (mirror a note in the doctype yaml if helpful).
- **Expected outcome:** The 7-vs-8 difference is explained in place; no code-gen, no equality
  assertion, Meta's Rulebook still validates.

### F7 — Instance-discovery scan lives in `tests/`
- **Severity:** LOW *(was MEDIUM — corrected: `DocInstances.cs` and the engine's
  `InstanceParser` are **not** duplicates — one is a cheap repo-wide line scan for `docType:`,
  the other a full YAML parse — and the fix as originally written would break ADR 0015.)*
- **What:** `tests/Tests/Docs/Support/DocInstances.cs:31-39` walks the repo and decides what
  counts as a doc-engine instance by scanning leading front matter for a `docType:` key. The
  engine CLI has no `validate --all`, so the test owns discovery.
- **Why:** Minor responsibility bleed — the test root knows the "what is an instance" shape.
  Contained, because the scan is trivial and the authoritative parse stays in the engine.
- **Fix:** Optional, perf-only: add `validate --all` (repo-wide discovery + validate) to the
  engine and have the Docs type shell out to it once. Do **not** delete `DocInstances.cs` by
  taking a `ProjectReference` on the engine — that violates ADR 0015's zero-dependency harness
  boundary. If `validate --all` is not added, leave the scan as-is.
- **Expected outcome:** Either an engine-side `validate --all` the Docs type shells out to, or
  the scan stays (acceptably) where it is. The harness never code-references the engine.

### F9 — `TestTypes.Namespace()` is central-only but generically named
- **Severity:** LOW *(was MEDIUM — corrected: `Namespace()` is only reached from central code
  paths (`ParityGuard.For()` / `ContainsTest`), so the "call it for a feature type and get a
  wrong namespace" trap is not actually reachable today. Renaming it would break `For():24`.)*
- **What:** `TestTypes.cs:30` hardcodes `ABox.Tests.{type}.Tests` (central convention); the
  co-located namespace `ABox.<Owner>.Tests.<Type>` is built separately inline in
  `ParityGuard.cs:33-34` (`ForColocated`). Two builders, one un-obvious scope.
- **Why:** A generically-named method that only works for central types is a latent trap, but
  the trap is dormant — nothing calls it for a feature type.
- **Fix:** Doc-only: add a one-line comment on `TestTypes.cs:30` that `Namespace()` builds
  **central** namespaces only; co-located namespaces are built in `ParityGuard.ForColocated`.
  Do **not** rename to `CentralNamespace` (breaks `ParityGuard.For():24`) or add a speculative
  `ColocatedNamespace` (YAGNI — second real caller first).
- **Expected outcome:** The method's central-only scope is stated where it's defined; no
  rename, no new method.

### F10 — Per-feature `Parity.cs` hardcodes its own type array
- **Severity:** LOW *(was MEDIUM — corrected rationale below; exploitability is low because
  registering a type requires a Rulebook anyway.)*
- **What:** e.g. `src/Features/Projects/Tests/Parity.cs:12` hardcodes `{ "Unit" }`. Adding a
  `Wire/` folder to that feature without editing `Parity.cs` means the local "fast signal"
  parity check silently skips it.
- **Why:** **Correction to the original claim:** "Meta still catches it" is *false* for a
  type folder with no `Rulebook/` — `CoverageTests.TypeFolders` (`CoverageTests.cs:40-44`)
  only discovers folders that already contain a `Rulebook/`, so a Rulebook-less `Wire/` folder
  is seen by *neither* local Parity *nor* Meta. (It is, however, low-risk: a type isn't
  "registered" until it has a Rulebook, and adding the Rulebook is what brings it into
  coverage.)
- **Fix:** Fold into **F1**'s Meta sweep: one sweep over `Suites.Colocated()` that asserts
  every direct child folder of a feature `Tests/` is either a registered type *with* a
  Rulebook or `Support`, closing this and F1's root-namespace gap together. Deriving the
  local `Parity.cs` list from disk does **not** close it (it would still only see Rulebook'd
  folders) — prefer deleting the local array in favor of the central sweep.
- **Expected outcome:** Adding a type folder to a feature is caught centrally whether or not
  it has a Rulebook yet; no hand-edited per-feature array.

### F12 — The plan's worked example (`Tasks`) has no `Tests/` on disk
- **Severity:** LOW *(was MEDIUM — illustrative diagram, not a guard.)*
- **What:** `test-colocation.md:126-135` uses `src/Features/Tasks/` as the worked
  co-location example, but that folder has only `Contract/ Create/ Module/` — no `Tests/`. The
  real migrated exemplar `Flows` sits right below at lines 137-143.
- **Why:** A newcomer following the canonical example to a folder that doesn't demonstrate the
  pattern. Placement-clarity paper cut.
- **Fix:** Annotate the `Tasks` block `[Phase 2 / planned]` rather than re-pointing to
  `Flows` (re-pointing loses the Type-subfolder teaching detail the diagram is showing).
- **Expected outcome:** The diagram's `Tasks` example is marked aspirational; `Flows` remains
  the real-on-disk exemplar.

### F13 — No front door for adding a new doctype or template
- **Severity:** LOW *(was MEDIUM — adding a doctype is rare and reviewed; a missing howto is a
  paper cut, not a hazard.)*
- **What:** There is no `doctypes/README` and no `howto/add-a-doctype.md`; the
  standard/criteria/guarantee three-layer split is tabled only in `test-colocation.md:172-182`.
  `howto/` covers add-a-kind / add-a-block / add-an-instance but not the doctype or the
  central template.
- **Why:** Two artifact kinds (a new doctype, a new per-type `template.md`) have no obvious
  home or procedure — placement ambiguity at the top of the standard.
- **Fix:** Primary: promote the standard/criteria/guarantee 3-layer table
  (`test-colocation.md:172-182`) into `tools/doc-engine/README.md` so the split is surfaced
  where doctypes live. A separate `add-a-doctype.md` howto is *optional* and should be avoided
  if it would imply doctype creation is routine/unreviewed.
- **Expected outcome:** The three-layer split is documented at the doc-engine front door; the
  per-type template's home (`tests/Templates/`) is noted.

### F14 — Doctype rubric mentions test-engine type-semantics
- **Severity:** LOW *(was MEDIUM — corrected: the rubric is **advisory text**, not an
  enforced rule. `DocValidator.cs` enforces nothing per-type — verified, zero `rubric`
  references — so there is no functional boundary violation, only descriptive bleed.)*
- **What:** `tools/doc-engine/doctypes/rulebook.yaml:17` rubric text classifies
  `e2e/wire/live` as "behavioral" — test-engine knowledge that `adr/0015:47-49` says stays on
  the test side. It is authoring/judging guidance, read by no validator.
- **Why:** Mild descriptive bleed; if the test taxonomy changes, the advisory text silently
  goes stale. No code lies, because no code reads it.
- **Fix:** Documentation-only: note in ADR 0015 that doctype rubrics are *advisory* authoring/
  judging text, outside the `[det]` guarantee. Do **not** "move the per-type classification to
  the test-template criteria" — that presumes an enforcement coupling that does not exist.
- **Expected outcome:** ADR 0015 states rubrics are advisory; no structural change.

---

## Doc sweep — the drift checklist

These are the concrete edits behind **F6**. Execute as one coherent PR. Each: the doc lies
about the code; the fix is to make it tell the truth; the outcome is a front door a newcomer
can trust. (S5 is the highest-value item — it's the one walkthrough that omits the
co-located path entirely.)

- [ ] **S1 (LOW)** `PLANS/test-colocation.md:3` — bump the status line "🟡 proposed — not yet
      built" → implemented (Phases 0–4 shipped; note doc-engine `Tests/` (F3) + governance
      remap (F4) as the tail). One-line edit; the rest of the plan is accurate.
- [ ] **S2 (MEDIUM)** `tests/Harness/RepoTree.cs:37-38` — the comment "Today no feature
      Tests/ exist, so this returns exactly the central set" is false (8 feature `Tests/` are
      live and the same file's methods return them). Per the no-comments rule, delete the
      stale narration (or rewrite to present-tense: returns central ∪ feature).
- [ ] **S3 (MEDIUM)** `tests/README.md:64-68` — `dotnet test ABox.slnx` → `dotnet test
      dirs.proj`; plan pointer `test-structure.md` → `test-colocation.md`; "six/seven types in
      `ABox.Tests`" → `ABox.Tests.Central` (central) + co-located `ABox.<Owner>.Tests`.
- [ ] **S4 (MEDIUM)** `tests/Tests/README.md:13,22-25` — reconcile the internal
      contradiction: lines 3-6 already say Unit/Wire/E2E/Live live co-located, but line 13
      ("these six test the product") + the bullets still list them centrally. Cut the stale
      bullets; describe only the ownerless `Arch/Structure/Docs`.
- [ ] **S5 (HIGH within the sweep)** `tests/Harness/README.md:31,45-46,157-218` — `ABox.Tests`
      → `.Central`; the "standing up a new test *type*" walkthrough (157-218) omits
      `ParityGuard.ForColocated` and the co-located feature path entirely — add it. Highest-
      value doc fix.
- [ ] **S6 (MEDIUM)** `tests/Meta/README.md:11-18` — says "three guards"; `CoverageTests` is a
      real 4th. Update the count + add the bullet (it's the load-bearing co-location backstop).
- [ ] **S7 (LOW)** `.claude/skills/test-rulebook/SKILL.md:32,83,118,134` — plan link (→
      `test-colocation.md`), namespace, the csproj/glob claim (F8), and the run command →
      co-location model.
- [ ] **S8 (MEDIUM)** `tests/Tests/ABox.Tests.Central.csproj:30` — the comment "This assembly
      is named `ABox.Tests`" is false; the `AssemblyName` (line 5) is `ABox.Tests.Central`.
      One-line comment fix.

---

## PROTECT — confirmed-good, don't regress

### P1 — Central stays purely structural
- **What:** Arch / Structure / Meta reference no feature behavior; ArchUnitNET + filesystem
  scan are the right primitives (`Structure/Support/*`, `Arch/Tests/RuleTests.cs`).
- **Why it's right:** The elegant core of the model — the central zone enforces structure and
  *only* structure.
- **Expected outcome (to preserve):** No feature-behavior assertion ever lands in `tests/`.

### P2 — Zero-dependency spine via shell-out
- **What:** `tests/Tests/Docs/Support/DocEngine.cs` invokes the CLI by `ProcessStartInfo`,
  never a `ProjectReference`; `ABox.Tests.Harness.csproj` references only xunit.
- **Why it's right:** ADR 0015's dependency arrow points *out* of the enforcement spine, for
  real and acyclically. (Guard it — see F5.)
- **Expected outcome (to preserve):** Harness never takes a code dependency on the engine.

### P3 — `TestsSourceDir` metadata seam
- **What:** `ParityGuard.ForColocated` reads the *same* on-disk `rules.md` the doc-engine
  validates — no copy-to-output step that could drift; every locator fails loud, never
  green-on-empty (`ParityGuard.cs:38-47`, `RepoTree.cs:90-109`).
- **Why it's right:** One source of truth for a Rulebook, read in place; broken scans throw
  rather than pass vacuously. (Parity reads the file's shape; the doc-engine validates its
  format — different checks on one file, not duplicated state.)
- **Expected outcome (to preserve):** No copied/duplicated Rulebook in build output.

### P4 — CoverageTests backstop is real and non-vacuous
- **What:** Mutation-tested — removing a built feature assembly fails Meta
  (`CoverageTests.cs`).
- **Why it's right:** The co-location safety story's load-bearing half works end-to-end. (Its
  gap is F1/F10, the *taxonomy* half, not this.)
- **Expected outcome (to preserve):** Dropping a feature assembly stays red.

### P5 — Standard → criteria → guarantee is a clean 3-layer vertical
- **What:** doctype (central) → `template.md` (central, per-type) → `rules.md` (co-located),
  physically separated, one validation pass each (`test-colocation.md:174-182`).
- **Why it's right:** The model's real load-bearing idea; the one artifact that gets the
  vertical exactly right. Promote it to canonical (see Deepest risk / F13).
- **Expected outcome (to preserve):** The three layers stay physically and ownership-separated.

### P6 — `src/Api/doc-catalog.json` is a deliberate seam, not a leak
- **What:** Generated by the engine, shipped by the contract rollup, enforced current by a
  Docs Rule.
- **Why it's right:** A controlled, guarded export — not duplicated state.
- **Expected outcome (to preserve):** Stays generated + freshness-guarded, never hand-edited.

---

## Deepest structural risk

**The three-way boundary is three orthogonal cut-lines fused into one rhetorical box, and
`Docs` is the type that proves they don't separate.**

- **What:** "Central / co-located / doc-engine" reads as one partition but is three
  independent axes — **ownership** (ownerless vs feature-owned), **guarantee** (structure vs
  functionality), **mechanism** (doc-engine format vs test-engine parity). The `Docs` type is
  central-on-ownership, format-on-mechanism, and behavioral-test-code-on-guarantee — one
  type, three zones — and the plan holds it up as "the whole model in one type"
  (`test-colocation.md:71-73`) *precisely because* it's the cell where all three axes meet.
- **Why it matters / how it rots:** Because the axes are conflated, ownership questions get
  answered by mechanism intuition and vice versa. F1 (the co-located taxonomy hole) is the
  clearest example: the *ownership* move (tests left the central wall) outran the *mechanism*
  (no reflection over feature assemblies' root namespace). The verification also showed the
  inverse failure mode — F2 was *mis*-flagged as a fork precisely by reading an intentional
  ownership asymmetry (Registered vs doctype-enum) as a mechanism bug. Both directions are the
  same conflation.
- **Fix:** Stop selling it as one boundary. State the three axes explicitly in one canonical
  place (CLAUDE.md or `tests/README.md`); name `Docs` as the deliberate **bridge type** where
  ownership=central, mechanism=doc-engine-shellout, guarantee=meta-structural intersect — not
  a clean member of one zone. Promote the `test-colocation.md:174-182`
  standard/criteria/guarantee table to the canonical statement and rewrite the front doors to
  mirror it.
- **Expected outcome:** The three axes are named once and referenced everywhere; `Docs` is
  documented as a bridge, not an exemplar of clean separation; the next fork has no room to
  form because the axis it would violate is now explicit.

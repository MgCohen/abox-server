# Review backlog — docs/test-harness co-location refactor

**What this is.** A task-oriented backlog of every finding from the boundary review of the
docs + test-harness refactor (the move from a central type-major test tree to owner-major
co-located suites, plus the doc-engine-as-standard split). Reviews
[`test-colocation.md`](test-colocation.md). Each item is a self-contained task — read it
cold, do it, verify the expected outcome. Ordered by severity.

**How to read a card.** `Severity` → triage. `What` → the defect, with `file:line`. `Why`
→ the concrete consequence. `Fix` → the change to make. `Expected outcome` → the
observable, verifiable end state that closes the task.

**One-line state of the refactor.** The boundaries are right and built right *in code*; the
*docs* still describe the pre-migration world, and two structural gaps slipped in (a
co-located taxonomy hole, a forked type-set). Close the code gaps, then one coherent
doc-sweep. Detail in [§ Deepest structural risk](#deepest-structural-risk).

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
| Co-located = functionality | 🟡 Leaky (no taxonomy backstop) |
| Doc-engine = format | 🟡 Leaky (validator untested; discovery in `tests/`) |
| Placement clarity | 🔴 Broken (front doors route to the deleted tree) |
| Responsibility ownership | 🟡 Leaky (type-set forked; namespace split) |

**Severity legend.** `CRITICAL` a guard that can't fail / silent false-green · `HIGH`
unguarded invariant or broadly-misleading front door · `MEDIUM` real smell, contained
blast radius · `LOW` cosmetic / local · `PROTECT` confirmed-good, don't regress.

---

## CRITICAL

### F1 — Co-located tests have no taxonomy backstop
- **Severity:** CRITICAL
- **What:** An uncited `[Fact]` placed at a feature assembly's root namespace (i.e. not
  under a registered `<Type>` namespace) escapes *both* parity and taxonomy. `TaxonomyTests.cs:34`
  (`EveryTestInsideARegisteredType`) reflects only over `SuiteAnchor.Assembly` (= the central
  `ABox.Tests.Central`); `CoverageTests.cs:35-44` runs `ForColocated` only for folders that
  *already* carry a `Rulebook`; `ParityGuard` scope is `{asm}.{type}` (`ParityGuard.cs:33-34`).
  Nothing reflects over a feature assembly to assert *every* test method lives inside a
  registered type. Proven by mutation: an uncited feature test stays green.
- **Why:** The whole point of co-location is that feature tests left the central protected
  wall — the backstop that replaces that wall is "every co-located test is cited." It does
  not exist, so a feature can ship an untested/uncited surface and CI is green. This is the
  single load-bearing safety promise of the move, unmet.
- **Fix:** Add a Meta sweep over every assembly from `Suites.Colocated()` asserting each
  marker-bearing method's namespace is `{asm}.<RegisteredType>` (or a sub-namespace). Mirror
  the existing central `EveryTestInsideARegisteredType` onto the feature assemblies.
- **Expected outcome:** Adding an uncited `[Fact]` to any `ABox.<Owner>.Tests` root namespace
  fails Meta. Mutation check: move a real feature test out of its `<Type>` namespace → red.

---

## HIGH

### F2 — The test-type set is owned twice and disagrees
- **Severity:** HIGH
- **What:** Two zones each declare the canonical type set and they differ. Taxonomy owner
  `TestTypes.cs:11` lists **7** (`Arch, Structure, Unit, E2E, Wire, Live, Docs` — no `meta`);
  format owner `tools/doc-engine/doctypes/rulebook.yaml:7` and `test-template.yaml:7` list
  **8** (the same plus `meta`). They agree only by hand-editing both.
- **Why:** No single owner of "what types exist." The two drift independently — exactly the
  mechanism by which the other forks in this backlog formed. A new type added in one place is
  silently absent in the other.
- **Fix:** One source of truth. Either generate the doctype enum from `TestTypes.Registered`,
  or add a Docs/Meta test asserting `doctype enum == TestTypes.Registered ∪ {meta}`.
- **Expected outcome:** Adding a type in one place without the other fails a test. The 7-vs-8
  discrepancy is reconciled and pinned.

### F3 — The doc-engine validator itself is untested
- **Severity:** HIGH
- **What:** The format/standards owner is the only zone with zero unit tests. No
  `*.Tests.csproj` exists under `tools/` (`find tools -name '*.Tests.csproj'` → none).
  `DocEngineTests` only shells out and asserts `exit==0` on already-valid instances — the
  happy path. No test exercises the *reject* path (`DocValidator` / `SchemaChecker` refusing
  a malformed doc). `dirs.proj:9` already globs `tools/**/Tests/`, so the seam is ready.
- **Why:** A vacuous-pass regression in the validator (e.g. it stops rejecting) would leave
  every Rulebook and template in the repo unguarded, and *nothing* would go red — the Docs
  type would still pass because its inputs are valid. The enforcer of format has no enforcer.
- **Fix:** Stand up `tools/doc-engine/Tests/` → `ABox.DocEngine.Tests` (via the
  **new-feature-tests** skill) with negative cases (malformed doc → non-zero exit / specific
  error) and positive cases for `DocValidator` and `SchemaChecker`.
- **Expected outcome:** `dotnet test dirs.proj` includes `ABox.DocEngine.Tests`; breaking the
  validator's reject logic turns it red.

### F4 — The doc-engine standard files are unprotected
- **Severity:** HIGH
- **What:** `tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` — the schema the entire
  repo's structured docs validate against — carry no `governance/protected-paths` entry.
  Meanwhile `governance/protected-paths:26` protects `tools/**/Tests/**/Rulebook/**`, which
  don't yet exist. Flagged at `test-colocation.md:247`; never applied.
- **Why:** The enforcement surface protects the *test* harness but not the *document*
  standard that is now equally load-bearing. An agent can silently weaken every doctype.
- **Fix:** Add `tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` to
  `governance/protected-paths`; regenerate CODEOWNERS.
- **Expected outcome:** Editing a doctype/block/kind/schema requires a reviewed PR; the
  policy-guard blocks an unreviewed change, same as for the harness.

### F5 — ADR 0015's load-bearing `[det]` invariant has no guard
- **Severity:** HIGH
- **What:** "`tests/Harness/**` takes no reference on `ABox.DocEngine` or `YamlDotNet`" is
  documented as machine-checkable (`adr/0015:80`, marked `[det]`; repeated at
  `test-colocation.md:316`) but enforced by no test. The csproj-scan mechanism that could
  assert it already exists at `Structure/Support/SourceTree.cs:158-163`.
- **Why:** This zero-dependency arrow is the spine of the whole boundary model (the
  enforcement harness must not depend on the thing it shells out to). The most load-bearing
  boundary is the unguarded one — a stray `ProjectReference` would compile and pass.
- **Fix:** Point the existing csproj-scan at `ABox.Tests.Harness.csproj`: assert no
  `ProjectReference` to DocEngine and no `PackageReference` to YamlDotNet.
- **Expected outcome:** Adding either reference to the harness csproj fails a Structure/Arch
  test. ADR 0015's `[det]` claim becomes true.

### F6 — Every front-door doc + the `test-rulebook` skill describes the pre-migration world
- **Severity:** HIGH (individually each edit is medium; collectively this is the headline)
- **What:** The routing docs still describe the central type-major tree the refactor
  deleted, and tell a newcomer to run `dotnet test ABox.slnx` — a strict subset that
  **silently skips every co-located feature suite** and reports green. Full edit list in
  [§ Doc sweep](#doc-sweep-the-drift-checklist) below.
- **Why:** Placement clarity (Q2) is *broken*: a newcomer trusting any front door builds in
  the wrong zone and gets a false-green test run. The code is correct and unread.
- **Fix:** One coherent doc-sweep PR (not scattered typo fixes) — rewrite the **bodies**, not
  just the banners, to the `dirs.proj` + central/co-located split. See the checklist.
- **Expected outcome:** Every front door names `dotnet test dirs.proj`, the central
  (`ABox.Tests.Central`) vs co-located (`ABox.<Owner>.Tests`) split, and routes each artifact
  kind to its real home. A newcomer following any single doc lands correctly.

---

## MEDIUM

### F7 — Instance-discovery policy lives in `tests/`, not the engine
- **Severity:** MEDIUM
- **What:** `tests/Tests/Docs/Support/DocInstances.cs:31-39` walks the repo and decides what
  counts as a doc-engine instance by re-parsing leading `---` front matter for a `docType:`
  key — duplicating the engine's own `InstanceParser` front-matter logic. The engine CLI has
  no `validate --all`, so the test owns discovery.
- **Why:** Doc-engine responsibility (what *is* an instance) leaks into the test root —
  precisely the "docEngine responsibility outside the docEngine root" smell. Two parsers of
  the same format will drift.
- **Fix:** Add a `validate --all` (repo-wide discovery + validate) to the engine; have the
  Docs type shell out to it once; delete `DocInstances.cs`.
- **Expected outcome:** Discovery logic exists only in `tools/doc-engine/`; the Docs test is a
  thin shell-out; no front-matter parsing under `tests/`.

### F8 — Two skills contradict on "does a feature add its own csproj?"
- **Severity:** MEDIUM
- **What:** `test-rulebook` SKILL `:118` says "No new test csproj is ever needed" (the old
  central-glob model); `new-feature-tests` `:46-65` correctly describes the per-owner
  `ABox.<Owner>.Tests.csproj` (current reality).
- **Why:** A newcomer reading the two skills gets opposite instructions for the same task —
  placement ambiguity at the point of action.
- **Fix:** Rewrite `test-rulebook` §5–6 to the co-location model, or scope it to central
  types only and defer all feature placement to `new-feature-tests`.
- **Expected outcome:** The two skills agree; one names the feature csproj, neither denies it.

### F9 — `TestTypes.Namespace()` is central-only but generically named
- **Severity:** MEDIUM
- **What:** `TestTypes.cs:30` hardcodes `ABox.Tests.{type}.Tests` (central convention) yet
  accepts the full `Registered` set including the feature types, whose real namespace is
  `ABox.<Owner>.Tests.<Type>`, built separately inline in `ParityGuard.cs:33-34`
  (`ForColocated`). Namespace convention has two owners.
- **Why:** A generically-named method that only works for central types is a trap: call it
  for a feature type and get a wrong namespace, silently. Latent fork.
- **Fix:** Rename to `CentralNamespace`, and/or give `TestTypes` a single
  `ColocatedNamespace(asm, type)` so the namespace convention has one owner.
- **Expected outcome:** One place builds any test namespace; the method name states its scope.

### F10 — Per-feature `Parity.cs` hardcodes its own type array
- **Severity:** MEDIUM
- **What:** e.g. `src/Features/Projects/Tests/Parity.cs:12` hardcodes `{ "Unit" }`. Adding a
  `Wire/` folder to that feature without editing `Parity.cs` means the local "fast signal"
  parity check silently skips it (Meta still catches it, but later).
- **Why:** The local guard's coverage is a hand-maintained list that drifts from disk.
- **Fix:** Derive the type list from disk (as `CoverageTests.TypeFolders` already does), or
  delete the local `Parity.cs` and rely on the central Meta sweep.
- **Expected outcome:** Adding a type folder to a feature is automatically parity-checked; no
  hand-edited array.

### F11 — `Suites.cs` excludes a literal `"ABox.Tests"` that can't exist
- **Severity:** MEDIUM
- **What:** `tests/Meta/Tests/Suites.cs:42` filters out an assembly literally named
  `"ABox.Tests"` — but the central assembly is `ABox.Tests.Central` (already excluded by the
  suffix filter). Dead clause; the comment names a non-existent assembly.
- **Why:** Misleading dead code in the discovery seam; reads as if `ABox.Tests` is a live
  participant.
- **Fix:** Drop the dead clause; fix the comment to name `.Central` / `.Meta`.
- **Expected outcome:** `Suites.cs` references only assemblies that exist.

### F12 — The plan's worked example (`Tasks`) has no `Tests/` on disk
- **Severity:** MEDIUM
- **What:** `test-colocation.md` uses `src/Features/Tasks/` as the worked co-location
  example, but that folder has only `Contracts/ Create/ Module/` — no `Tests/`. The real
  migrated exemplars are `Flows`, `Git`, `Inbox`, `Decisions`, `Projects`, `Agents`, `Host`,
  `Infrastructure`.
- **Why:** A newcomer following the canonical example to a folder that doesn't demonstrate the
  pattern. Placement-clarity paper cut.
- **Fix:** Point the example at `Flows` (a real migrated exemplar) or mark `Tasks`
  explicitly illustrative/aspirational.
- **Expected outcome:** The plan's example resolves to a folder that actually shows a
  co-located suite.

### F13 — No front door for adding a new doctype or template
- **Severity:** MEDIUM
- **What:** There is no `doctypes/README` and no `howto/add-a-doctype.md`; the
  standard/criteria/guarantee three-layer split is tabled only in `test-colocation.md:172-182`,
  not surfaced where a newcomer adds a doctype. `howto/` covers add-a-kind / add-a-block /
  add-an-instance but not the doctype or the central template.
- **Why:** Two artifact kinds (a new doctype, a new per-type `template.md`) have *no* obvious
  home or procedure — placement ambiguity at the top of the standard.
- **Fix:** Add `tools/doc-engine/howto/add-a-doctype.md` and a "new per-type template →
  `tests/Templates/`" note; cross-link the three-layer table.
- **Expected outcome:** Every doc-engine artifact kind has a one-hop front door.

### F14 — Doctype rubric encodes test-engine type-semantics
- **Severity:** MEDIUM
- **What:** `tools/doc-engine/doctypes/rulebook.yaml:17` rubric text classifies `e2e/wire/live`
  as "behavioral" — test-engine knowledge that `adr/0015:47-49` says stays on the test side.
- **Why:** Mild bleed of test-taxonomy meaning into the format schema; if the test taxonomy
  changes, the rubric silently lies.
- **Fix:** Document rubrics as advisory (outside the `[det]` guarantee) in ADR 0015, or move
  the per-type classification to the central `test-template` criteria.
- **Expected outcome:** Format schema carries no authoritative test-type semantics, or the
  rubric is explicitly marked advisory.

---

## Doc sweep — the drift checklist

These are the concrete edits behind **F6**. Execute as one coherent PR. Each: the doc lies
about the code; the fix is to make it tell the truth; the outcome is a front door a newcomer
can trust.

- [ ] `PLANS/test-colocation.md:3` — flip status "proposed — not yet built" → implemented
      (Phases 0–4 done; note doc-engine `Tests/` (F3) + governance remap (F4) as the tail).
- [ ] `tests/Harness/RepoTree.cs:37-38` — **delete** the comment "Today no feature Tests/
      exist, so this returns exactly the central set." 8 feature `Tests/` are live and the
      same file's methods return them; per the no-comments rule, delete the stale narration.
- [ ] `tests/README.md:64-68` — `dotnet test ABox.slnx` → `dotnet test dirs.proj`; plan
      pointer `test-structure.md` → `test-colocation.md`; "six/seven types in `ABox.Tests`"
      → `ABox.Tests.Central` (central) + co-located `ABox.<Owner>.Tests`.
- [ ] `tests/Tests/README.md:13,22-25` — cut "these six test the product" + the
      Unit/E2E/Wire/Live bullets; describe only the ownerless `Arch/Structure/Docs`.
- [ ] `tests/Harness/README.md:31,45-46,157-218` — `ABox.Tests` → `.Central`; add
      `ForColocated` + the co-located namespace to the parity walkthrough; "standing up a new
      type" must say templates are central and behavioral types go via `new-feature-tests`.
- [ ] `tests/Meta/README.md:7-8,13-15` — add the **CoverageTests** guard (the load-bearing
      backstop it omits); note parity is central+Meta, coverage is the feature assemblies.
- [ ] `.claude/skills/test-rulebook/SKILL.md:32,83,118,134` — plan link, namespace, the
      csproj/glob claim (F8), and the run command → co-location model.
- [ ] `tests/Tests/ABox.Tests.Central.csproj:30` — the comment "This assembly is named
      `ABox.Tests`" is false; the `AssemblyName` is `ABox.Tests.Central`.

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
  rather than pass vacuously.
- **Expected outcome (to preserve):** No copied/duplicated Rulebook in build output.

### P4 — CoverageTests backstop is real and non-vacuous
- **What:** Mutation-tested — removing a built feature assembly fails Meta
  (`CoverageTests.cs`).
- **Why it's right:** The co-location safety story's load-bearing half works end-to-end. (Its
  gap is F1, the *taxonomy* half, not this.)
- **Expected outcome (to preserve):** Dropping a feature assembly stays red.

### P5 — Standard → criteria → guarantee is a clean 3-layer vertical
- **What:** doctype (central) → `template.md` (central, per-type) → `rules.md` (co-located),
  physically separated, one validation pass each (`test-colocation.md:174-182`).
- **Why it's right:** The model's real load-bearing idea; the one artifact that gets the
  vertical exactly right. Promote it to canonical (see Deepest risk).
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
  answered by mechanism intuition and vice versa — which is exactly how the type-set fork
  (F2) and the co-located taxonomy hole (F1) slipped in. Each is one axis quietly disagreeing
  with another while the prose says "one boundary."
- **Fix:** Stop selling it as one boundary. State the three axes explicitly in one canonical
  place (CLAUDE.md or `tests/README.md`); name `Docs` as the deliberate **bridge type** where
  ownership=central, mechanism=doc-engine-shellout, guarantee=meta-structural intersect — not
  a clean member of one zone. Promote the `test-colocation.md:174-182`
  standard/criteria/guarantee table to the canonical statement and rewrite the front doors to
  mirror it.
- **Expected outcome:** The three axes are named once and referenced everywhere; `Docs` is
  documented as a bridge, not an exemplar of clean separation; the next two forks have no room
  to form because the axis each would violate is now explicit.

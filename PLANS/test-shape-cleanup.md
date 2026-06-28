# Test shape & naming normalization — action plan

**Status:** planned, not started · **Branch:** `claude/docs-test-refactor-review-cqugsd`
**Plan of record it amends:** [`test-colocation.md`](test-colocation.md)

Working doc for an in-flight cleanup of the test layout's *names and shapes*. The
Rulebook/parity *discipline* is unchanged — only where files sit and what they're called.

> **Two counts a cold reader needs:** there are **7 rubrics** — one per test type (Arch, Structure, Unit, E2E,
> Wire, Live, Docs), central in `tests/Rubrics/`, holding the shared judge criteria — and **15 rulebook
> instances** — one per co-located feature×type, each stating that feature's guarantees against its rubric.
> C1 touches the 7 rubrics + their doctype; C2 moves the 15 rulebooks. `docs` above is itself one of the
> registered test types (so "the `right-type` line omits `docs`" later means a real type went unlisted).

## Why — three inconsistencies that mislead a reader

| # | Problem | Evidence |
|---|---|---|
| 1 | **"template" is misnamed** — it's a per-type *rubric* (a `## Summary` + judge `## Criteria`), not a fill-in skeleton. The real skeleton lives in the harness README. | `tests/Templates/arch.template.md`, `docType: test-template` |
| 2 | **file name ≠ doctype**, inside a vestigial single-file folder | `…/<Type>/Rulebook/rules.md` (`docType: rulebook`); the `Rulebook/` folder holds only `rules.md` |
| 3 | **central and co-located type-folders differ in shape** | central nests `.cs` under `<Type>/Tests/`; co-located puts `.cs` directly in `<Type>/` |

\#3's cause is one line — `TestTypes.Namespace` returns `ABox.Tests.<Type>.Tests` (trailing
`.Tests`), and IDE0130 (`namespace = folder`, severity error) then *forces* the inner `Tests/`
folder. Co-located is `ABox.<Owner>.Tests.<Type>` (no trailing `.Tests`), so `.cs` sits directly.

## Decisions

- **`template` → `rubric`.** doctype `rubric`; folder `tests/Rubrics/`; files `tests/Rubrics/<Type>.md`
  (the folder gives context — no redundant `.rubric.md` suffix); rulebook front-matter key `template:` → `rubric:`.
  Rationale: the file's load-bearing part is the judge `## Criteria`; `## Summary` is preamble. "template"
  implied a fill-in mold it isn't.
- **`<Type>/Rulebook/rules.md` → `<Type>/Rulebook.md`.** Kill the single-file folder; file name now matches
  `docType: rulebook`. Justified by the repo's own YAGNI ("add the folder on the *second* file" — which never came).
- **Naming principle (why the two file names differ).** Name a file by the *discriminator its location leaves
  open*. In `tests/Rubrics/` the folder fixes the role, so the **type** discriminates → `<Type>.md`. In a
  `<Type>/` folder the type is fixed, so the **role** discriminates → `Rulebook.md`. Same principle, opposite axis.
- **Converge the type-folder shape:** drop central's inner `Tests/` and the `.Tests` namespace suffix →
  central type namespace becomes `ABox.Tests.<Type>`, test `.cs` sits directly in `<Type>/` (like co-located).
- **Support stays LOCAL (decision B).** Per-type `<Type>/Support/`; an owner/assembly-root `Support/` is only a
  *promotion target* on a genuine second consumer. **Do NOT hoist central Support to one root.**

## Target shape — identical in both homes

```
<assembly-root>/                 # tests/Tests/  (central)  |  src/.../Tests/  (co-located)
├── <Assembly>.csproj
├── Support/                     # ONLY if a helper is shared across types (promotion target)
└── <Type>/
    ├── *.cs                     # test code, directly here
    ├── Support/                 # type-private helpers, if any
    └── Rulebook.md
```

The **only** residual difference: central has no owner-root `Support/` (its types — Arch/Structure/Docs —
share nothing); co-located may (its types share a feature). That is **usage-driven, not structural**, and correct.

## Why Support stays local (B), not one root (A)

"Support" is a different thing in each home. Central's types are **disjoint kinds of analysis**
(`ArchitectureModel` is meaningless to Structure; `SourceTree` to Arch) — helpers never cross types, so
support is naturally **type-scoped**. A feature's types share the feature, so `TempGitRepo` serving Git
Unit *and* E2E is naturally **owner-scoped**. Forcing one root `Support/`:

- **junk-drawers** three disjoint helper sets into one namespace (`ABox.Tests.Support`);
- **destroys information** — today a file's location encodes its scope (`Structure/Support/SourceTree.cs` is
  obviously Structure-only); flattening loses that;
- **scales badly** — name collisions get likelier, refactors touch a shared folder;
- buys only **cosmetic** shape-uniformity that *hides* a real semantic difference.

B is the repo's own stated rule (CLAUDE.md: *"Test doubles live with the test that uses them; promote to a
shared location only when genuinely reused."*). One principle, two homes; location encodes scope.

> Meta: the central/feature **partition** (a hard cap on which types may be central) was over-engineering and
> was rightly killed. Don't overcorrect into "everything must look identical" — the differences that flow from
> *ownerless-structural-suite* vs *per-feature-suite* are real. Erasing them is a different over-engineering.

## Action plan — 3 commits, each green & reviewable (all touch protected paths → on-branch review)

> **Cross-cutting (every commit that edits `governance/protected-paths`):** `.github/CODEOWNERS` is **generated**
> from it (`governance/generate-codeowners.sh`) and **CI fails if out of sync** (`.github/workflows/ci.yml`).
> Never hand-edit CODEOWNERS — after editing the policy, run `./governance/generate-codeowners.sh` and commit it.

### C1 — `template` → `rubric` (coordinated doctype-schema + instance migration; atomic — red if split)
- `tools/doc-engine/doctypes/test-template.yaml` → `rubric.yaml`; `docType: test-template` → `rubric`
- `tools/doc-engine/doctypes/rulebook.yaml`: required attr `template:` → `rubric:` — a **schema change**; the 15
  rulebooks below MUST migrate in the *same* commit or the `Docs` type goes red between steps
- `tests/Templates/` → `tests/Rubrics/`; 7 files → `<Type>.md`; each `docType: test-template` → `rubric`
- the 7 rubric bodies say "Enforced in `<Type>/Tests/`" — fix that prose (the central three also go stale in C3)
- 15 rulebooks: front-matter `template: …` → `rubric: …/<Type>.md`
- `tools/doc-engine/Tests/Unit/DocEngineValidationTests.cs` — reject-path test hardcodes the `template:` attr
  string (≈ lines 44, 47); update to `rubric:` (the `"test-template"` grep misses it — the literal is `template`)
- `governance/protected-paths` line 30 (`tests/Templates/**` → `tests/Rubrics/**`, + description) → **regen CODEOWNERS**
- both skills, `tests/README.md`, `tests/Tests/README.md`, `tools/doc-engine/README.md`, and `tests/Harness/README.md`
  (it documents the old "folder holds `template.md` + `rules.md`" shape as the stability contract — load-bearing)
- `TestTypes.cs:10` comment ("rulebook/test-template enum")
- regenerate `src/Api/doc-catalog.json` (build-gated staleness check, `DocEngineTests.Shared_catalog_is_current`)
- **Gate:** `dotnet test dirs.proj` green; `docengine check` passes

### C2 — `Rulebook/rules.md` → `Rulebook.md` (collapse single-file folder) — HIGHEST RISK
The "Rulebook is a **folder**" assumption is duplicated across **four** files. Miss one and co-located parity
passes *vacuously*:
- `tests/Harness/TestTypes.cs` `RulebookPath`: `{type}/Rulebook/rules.md` → `{type}/Rulebook.md`
- `tests/Harness/ParityGuard.cs` `ColocatedRulebook`: `Path.Combine(sourceDir, type, "Rulebook", "rules.md")` → `…, "Rulebook.md")`
- `tests/Harness/RepoTree.cs`: `CentralRulebooks` / `FeatureRulebooks` / `HasRulebookSubfolder` / `RulebookFolders`
  from `Rulebook` *folder* enumeration → `Rulebook.md` *file* (+ check the `IsUnderFeatureTests` consumer)
- **`tests/Harness/Tests/CoverageTests.cs:42`** — `Directory.Exists(Path.Combine(d, "Rulebook"))`. ⚠️ This is the
  **live co-located parity driver**. Left unchanged, the folder vanishes → the check goes false → `ForColocated(…).Assert()`
  stops running → **every feature's parity silently evaporates (vacuously green)**. Switch to `File.Exists(…"Rulebook.md")`.
- **`tests/Harness/Tests/TaxonomyTests.cs:103`** `IsRegisteredTypeWithRulebook` — same folder check; switch to the file
- `tools/doc-engine/Tests/Unit/DocEngineValidationTests.cs:12` — hardcodes the `Structure/Rulebook/rules.md` fixture path
- move all 15 files; fix each one's relative `rubric:` / `harness:` paths (one level shallower)
- `governance/protected-paths` lines **23, 24, 25** (`tests/**/Rulebook/**`, `src/**/Tests/**/Rulebook/**`,
  `tools/**/Tests/**/Rulebook/**`) → `…/Rulebook.md` → **regen CODEOWNERS**; skills + READMEs; regenerate catalog
- **Gate:** suite green **AND anti-vacuity probe** — break ONE co-located rule (drop a `### ` in a feature
  `Rulebook.md`) and confirm **RED**. Proves `ForColocated` still runs; a green build alone does not.

### C3 — converge the type-folder shape (drop inner `Tests/` + suffix; keep Support local)
- `tests/Harness/TestTypes.cs` `Namespace`: `ABox.Tests.{type}.Tests` → `ABox.Tests.{type}` (`ParityGuard.For` +
  `ContainsTest` follow automatically)
- move the 3 central `<Type>/Tests/*.cs` → `<Type>/*.cs` and update each namespace (`…<Type>.Tests` → `…<Type>`):
  `Arch/Tests/RuleTests.cs`, `Structure/Tests/StructureTests.cs`, `Docs/Tests/DocEngineTests.cs`
- **keep** central `<Type>/Support/` where it is (do NOT hoist)
- fix every stale reference to the old `ABox.Tests.<Type>.Tests` / `<Type>/Tests/` form: `TestTypes.cs` comments
  (lines 6, 31-33, 46), `tests/Harness/Tests/TaxonomyTests.cs:48` (assertion *message*),
  `tests/Harness/Tests/Suites.cs:8` (comment), `tests/Harness/README.md`, `tests/Tests/README.md` (links to
  `Arch/Tests/RuleTests.cs` etc.), and the 3 central rubric bodies ("Enforced in `<Type>/Tests/`")
- **Verified safe — no edit needed** (agent-checked): ArchUnitNET filters on assembly *file name* `.Tests.dll`,
  not namespace (`ArchitectureModel.cs:28`); `ABox.Tests.Central.csproj` uses default compile globs; `ABox.slnx`
  references csproj paths only; no namespace collision (`ABox.Tests.{Arch,Structure,Docs}` unused; `SuiteAnchor`
  is the `ABox.Tests` parent)
- **Gate:** IDE0130 clean (namespaces match new folders); suite green
- Note: after dropping the suffix, `<Type>/Support` (ns `ABox.Tests.<Type>.Support`) falls *inside* the type's
  parity scope — verified harmless (Support carries no `[Rule]`/`[Fact]`), and it's exactly what co-located already does.

## Parked / follow-on — do not lose

- **SSOT — DONE (by removal, not a guard).** The list was duplicated 3× (`TestTypes.Registered` + both doctype
  `testType` enums). Rather than police three copies, we removed the doc-engine's: the doc-engine never needed the
  list (it validates *shape*; the type is the folder, and its code never reads `testType`). Both doctypes now make
  `testType` a plain `type: string`; `TestTypes.Registered` is the **sole** source; a harness `[Fact]`
  (`EveryRulebookDeclaresItsFolderAsTestType`) pins each rulebook's `testType` to its folder (and makes the old
  judge-only `named-type` rubric mechanical). The reject-path doc-engine test retargeted to `feature-plan`'s
  `status` enum. **Contract assumption:** the catalog's `testType` enum was treated as non-published (no in-repo
  consumer binds it; the external client was deemed not to need the value-set). Revisit if the client breaks.
- **Harness as a registered type.** Whether the harness's own tests become a `harness` type (uniform Rulebook,
  doc-engine-validated, self-parity). Verdict from analysis: a **values call** (category clarity vs uniform
  anti-drift), **leaning yes** *after* the renames + SSOT land — the renames remove the template/rules confusion
  and SSOT makes "add a type" a one-place edit. Cost: add `harness` to both doctype enums (protected) + author a
  Harness rubric + reframe the "enforcer is not a type" docs. Note: "the enforcer cannot check itself" was never a
  rule — `Suites.cs` only *described* that it didn't; self-checking is wanted. The narrow real constraint is only
  "don't route the harness assembly through the `LoadFrom` discovery gate" (identity hazard) — direct self-parity
  respects it.
- **Restore lean self-parity** (harness eats its own dogfood) — folds into the Harness-as-type item above.
- **Bigger question (parked as possible ADR):** is the whole location/discovery model over-built — `For` vs
  `ForColocated`, the `TestsSourceDir` stamp, the `Suites` `LoadFrom` dance? The friction this cleanup surfaced is
  weak evidence it might be. Not now.

## Validation record

Three agents validated this plan against the codebase (coverage sweep, mechanics/breakage, cold-readability).
Their findings are folded into the touch-lists above. The load-bearing ones:

- **The vacuous-pass trap (C2).** `CoverageTests.cs:42` + `TaxonomyTests.cs:103` gate co-located parity on a
  `Rulebook` *folder* existing — outside `RepoTree`. Miss them and every feature's parity goes green-but-dead.
  Hence the C2 anti-vacuity probe (break a real rule, expect red).
- **CODEOWNERS is generated** and CI-gated — regen after every `protected-paths` edit (cross-cutting note above).
- **C1 is a coordinated schema+instance migration**, not a cosmetic rename — doctype attr and all 15 instances
  move together or the build is red between steps.
- **C3 is safer than feared:** ArchUnitNET, the csproj globs, `ABox.slnx`, and namespace uniqueness were all
  verified to need no change; only stale strings/comments and the 3 file moves remain.

# Test-harness review — action plan

From a 3-agent review (structure / SSOT-reuse-conflicts / flaky-magic-values). One card per finding.
Format: **What · Why · Fix · Expect**. Tier 1 = land now (small, low-risk). Tier 2 = deliberate (bigger / has constraints).

Status: proposed. Verified live: `DocEngine.cs` `-c Debug` claim downgraded (BuildOnce builds Debug fresh → not stale/explode; just wrong-config + extra build).

---

## Tier 1 — land now

### T1 — Central csproj duplicates TestProject.props
- **What:** `tests/Central/ABox.Tests.Central.csproj:9-22` hand-lists coverlet / Test.Sdk / xunit / xunit.runner + the `Using` block; every co-located stub `<Import>`s `tests/TestProject.props` instead.
- **Why:** last central-vs-co-located asymmetry; package versions can drift between central and the rest, unguarded.
- **Fix:** add `<Import Project="..\TestProject.props" />`; delete the duplicated `PackageReference`/`Using` items; keep central-only extras (ArchUnitNET x2, Mvc.Testing, AspNetCore FrameworkReference, the src glob).
- **Expect:** central references the same pinned versions as every suite; warning-free build; full suite green.

### T2 — `Catalog.Files` passes vacuously on a missing dir
- **What:** `tools/doc-engine/Catalog.cs:39-41` returns `Array.Empty` when a catalog dir is absent; `LoadBlocks`/`AllDoctypes` then return `{}` and validation passes with nothing loaded.
- **Why:** a renamed/missing `blocks/`·`doctypes/`·`kinds/` → vacuous green, against the repo's fail-loud discipline (`RepoTree.RequireDir`).
- **Fix:** make the catalog loaders fail loud when their dir is missing or yields zero defs (throw with the path), mirroring `RequireDir`.
- **Expect:** rename `blocks/` → `docengine check` fails with a clear "no blocks at <path>"; a doc-engine unit test covers the reject path.

### T3 — `TestsSourceDir` key defined twice
- **What:** `private const string TestsSourceDirKey = "TestsSourceDir"` in BOTH `ParityGuard.cs:36` and `Suites.cs:16`.
- **Why:** two copies of one metadata key; rename one → the other goes silently stale (Suites returns empty → confusing failure blamed elsewhere).
- **Fix:** one shared const (on `Suites`, referenced by `ParityGuard`) — both are in the same assembly.
- **Expect:** single definition; suite green.

### T4 — `DocInstances.SkipDirs` hand-copies build-output names
- **What:** `tests/Central/Docs/Support/DocInstances.cs:10` literal `{".git","bin","obj","artifacts","prototype"}`; `RepoTree.cs:16` `BuildOutputDirs={bin,obj,artifacts}` claims (`:13` comment) to be "owned once."
- **Why:** third reader copied the values → drift; RepoTree's ownership comment is now false.
- **Fix:** `SkipDirs = RepoTree.BuildOutputDirs ∪ {".git","prototype"}` (DocInstances already uses `RepoTree`).
- **Expect:** one source for the output-dir names; suite green.

### T5 — `TaxonomyTests` hardcodes `"Support"`
- **What:** `tests/Harness/Tests/TaxonomyTests.cs:102` `Path.GetFileName(d) != "Support"` instead of `TestTypes.IsNonType`.
- **Why:** reuse miss; the literal can drift from `TestTypes.NonType` (the declared source).
- **Fix:** `!TestTypes.IsNonType(Path.GetFileName(d)!)`.
- **Expect:** behaviour identical; one source for non-type folder names; suite green.

### T6 — Stale paths/filenames in docs
- **What:** `tests/Central/Docs/Rulebook.md:16` (`tests/**/Rulebook/`), `tests/Harness/Tests/Rulebook.md:16` (`tests/Tests/`), `tests/Rubrics/Unit.md:7` (`Unit/Tests/`), `tests/Harness/README.md:48` (`Rule.cs`/`LiveFact.cs` → now `RuleAttribute.cs`/`LiveFactAttribute.cs`).
- **Why:** describe a layout that no longer exists; mislead the next reader.
- **Fix:** correct each to the current layout (`tests/Central/`, no `Rulebook/` dir, no nested `/Tests/`, real filenames).
- **Expect:** no stale path/filename in the live test docs (rubric/Rulebook Whys self-parity-safe — Whys aren't parsed).

---

## Tier 2 — deliberate (bigger / constrained)

### T7 — "Is this a test assembly?" encoded 3 incompatible ways
- **What:** `Suites.cs:40` (name `ABox.*`+`.Tests`) · `ArchitectureModel.cs:28` (dll-name infix `.Tests.`) · `dirs.proj:11`/central csproj (path `**/Tests/**`). Different rules, no guard on agreement.
- **Why:** latent — a renamed/off-pattern assembly slips a different net in each; the three can diverge silently.
- **Fix:** extract ONE predicate to the shared harness lib (`tests/Harness/`, referenced by both central and harness-tests); have `Suites` and the Arch filter call it; add a structure/harness guard that every built `*.Tests.dll` satisfies it. (dirs.proj path-glob stays — it's build-time, can't call code.)
- **Expect:** one definition of "test assembly"; a guard fails if the encodings disagree. Constraint to check: cross-assembly reference direction.

### T8 — Two independent parsers of one Rulebook.md
- **What:** `ParityGuard.cs:11` scans literal `"### "`; the doc-engine validates the same file via `InstanceParser` regex. Nothing asserts they agree.
- **Why:** a heading the engine accepts but `"### "` (trailing space, no trim) rejects → silent parity hole.
- **Fix:** CANNOT reuse the engine parser (ADR-0015: harness no-dep on doc-engine). Options: (a) a Docs/harness test that shells the engine and asserts its rule count == ParityGuard's `### ` count on a real Rulebook; (b) accept the dual-parse as deliberate and document it + hoist the heading literal to one const.
- **Expect:** either a guard proving the two parsers agree, or an explicit documented decision. Constraint: ADR-0015.

### T9 — `Suites.Colocated()` is build-state-fragile
- **What:** `Suites.cs:20-33` reconstructs `artifacts/bin/<config>/<Project>.dll` and `LoadFrom`s; couples correctness to a complete, same-config, same-version build (F2/F3). `ArchitectureModel.cs:26` similarly `LoadFrom`s `ABox.*.dll` from bin (stale after rename-without-clean).
- **Why:** partial/incremental build → false "unbuilt" failures or vacuous green; stale DLL pollutes the arch model.
- **Fix:** discover DLLs by recursive glob keyed on `TestsSourceDir`, not path reconstruction; wrap `GetTypes()` for `ReflectionTypeLoadException` with a readable message. (Arch stale-DLL: document the clean discipline or derive the assembly set from the solution — separate, harder.)
- **Expect:** discovery tolerates layout/config variance; a missing DLL gives a named error, not a vacuous pass.

### T10 — `DocEngine.cs` config pin + no timeout
- **What:** `tests/Central/Docs/Support/DocEngine.cs:19,24` hardpin `-c Debug`; `:43` `WaitForExit()` has no timeout.
- **Why:** exercises the engine in a different config than the suite + an extra Debug build; a hung engine deadlocks `dotnet test` with no diagnostic.
- **Fix:** derive config from `AppContext.BaseDirectory` (as `Suites` does) or drop the `-c`/`--no-build` pin; add `WaitForExit(timeout)`.
- **Expect:** engine runs in the suite's config; a hang fails with a timeout message, not a deadlock.

---

## Out of scope (noted, not fixed)
- `"Rulebook.md"` literal in ~7 sites — fails loud on mismatch; optional `const`. Low.
- doc-engine `ResolveRoot` (CWD) vs `RepoTree` (BaseDirectory) — divergent but works; could pass `--root <RepoTree.Root>`. Low.
- `Registered` set, `<Assembly>.<Type>` namespace convention — single-source + guarded. GOOD, leave.

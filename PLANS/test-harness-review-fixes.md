# Test-harness review — action plan (validated)

From a 3-agent review + a thermonuclear adversarial pass against this plan. One card per finding: **What · Why · Fix · Expect**.
Tiering is by **code-risk**, not process. Note: nearly every item touches a `critical` protected path
(`tests/Harness/**`, `tests/**/Rulebook.md`, `tests/Rubrics/**`) — but we are already inside the owner-reviewed
PR #105, so that's the existing review gate, not new friction. Unprotected: T1 (`ABox.Tests.Central.csproj`),
T2 (`tools/doc-engine/*.cs` root).

**Ordering:** T3 lands before/with T7 (same files). Everything else independent.

---

## Tier 1 — low code-risk, thermonuclear-verified SAFE

### T1 — Central csproj duplicates TestProject.props
- **What:** `tests/Central/ABox.Tests.Central.csproj:16-28` duplicates props' coverlet / Test.Sdk / xunit / xunit.runner + the `<Using>` block (`tests/TestProject.props:13-22`).
- **Why:** last central-vs-co-located asymmetry; package versions can drift, unguarded.
- **Fix:** `<Import Project="..\TestProject.props" />`; delete the duplicated `PackageReference`s **and** the duplicate `<Using Include="ABox.Tests.Harness"/>` / `<Using Include="Xunit"/>` (props already has them — leaving them = duplicate global-using = warnings-as-errors break). Keep central-only extras (ArchUnitNET x2, Mvc.Testing, AspNetCore FrameworkReference, src glob, RootNamespace, TestsSourceDir).
- **Expect:** same pinned versions everywhere; warning-free build; full suite green.

### T2 — doc-engine catalog passes vacuously on a missing dir  *(understated — worse than first thought)*
- **What:** `tools/doc-engine/Catalog.cs:39-41` returns empty for a missing dir. `SchemaChecker.Run` nests the blocks/doctypes loops **inside** the kinds loop → a missing `kinds/` makes `check` validate **zero** of everything and return 0 = PASS.
- **Why:** renamed/missing `blocks`·`doctypes`·`kinds` → vacuous green, against fail-loud discipline (`RepoTree.RequireDir`).
- **Fix:** fail loud in the **loaders / SchemaChecker** (not `Catalog.Files` — it's the generic primitive that also reads floor/kind paths): throw when `blocks`(14)/`kinds`(2)/`doctypes`(4) yield zero. None is legitimately empty. (Per-doctype block lists CAN be empty — that's `LoadDoctype` on a single file, unaffected.)
- **Expect:** rename `blocks/` → `docengine check` fails "no blocks at <path>"; a reject-path case in `tools/doc-engine/Tests/Unit/` (suite already exists — add a case, don't stand one up).

### T3 — `TestsSourceDir` key defined twice  *(land with/before T7)*
- **What:** `private const string TestsSourceDirKey = "TestsSourceDir"` in both `tests/Harness/Tests/ParityGuard.cs:36` and `tests/Harness/Tests/Suites.cs:16`.
- **Why:** two copies of one metadata key; rename one → the other silently stale.
- **Fix:** one shared const on `Suites`, referenced by `ParityGuard` (same assembly — verified reachable).
- **Expect:** single definition; suite green.

### T4 — `DocInstances.SkipDirs` hand-copies build-output names
- **What:** `tests/Central/Docs/Support/DocInstances.cs:10` literal `{.git,bin,obj,artifacts,prototype}`; `RepoTree.cs:16` `BuildOutputDirs` (`:13` comment claims "owned once" — now false).
- **Why:** third reader copied the values → drift.
- **Fix:** `SkipDirs = RepoTree.BuildOutputDirs ∪ {.git,prototype}` (`BuildOutputDirs` is `public static`; Central refs the lib — reachable).
- **Expect:** one source for output-dir names; suite green.

### T5 — `TaxonomyTests` hardcodes `"Support"`
- **What:** `tests/Harness/Tests/TaxonomyTests.cs:102` `Path.GetFileName(d) != "Support"` instead of `TestTypes.IsNonType`.
- **Why:** reuse miss; literal can drift from `TestTypes.NonType`.
- **Fix:** `!TestTypes.IsNonType(Path.GetFileName(d)!)` (same assembly — reachable).
- **Expect:** behaviour identical; one source; suite green.

### T6 — Stale paths/filenames in docs
- **What:** `tests/Central/Docs/Rulebook.md:16` (`tests/**/Rulebook/`), `tests/Harness/Tests/Rulebook.md:16` (`tests/Tests/`), `tests/Rubrics/Unit.md:7` (`Unit/Tests/`), `tests/Harness/README.md:45,48` (`Rule.cs`/`LiveFact.cs` → `RuleAttribute.cs`/`LiveFactAttribute.cs`).
- **Why:** describe a layout that no longer exists.
- **Fix:** correct each. (All four sit in prose / `**Why:**`, not `### ` headers — parity unaffected.)
- **Expect:** no stale path/filename in live test docs.

### T11 — ADR-0015 `[det]` guard misses the harness-tests csproj  *(MISSED by v1; found by review)*
- **What:** `SourceTree.HarnessForbiddenReferences` (StructureTests `HarnessTakesNoDependencyOnTheDocEngine`) scans only `tests/Harness/ABox.Tests.Harness.csproj`; the `[det]` rule covers `tests/Harness/**` (whole tree), so `tests/Harness/Tests/ABox.Tests.Harness.Tests.csproj` could add an `ABox.DocEngine` ref undetected.
- **Why:** the no-dep invariant has a hole exactly where T7/T8 would be tempted to add an engine ref.
- **Fix:** extend `HarnessForbiddenReferences` to cover every `*.csproj` under `tests/Harness/`.
- **Expect:** a doc-engine ProjectReference in either harness csproj fails the Structure rule (mutation-probe it).

### T12 — `ParityTests` hardcodes its own assembly name  *(MISSED by v1)*
- **What:** `tests/Harness/Tests/ParityTests.cs:20` passes literal scope `"ABox.Tests.Harness.Tests"` to `ForRulebook` — duplicates the assembly name.
- **Why:** same drift class T3/T5 target; rename-fragile.
- **Fix:** `typeof(ParityTests).Assembly.GetName().Name!`.
- **Expect:** rename-safe; one source; self-parity still green + mutation-probes.

### T10 — `DocEngine.cs` config pin + no timeout  *(two-site coupling)*
- **What:** `tests/Central/Docs/Support/DocEngine.cs:19` (`run … --no-build -c Debug`), `:24` (`build … -c Debug`), `:43` `WaitForExit()` no timeout.
- **Why:** exercises engine in a different config than the suite + extra Debug build; a hung engine deadlocks `dotnet test` silently. (Not stale/explode — `BuildOnce` builds Debug fresh.)
- **Fix:** derive config from `AppContext.BaseDirectory` (reliable — it's the *test assembly's* output dir, independent of the spawned process's `WorkingDirectory`); change **both** `:19` run-`-c` and `:24` build-`-c` together (mismatch → "no <config> binary"); add `WaitForExit(timeout)`.
- **Expect:** engine runs in the suite's config; a hang → timeout message, not deadlock.

---

## Tier 2 — redesign / deliberate

### T7 — "Is this a test assembly?" encoded 3 ways  *(reframed — NOT one predicate)*
- **What:** `tests/Harness/Tests/Suites.cs:40` (`ABox.*`+`.Tests`, FEATURE only, excludes Central) · `tests/Central/Arch/Support/ArchitectureModel.cs:28` (`!Contains(".Tests.")`, excludes ALL test dlls incl. Central) · `dirs.proj:11`/central csproj (path `**/Tests/**`).
- **Why:** three encodings, no guard on agreement — but they answer **different questions** (Arch excludes Central; Suites excludes Central too but via a different rule).
- **Fix:** NOT one predicate (collapsing breaks the arch prod graph). **Two named predicates** in the shared lib (`tests/Harness/`): `IsTestAssembly` (Arch's exclusion) and `IsFeatureTestAssembly` (Suites/Coverage). Arch + Suites each call the right one; add a guard that every built `*.Tests.dll` classifies consistently. dirs.proj path-glob stays (build-time). Direction OK (both Central and Harness.Tests ref the lib; no cycle).
- **Expect:** two definitions, each used once; a guard catches divergence. *Closer to a small redesign than a refactor — schedule deliberately.*

### T9 — `Suites.Colocated()` build-state-fragile
- **What:** `Suites.cs:20-33` reconstructs `artifacts/bin/<config>/<dll>` + `LoadFrom`; `ArchitectureModel.cs:26` `LoadFrom`s `ABox.*.dll` from bin (stale after rename-without-clean).
- **Why:** partial/incremental/cross-config build → false "unbuilt" or vacuous green; stale DLL pollutes the arch model.
- **Fix:** discover DLLs by glob keyed on `TestsSourceDir` **and filtered to the running config** (recursive glob hits Debug+Release — must dedup by config/identity or `LoadFrom` clashes); wrap `GetTypes()` for `ReflectionTypeLoadException` with a named message. Arch stale-DLL = separate (clean discipline or derive from solution).
- **Expect:** discovery tolerant of layout/config; missing DLL → named error, not vacuous pass.

---

## Killed / out of scope
- **T8 (two Rulebook parsers) — DROPPED.** Divergence window is `### ` vs `###\t`/`###  ` (engine `\s+` accepts, ParityGuard `StartsWith("### ")` rejects); a real Rulebook never uses a tab heading. Option (b) (doc it) is unfalsifiable; option (a) (shell-and-count cross-check) is real but low ROI for a protected-path change. Revisit only if a heading-style bug actually appears.
- `"Rulebook.md"` literal in ~7 sites — fails loud on mismatch; optional `const`. Low.
- doc-engine `ResolveRoot` (CWD) vs `RepoTree` (BaseDirectory) — works; could pass `--root`. Low.
- `Registered` set, `<Assembly>.<Type>` namespace — single-source + guarded. GOOD, leave.

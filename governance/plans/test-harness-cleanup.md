# Test-harness cleanup — action plan

Cold read. Follow-up to PR #32 (`refactor(tests)`: Meta type + two-file Rulebooks).
Behavior-preserving structural fixes only. All paths relative to repo root.

Owners of the convention `type "Arch"` ↔ `ns ABox.Tests.Arch.Tests` ↔ `path Arch/Rulebook/rules.md`:
- `tests/Harness/TestTypes.cs`
- `tests/Harness/ParityGuard.cs`
- `tests/Tests/Meta/Tests/ParityTests.cs`
- `tests/Harness/README.md:44` (doc mirror)

---

## 1. Collapse the type↔namespace↔path convention into TestTypes

- **What:** The convention is encoded 3×. `Meta/ParityTests` builds `$"ABox.Tests.{type}.Tests"`; `ParityGuard.DeriveRulebookPath` splits it back to recover `type`; an `ArgumentException` guards a shape the only caller machine-generates (unreachable). Data round-trips `type → string → type`.
- **Why:** One convention, one owner. The round-trip + dead validation read as robustness but are pure indirection.
- **Change:**
  - `TestTypes.cs` — add:
    ```csharp
    public static string Namespace(string type)    => $"ABox.Tests.{type}.Tests";
    public static string RulebookPath(string type) => $"{type}/Rulebook/rules.md";
    ```
  - `ParityGuard.cs` — `For(Assembly, string type)` calls `TestTypes.Namespace/RulebookPath`; store `scope` + `rulebookPath` in ctor; `Assert` uses stored path. **Delete `DeriveRulebookPath` and its throw.**
  - `Meta/Tests/ParityTests.cs` — `ParityGuard.For(asm, type)` (drop the `$"ABox.Tests.{type}.Tests"` build).
  - `Harness/README.md:44` — update the example call to match.
- **Expected result:** `DeriveRulebookPath` + unreachable `ArgumentException` gone; namespace template exists once; no string round-trip. Tests stay green.

## 2. Express ContainsTest via the same template (kill the third parse)

- **What:** `TestTypes.ContainsTest` parses `parts[]` to test "ns is this scope or a sub-namespace"; `ParityGuard.InScope` is the identical predicate via `StartsWith(scope + ".")`. Two hand-rolled copies of one rule.
- **Why:** After #1, `Namespace(type)` exists — both directions of the convention should lean on it.
- **Change:** `TestTypes.cs`:
  ```csharp
  public static bool ContainsTest(string? ns) =>
      ns is not null && Registered.Any(t =>
          ns == Namespace(t) || ns.StartsWith(Namespace(t) + ".", StringComparison.Ordinal));
  ```
- **Expected result:** `parts[]` indexing in `ContainsTest` gone; predicate matches `InScope`; `Meta/TaxonomyTests` "Every test lives inside a registered test type" stays green.

## 3. Hoist the build-output dir names to one owner

- **What:** `Ignored = { "bin","obj","artifacts" }` is duplicated in `RepoTree.cs` and `SourceTree.cs`. All uses (filter in `RepoTree.TestTypeFolders` + `SourceTree.NotIgnored`, hunt target in `SourceTree.IsOutputRoot`) rest on one canonical fact: these are the build-output dir names.
- **Why:** One fact, one owner. `SourceTree.Root` already = `RepoTree.Root`, so the dependency edge exists — referencing a shared constant adds no coupling. A 4th build-output dir name would otherwise need editing in two places.
- **Change:** `RepoTree.cs` — expose `public static readonly string[] BuildOutputDirs = { "bin", "obj", "artifacts" };` (matches the `TestTypes.Registered` idiom). Delete `SourceTree.Ignored`; point its three call sites + `RepoTree.TestTypeFolders` at `RepoTree.BuildOutputDirs`.
- **Expected result:** One definition of the build-output names; `Structure` + `Meta` structure guards stay green.

## 4. Promote the Bullets reporter to Harness

- **What:** `Bullets(IEnumerable<string>)` is copy-pasted in `Structure/Tests/StructureTests.cs`, `Meta/Tests/TaxonomyTests.cs`, `Meta/Tests/RulebookFormatTests.cs` (latter also dups `Rel`, `Join`).
- **Why:** Third consumer = genuine reuse (repo rule: promote doubles on real second use). Drifts silently otherwise.
- **Change:** Add `tests/Harness/Report.cs` with `Bullets` (+ `Join`); keep `Rel` in `RulebookFormatTests` (it binds `RepoTree.Root`, single consumer). Replace the 3 local copies.
- **Expected result:** One reporter; three local copies deleted.

## 5. Split the harness's own test suite (Meta) into its own assembly

- **What:** Meta lives in the product suite (`tests/Tests/`) and reflects over its *own* assembly: `ParityTests` → `ParityGuard.For(typeof(ParityTests).Assembly, …)`, `TaxonomyTests` → `typeof(TaxonomyTests).Assembly.GetTypes()`. That only works because Meta is co-located with every product test. Move it to its own assembly so the test-system's guards observe the suite from *outside*, the way Arch guards observe `src`.
- **Why:** Repo philosophy is validator-outside-the-validated (Arch over `src`, "can't be dodged"). Meta currently validates from *inside* the bag it checks. The split makes `engine → tests` symmetric with `tests → src`, and de-special-cases the parity loop: the remaining `Registered` set is homogeneous (all six fit `ABox.Tests.<Type>.Tests`), Meta stops being the odd member.
- **Naming:** NOT `engine` — the engine *is* `Harness` (ParityGuard/RulebookFormat/RepoTree/TestTypes). The thing being split out is the harness's own test suite. Name it `Meta` (keep) or `Harness.Tests`.
- **Change:**
  - New `tests/Meta/ABox.Tests.Meta.csproj` (`IsTestProject` true; references `Harness` + a handle to the product assembly so reflection sees it — `ProjectReference` to `ABox.Tests`, or guarded `Assembly.Load("ABox.Tests")` that throws on not-found, never vacuous-green). Move the three Meta test classes + `Meta/Rulebook/`.
  - `ParityTests`/`TaxonomyTests` — reflect over the product assembly via that handle, not `typeof(this).Assembly`. Engine's own rulebook parity becomes one explicit `ParityGuard.For(<this assembly>, …)` call.
  - `RepoTree.RulebookFolders()` is hardwired to `tests/Tests/*` — Meta's Rulebook now sits outside it. Engine self-checks its own rulebook via an explicit path; `RepoTree` stays product-scoped.
  - `TestTypes.Registered` — drop `"Meta"`. The convention triple now applies to product types only; engine is a bespoke one-off outside the template.
- **Expected result:** Validator sits outside the suite it validates; parity loop has no Meta exception; three assemblies (`Harness` lib, `Tests` product suite, `Meta`/`Harness.Tests` self-suite) with dependency direction `Meta → Tests → Harness` and `Tests → src`.
- **Sequencing:** Do **after** #1–#2 (or fold in) — both touch `TestTypes`/`ParityGuard`, and #1's "one template for all types" assumption changes once Meta stops being a `tests/Tests/<Type>` member. This is the one item with a real cost (cross-assembly handle + `RepoTree` ripple); skip it if the validator-outside property isn't worth that — the cheaper alternative is to keep two assemblies and merely stop treating Meta as a peer product type.

---

## Out of scope (decided, no change)

- `ParityGuard.For` keeping an explicit `Assembly` param — kept: it's what keeps the guard honest about where it reflects.

## Done-when

- `dotnet build ABox.slnx` warning-free; `dotnet test ABox.slnx` green.
- #1–#4 land as one coherent commit (the convention collapse + dedup). #5 is its own commit (assembly boundary) — or dropped if validator-outside isn't worth the cost.

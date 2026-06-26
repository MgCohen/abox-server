# test-colocation — follow-ups & pending work

Tracks what's left after the test-colocation implementation (PR #96). Updated 2026-06-26.

## Status

- **Implementation: complete and green.** Phases 0→5 of [`test-colocation.md`](test-colocation.md) are built.
  Full suite **275 passed + 14 Live skipped** across 10 assemblies; `dotnet build dirs.proj` + `ABox.slnx`
  warning-free; CI green on **ubuntu + windows + policy-guard**; rebased on current `main`.
- **Not on `main` yet.** The entire stack lives only on branch `claude/doc-engine-test-org-oycmbd` /
  **PR #96**. `main` still has the single `tests/Tests/ABox.Tests.csproj`, `dotnet test ABox.slnx`, and none
  of `dirs.proj` / `Templates/` / `Fixtures/` / `ForColocated` / `CoverageTests` / co-located assemblies.
- **Blocking merge:** only the required `@MgCohen` code-owner review (critical-path wall). Nothing technical.

## 🟡 Should do (correctness / honesty — non-blocking, do before or just after merge)

- [ ] **#4 Flip plan statuses.** `PLANS/test-colocation.md` still reads "🟡 proposed — not yet built"; mark it
      shipped (date, branch, suite numbers). Mark `PLANS/test-structure.md` superseded-by test-colocation.
- [ ] **#5 Reword the Meta parity Rule.** `tests/Meta/Rulebook/rules.md` → "Parity holds for every registered
      type" + its prose still describe iterating **all** registered types. The test now iterates only the
      **centrally-present** types (Arch/Structure/Docs); the co-located types are policed by `CoverageTests`.
      Reword the Rule so header/prose match behavior (parity keeps them honest).
- [ ] **#6 Finish the `tests/Harness/README.md` rewrite.** Currently a banner + the old per-type
      `tests/Tests/<Type>` authoring walkthrough. Rewrite the walkthrough for the co-located layout
      (point at the `new-feature-tests` skill; templates central; `rules.md` co-located).

## 🟢 Deferred ideals (owner said "acceptable for now")

- [ ] **#7 Wire per-feature split + cross-process Host lock.** Wire is currently one `ABox.Host.Tests`
      assembly (safe: single assembly → the no-parallel Host-boot collection serializes). The ideal is
      `InboxWire → ABox.Inbox.Tests`, etc., which needs a cross-process lock so Host boots serialize across
      assemblies. This is the original deferred host-race decision.
- [ ] **#8 `Delay*` / `StubFlow` → Flows.** They live in `ABox.Agents.Tests` today (with their only
      consumers `FlowHarness`/`WireApp`, which kept it cycle-free). Moving to Flows adds an
      `Agents.Tests → Flows.Tests` reference. Cosmetic preference only.
- [ ] **#9 doc-engine test assembly.** Plan envisioned `tools/doc-engine/Tests/ → ABox.DocEngine.Tests`, but
      **the doc-engine has zero tests today** — so this is *write new tests for the doc-engine*, net-new work,
      not a missed migration. The `tools/**/Tests/**/Rulebook/**` protected glob already anticipates it.
- [ ] **#10 Add feature test projects to `ABox.slnx`.** Optional, IDE discoverability only — `dirs.proj`
      already discovers/runs them; `ABox.slnx` is intentionally the product/IDE solution.

## Notes for whoever picks this up

- The recipe for any new feature suite is the **`new-feature-tests`** skill (stub csproj, `Parity.cs`,
  per-type `rules.md` front-matter, the namespace-shadowing + `InternalsVisibleTo` gotchas).
- Central parity is driven by `tests/Meta` (`ParityTests` over central types + `CoverageTests` over every
  co-located assembly via `Suites.Colocated()`); a feature's own `Parity.cs` is the fast local signal.
- `Op`/`OpFlow` are central (feature-independent) in `ABox.Tests.Fixtures`; all other fixtures co-locate with
  their owning feature.

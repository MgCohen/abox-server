# Test Rulebooks → Artifacts — Migration Plan

> **Scope: tests only.** Reorganize the *test* system into the Artifact model, to
> prove the shape before scaling it to ADRs/plans/research. **Hard invariant: a
> warning-free build + green `dotnet test ABox.slnx` at the end of every phase, and
> zero loss of any current testing feature.** Each phase is one coherent commit and
> is independently revertible. Produced 2026-06-17.

## Goal

Move each test type's **definition** (`template.md` + `rules.md`) out of
`tests/Tests/<Type>/Rulebook/` into a per-artifact folder
`governance/artifacts/<Type>/`, while the test **code** stays in `tests/`. The
shared **engine** (`tests/Harness`) and **Meta** self-suite stay where they are for
now. Parity then bridges the new gap — definition in `governance/artifacts/`, proof
in `tests/` — which is exactly what a parity guard is for.

**Out of scope (later):** ADR/Plan/Research artifacts; relocating the engine to
`governance/harness/` (that rides with ADR 0013). This plan only reshapes tests.

## Target shape

```
governance/artifacts/
├── README.md              # what an Artifact is + the artifact.yml schema (the "main folder")
├── Arch/  { artifact.yml, template.md, rules.md }
├── Structure/  { … }
├── Unit/ E2E/ Wire/ Live/  { … }
└── Meta/  { artifact.yml, template.md, rules.md }

tests/
├── Harness/   ← UNCHANGED (the shared engine; relocates later, not here)
├── Meta/      ← keeps its Tests/ ; its Rulebook/ moves to governance/artifacts/Meta/
└── Tests/<Type>/
    ├── Tests/    ← UNCHANGED (the [Rule] facts — namespaces ABox.Tests.<Type>.Tests)
    └── Support/  ← UNCHANGED
    (Rulebook/ folder removed — its two files moved out)
```

Definitions flatten (no `Rulebook/` subfolder): the artifact folder *is* the rulebook.

## Features that must survive (the contract this plan is graded against)

| # | Feature today | Preserved by |
|---|---|---|
| F1 | **Parity 1:N** — every `### ` Rule has ≥1 `[Rule]` test; a bare test fails | repoint `ParityGuard.ProductRulebook` + the Meta self-rulebook path |
| F2 | **Rulebook format** — every Rule matches `template.md`; `rules.md` holds only pointers + Rules; every `template.md` has `## Criteria` | `RepoTree.RulebookFolders()` repointed; filenames unchanged |
| F3 | **Taxonomy** — every `tests/Tests/` folder is a registered type; every test lives in one | unchanged (code tree untouched) |
| F4 | **namespace = folder** (IDE0130 error) for test code | unchanged (code stays) |
| F5 | **Judges** — `/judge`, `/judge-rulebook`, `/judge-authoring` read on-disk paths | repoint the command/skill paths |
| F6 | **Protected-path coverage** of the definitions | `governance/**` already critical → continuous; add explicit `governance/artifacts/**` row |
| F7 | **`rules.md` = Template/Harness pointers + Rules only** | update the relative links in each `rules.md` preamble |
| F8 | **Derive-don't-hardcode / failure messages as fix instructions** | no behavior change; keep error text accurate to new paths |

## Coupling inventory (everything that hardcodes the old layout)

| Location | Assumes | Change |
|---|---|---|
| `Harness/RepoTree.cs` | `RulebookFolders()` = `tests/Tests/*/Rulebook` + `tests/Meta/Rulebook` | add `ArtifactsRoot = governance/artifacts`; `RulebookFolders()` enumerates `ArtifactsRoot/*` |
| `Harness/TestTypes.cs` | `RulebookPath(t)` = `"<t>/Rulebook/rules.md"` under `TestsRoot` | → `governance/artifacts/<t>/rules.md` (relative to `Root`). `Namespace()`/`Registered`/`ContainsTest()` **unchanged** |
| `Harness/ParityGuard.cs` | `ProductRulebook` = `TestsRoot/<t>/Rulebook/rules.md` | → `ArtifactsRoot/<t>/rules.md` |
| `Meta/Tests/ParityTests.cs` | Meta self-rulebook at `MetaRoot/Rulebook/rules.md` | → `ArtifactsRoot/Meta/rules.md` |
| `Meta/Tests/RulebookFormatTests.cs` | iterates `RepoTree.RulebookFolders()`, reads `template.md`/`rules.md` | **free** once `RulebookFolders()` repointed |
| `Meta/Tests/TaxonomyTests.cs` | `tests/Tests/` subdirs vs `Registered` | **free** (code tree unchanged) |
| `governance/protected-paths` | `tests/**/Rulebook/**` critical | drop that row; add `governance/artifacts/**`; keep `tests/Tests/{Arch,Structure}/**`, `tests/Harness/**`, `tests/Meta/**`; regenerate CODEOWNERS |
| `.claude/commands/judge*.md`, `.claude/skills/test-rulebook/SKILL.md` | `tests/Tests/<T>/Rulebook/...` paths | repoint to `governance/artifacts/<T>/...` |
| `tests/{README,Tests/README,Harness/README,Meta/README}.md`, `CLAUDE.md`, `PLANS/test-structure.md` | describe the Rulebook location | update prose + pointers |
| **csprojs** | — | **none** — `.md` are never referenced/compiled (verified); the move is build-safe |

## Phases

### Phase 1 — Move the definitions + repoint the engine (the core)
1. `git mv` each `tests/Tests/<T>/Rulebook/{template.md,rules.md}` → `governance/artifacts/<T>/` (Arch, Structure, Unit, E2E, Wire, Live), and `tests/Meta/Rulebook/*` → `governance/artifacts/Meta/`. Remove the empty `Rulebook/` dirs.
2. `RepoTree`: add `ArtifactsRoot`; rewrite `RulebookFolders()` to enumerate `ArtifactsRoot/*` (folders containing a `template.md`). Drop `MetaRoot` only if it falls unused, else keep as an existence assertion.
3. `TestTypes.RulebookPath` + `ParityGuard.ProductRulebook` + `ParityTests` Meta path → the new locations.
4. Fix the `rules.md` preamble links (the `template.md` sibling link stays; the Harness link still contains the substring `Harness/README.md` the guard checks). Update stale `tests/Tests/...` mentions in code comments.
5. **Gate:** build warning-free; `dotnet test` green; all Meta guards still run over the moved files.

### Phase 2 — Generalize the registry (additive)
1. Add `governance/artifacts/<T>/artifact.yml` per type (`home: tests/Tests/<T>`, `family: code-first`, `det/judge: true`, `parity: ABox.Tests` — `ABox.Tests.Meta` for Meta, `gate: block`).
2. Add `governance/artifacts/README.md`: what an Artifact is + the `artifact.yml` schema + how to add one.
3. Add `Harness/Artifacts.cs` — read the flat `artifact.yml` (hand-parse key:value; no new dependency, per the lean ethos) + a Meta guard **"every artifact folder is well-formed"**: has a valid `artifact.yml`, a `template.md` with `## Criteria`, a `rules.md` iff `family: code-first`, unique name, existing `home`. (Optional: derive `TestTypes.Registered` from the code-first artifacts to kill the duplication — defer if risky.)
4. **Gate:** build + test green; deliberately add a malformed `artifact.yml` and confirm the new guard goes red, then remove.

### Phase 3 — Repoint the surfaces
1. `governance/protected-paths`: drop `tests/**/Rulebook/**`; add `governance/artifacts/** | @MgCohen | critical`; run `generate-codeowners.sh`. (Protection is continuous — `governance/**` already covers the new home.)
2. Update `.claude/commands/judge*.md` + `.claude/skills/test-rulebook/SKILL.md` paths (rename the skill to artifact-oriented later — not now).
3. Update the four `tests/**/README.md`, the `CLAUDE.md` "Tests are Rulebooks" pointer, and note `PLANS/test-structure.md` as updated by this migration.

### Phase 4 — Verify zero feature loss (negative tests)
Run each guard *to red* and back, to prove nothing went vacuously green:
- delete a `[Rule]` test → **F1** parity red; restore.
- add a stray `##` to a `rules.md` → **F2** format red; restore.
- strip `## Criteria` from a `template.md` → **F2** red; restore.
- drop a junk folder in `governance/artifacts/` → **F4/registry** guard red; remove.
- `/judge-rulebook` a moved rulebook → resolves at the new path (**F5**).
- `protected-paths-check.sh` on a `governance/artifacts/**` edit → flagged critical (**F6**).
- final `dotnet build` + `dotnet test` green.

## Rollback

Each phase is one commit. Phase 1 is the only risky one (engine repoint); if any guard can't be repointed cleanly, revert that single commit — the move and the path changes are in it together, so there's never a split-brain state where the engine looks in the wrong place.

## Why this de-risks the ADR scale-up

After this lands, `governance/artifacts/` exists, the engine reads definitions from it, and the well-formedness guard runs over it. Adding the **ADR** artifact is then purely additive: a new `governance/artifacts/ADR/` folder (`artifact.yml family: nl-first, parity: null`, `template.md` with `## Criteria`) — no `rules.md`, no test code, and the existing guards cover it the moment it lands. The hard part (reshaping the engine's path assumptions) is paid once, here, on the system we understand best.

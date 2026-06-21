# Test → Artifact Migration (re-derived)

> **Execution plan, grounded in [`artifact-standard.md`](artifact-standard.md)** (floor
> + Q1–Q4 locked 2026-06-17). Makes the test system the **first instance** of the
> Artifact Standard. **Hard invariant:** warning-free build + green `dotnet test
> ABox.slnx` at the end of every phase, and **zero loss of any current testing
> feature.** Strategy **A** — pilot one sub-type through the engine repoint, then sweep
> the rest. Each phase is one coherent commit, independently revertible.

## What the locks fix

- **One `Test` artifact** (Q3), sub-types as sub-folders; the seven share one profile.
- **Registry = per-folder + generated `INDEX.md`** (Q1); profile in YAML `artifact.yml`.
- **Generic structural core + `ParityGuard` as the first (code-first) adapter** (Q2).
- **Pilot then sweep** (Q4).

## Target shape

```
governance/artifacts/
├── INDEX.md                  # generated matrix of all artifacts (never hand-edited)
└── Test/
    ├── artifact.yml          # the ONE Test profile (home: tests/Tests, family: code-first,
    │                         #   parity: ABox.Tests, gate: block, purpose: …)
    ├── conventions.md        # generic test stuff (parity discipline, authoring craft)
    ├── Arch/      { template.md, rules.md }
    ├── Structure/ { template.md, rules.md }
    ├── Unit/ E2E/ Wire/ Live/ { … }
    └── Meta/      { template.md, rules.md }

tests/                        # CODE ONLY after the move
├── Harness/                  # the engine — unchanged in place (ADR 0013-D3 tool-pinned)
├── Meta/Tests/               # the self-suite tests (Rulebook/ gone)
└── Tests/<Type>/{Tests,Support}/   # the [Rule] facts (Rulebook/ gone)
```

Only the **14 Rulebook markdown files** leave `tests/`; all `.cs`, `.csproj`, and
namespaces stay. (Verified: csprojs never reference the `.md` — the move is build-safe.)

## Features that must survive (the contract)

| # | Feature today | Preserved by |
|---|---|---|
| F1 | **Parity 1:N** — every Rule has ≥1 `[Rule]` test; bare test fails | the code-first **adapter** (`ParityGuard`) repointed to `Test/<Type>/rules.md` + the Meta self-rulebook |
| F2 | **Structural format** — every Rule matches its template; `rules.md` = pointers + Rules; every template has `## Criteria` | the **generic structural core**, reading templates from the new home |
| F3 | **Taxonomy** — every `tests/Tests/` folder is a registered type; every test lives in one | unchanged (code tree untouched) |
| F4 | **namespace = folder** (IDE0130 error) | unchanged |
| F5 | **Judges** — `/judge`, `/judge-rulebook`, `/judge-authoring` | repoint command/skill paths to `governance/artifacts/Test/` |
| F6 | **Protected-path coverage** of the definitions | `governance/**` already critical; add explicit `governance/artifacts/**` row |
| F7 | **`rules.md` = pointers + Rules only** | update the relative links in each `rules.md` preamble |
| F8 | **Floor (new)** — each artifact declares home + purpose + template + criteria | the **meta-guard** enforces it on the `Test` definition |

## Coupling inventory (what hardcodes the old layout)

| Location | Change |
|---|---|
| `Harness/RepoTree.cs` | `RulebookFolders()` → enumerate `governance/artifacts/Test/*` (the sub-types) |
| `Harness/TestTypes.cs` | `RulebookPath(t)` → `governance/artifacts/Test/<t>/rules.md`; `Namespace`/`Registered` unchanged |
| `Harness/ParityGuard.cs` | `ProductRulebook` → the new home (the adapter's path) |
| `Meta/Tests/ParityTests.cs` | Meta self-rulebook → `governance/artifacts/Test/Meta/rules.md` |
| `Meta/Tests/RulebookFormatTests.cs`, `TaxonomyTests.cs` | free once `RepoTree` repointed |
| `governance/protected-paths` | drop `tests/**/Rulebook/**`; add `governance/artifacts/**`; regenerate CODEOWNERS |
| `.claude/commands/judge*.md`, `.claude/skills/test-rulebook/SKILL.md` | repoint paths |
| `tests/**/README.md`, `CLAUDE.md` | update prose + pointers |
| **csprojs** | **none** — `.md` never referenced (build-safe) |

## New infra to build (the generic core, per Q1/Q2)

- **register-reader** — discover `governance/artifacts/*/artifact.yml` (per-folder, YAML).
- **structural validator** — generic: every instance matches its type's template schema (generalize "every Rule matches its template").
- **code-first adapter** — the existing `ParityGuard`, *registered as the first adapter* (reflection-parity), iterating the `Test` sub-types.
- **meta-guard** — enforce the floor on each artifact definition: has home + purpose + template + `## Criteria`.
- **INDEX generator** — compile `governance/artifacts/INDEX.md` from the folders.

## Phases (strategy A)

> **Defer to type #2 (ADR):** the generated `INDEX.md` (pointless at one entry) and the
> reflexive meta-type *packaging* (keep the meta-guard *function*; skip the
> "`ArtifactType` governs itself" framing until a second definition makes it real). See
> [`artifact-standard.md`](artifact-standard.md) § Build order.

**Phase 0 — Cheap, in-`tests/`, reversible (do first).** Add the floor's **purpose /
when-to-use** check to the existing Meta format guard (a real gap today), and
**consolidate** the three Rulebook-path derivations (`RepoTree` / `TestTypes` /
`ParityGuard`) into one source. No relocation, no protected-path move beyond the harness
itself; useful even if nothing else ships. **Gate:** green.

**Phase 1 — Infra + pilot (`Structure`).** Stand up `governance/artifacts/Test/` with
`artifact.yml` + `conventions.md`; move `Structure`'s `{template.md, rules.md}` into
`Test/Structure/`; build the register-reader + generic structural validator + meta-guard
+ INDEX; repoint `RepoTree`/`ParityGuard` for the migrated sub-type, with the other six
still read from their current path during the pilot. **Gate:** green; the generic engine
is proven end-to-end on one sub-type.

**Phase 2 — Sweep.** Move the remaining six + `Meta` into `Test/<Type>/`; drop the pilot
dual-read; the code-first adapter now iterates all sub-types from the one home. **Gate:**
green; old `Rulebook/` folders gone.

**Phase 3 — Surfaces.** `protected-paths` (+ regenerate CODEOWNERS), judges, the
`test-rulebook` skill, the `tests/**` READMEs and `CLAUDE.md` pointers.

**Phase 4 — Verify zero loss (negative tests).** Drive each guard to red and back:
delete a `[Rule]` test → F1; stray `##` in a `rules.md` → F2; strip `## Criteria` → F2;
a `Test/` sub-folder missing its template → meta-guard/F8; `/judge-rulebook` resolves at
the new path → F5; `protected-paths-check.sh` flags a `governance/artifacts/**` edit → F6.
Final `dotnet build` + `dotnet test` green.

## Rollback

Each phase is one commit. Phase 1 (the engine repoint) is the only risky one and is
scoped to a single sub-type; if a guard can't be repointed cleanly, revert that commit —
the move and the path change live together, so there is never a split-brain state.

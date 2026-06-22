# Governance Relocation — Execution Plan

> **Decision:** [ADR 0014](../../design/adr/0014-governance-agent-first-root.md) (accepted).
> This is the **how**, sequenced into small reviewable PRs behind that ADR. The **what/why**
> and the migration map live in
> [`governance-relocation-proposal.md`](governance-relocation-proposal.md); this file is the
> ordered checklist, the green-gates, and the rollback per step. Pure relocation + naming —
> **no behavior change** (ADR 0014, Consequences).

## Invariants every step holds

- **`git mv`, never delete-and-recreate** — preserve history (ADR 0014).
- **Enforcers never point at a missing path.** A script/policy move and the rewire of every
  reader of that path land in the **same** PR — never split across PRs, or a window opens
  where a guard reads a path that no longer exists.
- **Green-gate per PR:** `dotnet build` + `dotnet test ABox.slnx` clean, **and** the required
  checks (`build-test (ubuntu-latest)` / `build-test (windows-latest)`) pass. `policy-guard`
  is advisory — it *will* flag every protected-path touch; that is expected, not a failure.
- **CODEOWNERS stays resolvable.** Any PR that moves a protected path regenerates CODEOWNERS
  in the same PR, or required-review paths orphan and every PR locks.
- **One coherent commit per PR**, warning-free (repo code standard).

## Rewire points (from the wiring scan — the readers each move must update)

| Reader | Hardcodes | Updated in |
|---|---|---|
| `.github/workflows/ci.yml` | `governance/*.sh`, policy path | PR-A |
| `governance/protected-paths-check.sh` | `governance/protected-paths` | PR-A |
| `governance/generate-codeowners.sh` | policy path → `.github/CODEOWNERS` | PR-A |
| `governance/notify-critical.sh`, `notify.yml` | policy path | PR-A |
| `governance/README.md` | `core.hooksPath .githooks`, script paths | PR-A |
| `.claude/settings*.json` (PreToolUse guard, if it calls the script) | script path | PR-A |
| ~6 `PLANS/*` + ADRs 0010/0012/0013/0014 cross-links | `PLANS/`, `design/` | PR-B / PR-C |
| `CLAUDE.md` | conventions text + doc paths | PR-D |
| `tests/Harness/RepoTree` | `governance/registry` **only** | **unaffected** ✅ |

`RepoTree` reads `ABox.slnx` (root marker) + `governance/registry` + `tests/` — none of the
moving instance dirs — so the **test harness is insulated** from every step except the
registry, which does not move. The one test-visible coupling is the `Research` artifact's
`home` (PR-C), handled there.

## The sequence

### PR-A — Stand up the engine (`governance/harness/`) + rewire enforcers  *(load-bearing, atomic)*

The riskiest step: the live control surface moves. Keep it self-contained so enforcers are
never mid-air.

1. `git mv` the engine pieces into `governance/harness/`:
   - `protected-paths-check.sh`, `generate-codeowners.sh`, `notify-critical.sh`, `notify.yml`,
     `notify.md` → `governance/harness/`
   - `.githooks/pre-*` → `governance/harness/hooks/`
2. `git mv governance/protected-paths` → `governance/policy/protected-paths` (instance).
3. Rewire every reader to the new paths: the three scripts (policy path), `ci.yml` (script
   paths), the `.claude` PreToolUse guard if it shells the checker, and `governance/README.md`
   (`core.hooksPath governance/harness/hooks`, new script paths).
4. **Rewrite the policy's own rows** to the new layout: `governance/harness/**` +
   `governance/policy/**` critical; keep `governance/registry/**` critical; `governance/**`
   catch-all stays. Regenerate `.github/CODEOWNERS`.
5. ADR/plan *templates* into `governance/harness/templates/` (new, from the proposal skeleton)
   — additive, low-risk.
- **Green-gate:** run `protected-paths-check.sh` against a known protected path (still blocks)
  and a `src/` path (still allows); `build-test` green; CODEOWNERS names the new paths.
- **Rollback:** single PR revert restores the old paths atomically (nothing else depends yet).

### PR-B — Move decisions + plans (instance) + relink

1. `git mv design/adr` → `governance/decisions/` ; `git mv PLANS` → `governance/plans/`.
2. Rewrite the ~6 `PLANS/*` and ADR cross-links (and any `governance/README.md` / `CLAUDE.md`
   pointers) to the new paths. **Link-only churn** — no content edits.
3. Policy rows: `governance/decisions/**` review-tier; **stable specs** (`behavioral-oracle`,
   the PRD) review-tier; **volatile `governance/plans/**` ungoverned** (ADR 0014 Q1).
   Regenerate CODEOWNERS.
- **Green-gate:** no dead relative links (link-check the moved docs); `build-test` green
  (no code touched).
- **Rollback:** revert; `git mv` is history-preserving so the inverse is clean.

### PR-C — Move design + consolidate research + update `Research.home`

1. `git mv design/*` (the non-adr remainder: oracle, design notes) → `governance/design/`.
2. **Consolidate research** (ADR 0014 Q3): `git mv research/*` **and** `design/research/*`
   → `governance/design/research/`. Reconcile any name collisions explicitly. `spikes/` stay.
3. **Update `governance/registry/Research/artifact.yml`** `home: governance/design/research`
   **in the same PR** — the Meta floor guard requires `home` to be an existing dir, so the move
   and the `home` flip must be atomic or the guard goes red.
4. Relink referrers to `design/` / `research/` (the ~9 inbound links). Regenerate CODEOWNERS if
   tiers shift.
- **Green-gate:** `dotnet test` green — specifically the Meta *Every artifact declares the
  floor* guard, which now validates the moved `home`. This step is the one that **closes the
  deferred #3 consolidation**.
- **Rollback:** revert moves + restore `home: research`.

### PR-D — Split `CLAUDE.md` + finalize conventions

1. Extract the portable half of `CLAUDE.md` → `governance/harness/conventions/`
   (`code-standards.md`, `agent-guardrails.md`, `test-rulebook.md`).
2. Reduce root `CLAUDE.md` to this-repo prose + `@`-imports of those conventions (stays pinned
   at root — Claude Code auto-loads it).
3. Final link sweep across the repo for any remaining `PLANS/` / `design/adr/` / `design/`
   references.
- **Green-gate:** root `CLAUDE.md` `@`-imports resolve; full-repo grep for old paths returns
  nothing (outside `prototype/`); `build-test` green.
- **Rollback:** revert; conventions fold back inline.

## Sequencing rationale

- **PR-A first and alone** because it is the only load-bearing move — every other step is docs.
  Isolating it keeps the blast radius of an enforcer mistake to one revert.
- **B and C are independent** of each other (decisions/plans vs design/research) and can land in
  either order or parallel; both depend only on A (for the policy/CODEOWNERS rewire pattern).
- **D last** — the `CLAUDE.md` split references the final resting paths of conventions and docs,
  so it wants A–C settled.

## Out of scope (per ADR 0014)

- **Q2 distribution mechanism** (copy vs subtree vs setup-script) — deferred to first real
  second-adopter; nothing here commits to one.
- **Code `spikes/`** — stay where they are.
- **No behavior change** — if any step changes what an enforcer *does* (not just where it
  lives), stop: that is a new decision, not this migration.

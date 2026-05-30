# Agentic SDLC — phase-by-phase build & quality flow

**Status:** v0.1 — iteration draft.
**Purpose:** Make every phase, agent role, tool, gate, and exit criterion explicit so the workflow can be reviewed, tightened, and eventually implemented as concrete agent definitions + CI configuration.

**Reading order:** §0 establishes the cross-cutting foundations every phase depends on. §A → §B → §C is the runtime order. §Z lists what's still open.

Each phase section follows the same template:
- **Goal** — one sentence; what this phase produces.
- **Inputs** — durable artifacts read.
- **Outputs** — durable artifacts written.
- **Agents** — role names + their write boundaries.
- **Tools** — concrete tooling (CLI, services, hooks).
- **Flow** — ASCII flow graph of the logic path.
- **Exit criteria** — how we know this phase is done.
- **Failure modes** — concrete things that go wrong here.
- **Open questions** — what we haven't decided yet.

---

## §0 — Cross-cutting foundations

These are not a "phase"; they are the runtime substrate every phase depends on. Build these once at project init.

### §0.1 Repository layout

```
.
├── governance/                      # READ-ONLY to all coding agents
│   ├── adrs/                        # Append-only. New ADRs supersede old; never edit in place.
│   ├── evals/
│   │   ├── rubrics/                 # LLM-judge prompts, scoring criteria
│   │   └── datasets/                # Golden inputs, fixtures/discovery/ (from §A.2)
│   ├── lint-configs/                # ESLint, ruff, etc.
│   └── codeowners-policy.md         # The rules behind CODEOWNERS
├── .github/
│   ├── CODEOWNERS                   # Owns itself: see §0.3
│   └── workflows/                   # CI: eval gate, mutation testing, anti-gaming auditor
├── src/                             # Implementation
├── tests/
│   ├── acceptance/                  # Owned by fixture-author agent
│   ├── unit/                        # Owned by code-author agent
│   └── mutation/                    # Tooling config only
├── fixtures/
│   └── discovery/                   # Failing-test stubs harvested from §A.2 prototype
└── PLANS/                           # Spec, MVP plan, feature briefs (durable docs)
```

### §0.2 Agent roles & write boundaries

The core safety property: **no single agent can both author the test and the implementation it gates.**

| Role                 | Can write                                         | Cannot write                                  | Notes |
|----------------------|---------------------------------------------------|-----------------------------------------------|-------|
| `spec-author`        | `PLANS/**`                                        | everything else                               | Human-paired; produces specs/MVP plan. |
| `fixture-author`     | `tests/acceptance/**`, `fixtures/**`              | `src/**`, `governance/**`                     | Translates acceptance criteria → executable fixtures. |
| `code-author`        | `src/**`, `tests/unit/**`                         | `tests/acceptance/**`, `fixtures/**`, `governance/**` | Implementation. |
| `reviewer-panel`     | nothing (PR comments only)                        | everything                                    | Read-only. Heterogeneous models (see §0.5). |
| `test-integrity-auditor` | nothing (PR block decisions only)             | everything                                    | Fires only when diff touches tests/mocks/fixtures. |
| `quality-prober`     | branches under `bot/quality/**`                   | `governance/**`, anything touching tests directly | Self-improvement loop agent (§C). |
| `governance-author`  | `governance/**`                                   | `src/**`, `tests/**`                          | Human-only by default; agents may *propose* via PR. |

Enforcement layers (defense in depth):
1. **Claude Code PreToolUse hooks** — block writes outside the role's allowed paths locally.
2. **CODEOWNERS** — block merge if PR touches paths not owned by the PR author's identity (see §0.3).
3. **Branch protection** — require PR + CODEOWNERS approval + passing checks. "Include administrators" ON.

### §0.3 CODEOWNERS policy

```
# CODEOWNERS owns itself — closes the self-edit loophole
/.github/CODEOWNERS              @org/architecture

# Governance is human-only
/governance/**                   @org/architecture
/PLANS/**                        @org/architecture

# Test ownership boundary
/tests/acceptance/**             @org/fixture-authors @org/architecture
/fixtures/**                     @org/fixture-authors @org/architecture
/tests/unit/**                   @org/code-authors

# Implementation
/src/**                          @org/code-authors

# Default fallback
*                                @org/maintainers
```

Each agent identity (bot GitHub account or PAT) is a member of exactly one team. Cross-boundary PRs require human approval.

### §0.4 Eval suite (the measurement layer)

| Layer | What it measures | When it runs | Authoritative? |
|-------|------------------|--------------|----------------|
| **L1 — Linters & static analysis** | Style, obvious bugs, security smells | Every commit | No, but blocking |
| **L2 — Unit + acceptance tests** | Functional correctness | Every PR | Yes for functional |
| **L3 — Mutation testing** | Test suite *strength* (kill rate) | Weekly + before release | Yes for test quality |
| **L4 — Structural metrics** | Dep-cycle delta, assertion density, build-time delta, change-failure rate | Every PR (delta vs main) | Trend signal only |
| **L5 — LLM-as-judge (rubric-based)** | ADR/standards compliance, code clarity | Nightly + on PR | Advisory; never sole gate |
| **L6 — Anti-gaming auditor** | Test weakening, mock relaxation, skip/xfail introduction | When diff touches tests/mocks/fixtures | Blocking |

**Hard rule:** never combine functional and quality scores into a single composite. Functional gates merge; quality scores inform the self-improvement loop.

**Forbidden as quality signals:** raw LOC, class count, file count, naive duplication %. These are Goodhart magnets — they reward fragmentation and premature abstraction.

### §0.5 Heterogeneous review

Same-model panels manufacture consensus. Reviewer panels use at least two model families (e.g., Claude + Gemini) or single-judge round-robin (CyclicJudge) across PRs. The `test-integrity-auditor` is always a different model from the code-author.

### §0.6 Budgets (loop safety)

Any recurring loop (esp. §C) is bounded by three independent budgets enforced **outside the model**:
- **Token budget** per day/week.
- **Max open bot-PRs** (suggested cap: 5).
- **Per-target cooldown** — after a merge or rejection on a file, that file is off-limits for K days.

---

## §A — Bootstrap (project init)

Goal: produce a durable spec and a credible MVP plan, with at least one hard-slice unknown validated before commitment.

### §A.1 — North-star spec

**Goal:** Capture intent that survives every implementation rewrite.
**Inputs:** Founder/user vision, constraints (deadlines, compliance, integrations).
**Outputs:** `PLANS/00-north-star.md` — problem, audience, non-goals, success criteria, constraints. No solution detail.
**Agents:** `spec-author` (paired with human).
**Tools:** Conversational session; no code.
**Flow:**

```
[Human + spec-author]
     |
     v
  back-and-forth Q&A (≤1 hr)
     |
     v
  draft 00-north-star.md
     |
     v
  human sign-off  →  committed under governance ownership
```

**Exit criteria:** Document fits on one page. Can a stranger read it and explain the product back in one paragraph? Yes → ship.
**Failure modes:** Spec drifts into solution detail. Founder treats it as "the plan" rather than "the why."
**Open questions:** Do we want a template repo with `00-north-star.md` skeleton? Yes — TODO.

### §A.2 — Throwaway probe

**Goal:** Surface unknowns the spec cannot predict (integration friction, scale assumptions, framework limits). Promote evidence, discard code.
**Inputs:** `00-north-star.md`.
**Outputs:**
- `PLANS/01-probe-findings.md` — what we learned, what surprised us.
- `fixtures/discovery/*.failing.test.*` — every surprise expressed as a failing test/fixture.
- `governance/adrs/0001-*.md` … initial ADRs forced by the probe.
- *Discarded:* the probe codebase itself. Branch deleted.

**Agents:** Single `code-author` agent in unconstrained "scout" mode. Human reviews findings.
**Tools:** Claude Code, fast LLM, no CI. Disposable branch.
**Flow:**

```
00-north-star.md
       |
       v
  scope hard slice  ←─── MANDATORY: must include ≥1 async path,
       |                 ≥1 auth flow, OR ≥1 data migration
       v
  scout agent builds end-to-end probe (timeboxed: 1-2 days)
       |
       v
  human + agent: post-mortem
       |
       +──> probe-findings.md   (durable)
       +──> fixtures/discovery/ (durable failing tests)
       +──> ADRs forced by surprises (durable)
       +──> probe code: DELETE
```

**Exit criteria:** Every surprise from the probe exists either as a fixture, an ADR, or a documented finding. The probe code is gone.
**Failure modes:**
- Probe validates only the easy half (UI, simple CRUD) — auth/scale/migrations never touched.
- Team gets attached to the prototype code and keeps it. The whole point is to discard.
- Findings stay in someone's head, not committed.

**Open questions:**
- How do we enforce "hard slice required"? Checklist in probe brief.
- Token/time budget for probe? Suggest 1-2 days max.

### §A.3 — Replan → MVP plan

**Goal:** Produce a feature list informed by the probe.
**Inputs:** `00-north-star.md`, `01-probe-findings.md`, `fixtures/discovery/`, initial ADRs.
**Outputs:** `PLANS/02-mvp-plan.md` — ordered feature list with rough acceptance criteria per feature.
**Agents:** `spec-author` (human-paired).
**Tools:** Conversational planning.
**Flow:**

```
north-star + probe-findings + discovery fixtures
       |
       v
  planning session (human-driven)
       |
       v
  ordered feature list  →  02-mvp-plan.md
       |
       v
  each feature gets a stub brief in PLANS/features/NN-*.md
```

**Exit criteria:** Feature list is ordered (not parallel), each has acceptance criteria in English, dependencies between features are explicit.
**Failure modes:** Premature decomposition into tasks. MVP plan should be features, not tasks.
**Open questions:** How granular is a "feature"? Heuristic: one feature ≈ one PR ≈ 1-3 days of agent work.

### §A.4 — Skeleton features pass

**Goal:** Lay down cross-cutting concerns *before* business features, so they don't fragment.
**Inputs:** `02-mvp-plan.md`.
**Outputs:**
- Stub implementations + ADRs for: auth, observability/logging, error taxonomy, transaction boundaries, config loading, API/IPC envelope.
- One ADR per concern in `governance/adrs/`.

**Agents:** `code-author` + `fixture-author` (full §B loop, but for skeleton concerns rather than business features).
**Tools:** Same as §B.
**Flow:** Same shape as §B, but each "feature" is a cross-cutting concern.

**Exit criteria:** A new business feature in §B can use auth, log an error, and emit a metric without inventing its own pattern.
**Failure modes:** Skipped because "it's just plumbing." Five features later, five different error taxonomies exist.
**Open questions:** Which concerns are MUST vs NICE? MUST list above. Anything project-specific is a per-project decision in §A.3.

---

## §B — Per-feature build loop (sequential)

Goal: ship one MVP feature with acceptance fixtures, implementation, multi-agent review, and standards delta — then move to the next.

**Sequencing rule:** Features go one at a time. Single-threaded writes, multi-threaded review. (Cognition's rule: "writes single-threaded, intelligence multi-threaded.")

### §B.1 — Feature spec

**Goal:** Translate the MVP-plan feature stub into a brief actionable by `fixture-author`.
**Inputs:** `PLANS/features/NN-feature.md` (stub), `governance/adrs/*`, existing fixtures.
**Outputs:** Enriched feature brief — acceptance criteria as numbered English statements, non-goals, dependencies on existing features.
**Agents:** `spec-author` (human-paired).
**Tools:** Conversational session.
**Flow:**

```
feature stub + ADR context
       |
       v
  spec-author refines: "Given X when Y then Z" list
       |
       v
  feature brief committed to PLANS/features/NN-feature.md
```

**Exit criteria:** Acceptance criteria are testable. Each starts with a precondition, action, observable outcome.
**Failure modes:** Criteria stated as implementation ("uses Redis to cache X") rather than behavior ("Y returns within 100ms").

### §B.2 — Fixture authoring

**Goal:** Translate English acceptance criteria into executable acceptance tests.
**Inputs:** Feature brief, fixtures from §A.2 and prior features.
**Outputs:** New files in `tests/acceptance/` and possibly `fixtures/`. PR labeled `fixtures:NN`.
**Agents:** `fixture-author` only. (No write access to `src/`.)
**Tools:** Test framework (e.g., pytest, vitest), Gherkin or table-driven style.
**Flow:**

```
feature brief
      |
      v
  fixture-author proposes test list
      |
      v
  human approves test list (cheap signoff — minutes)  ← OPTIONAL but recommended
      |
      v
  fixture-author writes failing tests
      |
      v
  CI runs: tests MUST FAIL (no impl yet) — this is the gate
      |
      v
  PR merged on green-on-failure  ← unusual gate: expects red
```

**Exit criteria:** Tests exist, run, fail in the expected way. `code-author` has not yet touched the branch.
**Failure modes:**
- Fixture-author hedges and writes tests that pass trivially (always-true assertions). *Mitigation:* a separate sanity check that every new acceptance test must fail without impl present.
- Fixtures encode implementation detail. *Mitigation:* reviewer-panel checks behavior-vs-implementation framing.

### §B.3 — Implementation

**Goal:** Make the §B.2 fixtures pass without weakening them.
**Inputs:** Failing acceptance tests, feature brief, ADRs.
**Outputs:** Code under `src/**`, unit tests under `tests/unit/**`. PR labeled `impl:NN`.
**Agents:** `code-author` only. (No write access to `tests/acceptance/**`, `fixtures/**`, `governance/**`.)
**Tools:** Claude Code, Aider, or equivalent. Local linters. Local test runner.
**Flow:**

```
failing acceptance tests + feature brief + ADRs
      |
      v
  code-author breaks feature into internal tasks (own scratch)
      |
      v
  TDD inner loop: write unit test → impl → run unit + acceptance
      |
      v
  all acceptance tests green
      |
      v
  open PR → triggers §B.4 review panel
```

**Exit criteria:** All acceptance tests pass. No acceptance test was modified (CI verifies). Linters green.
**Failure modes:**
- Cheapest path to green is editing the acceptance test. *Mitigation:* PreToolUse hook blocks writes outside `src/` and `tests/unit/`; CODEOWNERS blocks merge if `tests/acceptance/` was touched by the code-author identity.
- Implementation copies fixtures inline. *Mitigation:* reviewer-panel checks for fixture-shaped constants in `src/`.

### §B.4 — Review panel

**Goal:** Independent multi-perspective verification before merge.
**Inputs:** PR diff (impl).
**Outputs:** PR comments; approve/block decisions. No code writes.
**Agents:**
- `reviewer-panel` (≥2 heterogeneous models): correctness, clarity, ADR compliance.
- `test-integrity-auditor` (fires only if diff touches tests/mocks/fixtures — should never fire for §B.3 impl PRs; if it does, hard block).
- One validator pass that consumes finder output and refutes false positives (Anthropic plugin pattern).

**Tools:** CI workflow that spawns reviewer agents in parallel; results posted as PR comments and a status check.
**Flow:**

```
impl PR opened
      |
      +──> parallel: reviewer-A (Claude), reviewer-B (Gemini), test-integrity-auditor
      |        each writes structured findings
      |
      v
  validator agent: dedupe + refute false positives
      |
      v
  surviving findings → PR comments
      |
      v
  if any blocking finding survives → status check FAIL → code-author iterates
      |
      v
  human approval (CODEOWNERS) + green checks → merge
```

**Exit criteria:** All blocking findings resolved. Human approver signed off per CODEOWNERS.
**Failure modes:**
- Panel rubber-stamps when models share blind spots. *Mitigation:* heterogeneous models.
- Validator dismisses real findings as false positives. *Mitigation:* validator must cite the finder's evidence in its refutation; spot-check weekly.

### §B.5 — ADR append + standards delta

**Goal:** Compound learning. Each feature that introduces a new pattern produces an ADR.
**Inputs:** Merged feature, post-implementation reflection.
**Outputs:** New ADR(s) in `governance/adrs/`, possibly a new lint rule.
**Agents:** `code-author` *proposes* ADR via PR; `governance-author` (human) approves.
**Tools:** ADR template; lint config repo.
**Flow:**

```
feature merged
      |
      v
  retrospective prompt to code-author: "What pattern did you establish?"
      |
      v
  if new pattern → ADR proposed (PR to governance/)
      |
      v
  human review + merge
      |
      v
  if pattern is enforceable → corresponding lint rule added
```

**Exit criteria:** Either an ADR was added, or the team explicitly decided no new pattern emerged.
**Failure modes:**
- Skipped under time pressure. *Mitigation:* status check requires either an ADR PR linked from the impl PR OR a "no-ADR" tag with rationale.
- ADRs proliferate without supersession. *Mitigation:* governance-author audits monthly; supersede aggressively.

### §B.6 — Eval gate (per PR)

**Goal:** Catch regressions in functional + structural signals before merge.
**Inputs:** PR diff + main baseline.
**Outputs:** Status checks (pass/fail) + score deltas posted as PR comment.
**Agents:** None (CI only) for L1-L4. L5 LLM-judge optional, advisory.
**Tools:** Linter (L1), test runner (L2), mutation testing config (run weekly, not per PR — too slow), structural metrics calculator (L4), LLM-judge service (L5).
**Flow:**

```
PR open
   |
   v
  L1 linter        ─┐
  L2 tests         ─┤── parallel
  L4 structural    ─┘
        |
        v
  any L1/L2 fail → block
  L4 delta posted as comment (trend only)
        |
        v
  L5 LLM-judge (optional, async) posts advisory comment
        |
        v
  merge gated on: L1 + L2 + reviewer-panel approval
```

**Exit criteria:** L1 + L2 green, no blocking finding from §B.4.
**Failure modes:** Treating L4 numbers as gates. They are not. They are signals.

---

## §C — Self-improvement loop (post-MVP)

Goal: continuously raise code quality without changing functionality, under hard budgets that prevent runaway loops.

**Activation:** Only after MVP is "feature-complete and functionally passing." Never run during §B.

### §C.1 — Scan

**Goal:** Find candidate targets for improvement.
**Inputs:** Current main branch, eval scores, weekly mutation-test results, lint rules, ADRs.
**Outputs:** Prioritized list of (file, issue type, score) tuples. Stored as a transient queue (e.g., GitHub issues labeled `bot/quality`).
**Agents:** `quality-prober` (scan mode).
**Tools:** Linters, structural metrics, LLM-judge against rubrics in `governance/evals/rubrics/`.
**Flow:**

```
main branch + governance/evals/rubrics/*
      |
      v
  prober scans:
    - lint violations not yet fixed
    - mutation-survivors (untested mutations)
    - dependency cycles introduced
    - LLM-judge findings against rubric
      |
      v
  rank by (severity × confidence), filter by cooldown
      |
      v
  prioritized queue (top N where N ≤ remaining PR budget)
```

**Exit criteria:** Queue produced or empty (no candidates → loop idle, no cost).
**Failure modes:** Scan picks up noise (style nits) and floods the queue. *Mitigation:* rubrics gate by severity; lint rules tuned so low-value rules don't enter the queue.

### §C.2 — Propose

**Goal:** Generate a fix proposal for one queued issue.
**Inputs:** One queue item.
**Outputs:** Branch under `bot/quality/**` with proposed diff + reasoning summary.
**Agents:** `quality-prober` (propose mode). Cannot write to `tests/acceptance/`, `fixtures/`, `governance/` — same boundaries as `code-author`.
**Tools:** Claude Code / Aider in branch.
**Flow:**

```
one queue item
     |
     v
  prober reads file + relevant ADRs
     |
     v
  prober makes proposal (diff only — no test changes allowed)
     |
     v
  branch pushed: bot/quality/<issue-id>
```

**Exit criteria:** Branch exists, all acceptance tests still pass locally.
**Failure modes:** Prober rewrites broad surface area. *Mitigation:* per-PR file-touch cap (e.g., max 5 files); breach → auto-reject.

### §C.3 — Heterogeneous review

Identical to §B.4 (review panel + test-integrity-auditor + validator). One difference: the bar is higher — the proposal must demonstrably improve a measured signal (see §C.5).

### §C.4 — Apply (open PR)

**Goal:** Open the PR for human review or auto-merge per policy.
**Inputs:** Reviewed branch.
**Outputs:** PR on `main`.
**Agents:** `quality-prober`.
**Tools:** `gh pr create`.
**Flow:**

```
panel approves
     |
     v
  PR opened to main with: issue link, before/after scores, proposal summary
     |
     v
  policy decides:
    - auto-merge if (low-risk file) AND (all checks green) AND (objective metric improved)
    - else human approval required
```

**Exit criteria:** PR exists, all checks pass.

### §C.5 — Re-eval + keep-or-revert (SICA rule)

**Goal:** Don't keep changes that didn't actually help.
**Inputs:** Merged PR.
**Outputs:** Either a stable merge or a revert PR.
**Agents:** Automated job (no agent reasoning).
**Tools:** Eval suite (L1-L4); mutation-test ground truth when available.
**Flow:**

```
PR merged
     |
     v
  re-run eval suite on main
     |
     v
  objective metric improved vs pre-merge baseline?
     ├── yes → keep; record success in queue history
     └── no  → automatic revert PR; record reason; cooldown the file for 30 days
```

**Exit criteria:** Either keep with recorded improvement, or revert with recorded reason.
**Failure modes:**
- Noisy metrics flip-flop. *Mitigation:* require improvement to exceed measurement noise floor (e.g., +3% min).
- Mutation-test signal lags (runs weekly). *Mitigation:* mutation-kill is checked at the weekly tick; regressions in the past week's bot-PRs are reverted in batch.

### §C.6 — Budgets & cooldown

**Goal:** Prevent runaway loops. Enforced outside any agent.
**Tools:** GitHub Actions schedules + repo-level state file under `governance/bot-state.json` (read-only to agents, written by CI).
**Rules:**
- **Daily token budget** — exceeded → loop pauses until next day.
- **Max open `bot/quality/**` PRs** — default 5. Cap reached → scan halts.
- **Per-file cooldown** — after merge or revert, file off-limits for 30 days.
- **Weekly mutation-test pass** — required green before next week's loop runs. Red → quality loop disabled until human fixes.

---

## §Z — Open questions for iteration

Things we haven't decided. Each is a candidate for the next iteration session.

1. **Probe time/token budget** — is 1-2 days right, or shorter? Should it cost ≤X% of MVP build budget?
2. **Feature granularity** — "1 PR ≈ 1 feature" is a heuristic. Do we want a stricter rule (e.g., max LOC delta)?
3. **Human signoff points** — feature brief approval, fixture list approval, PR approval. Three signoffs feels right but may be too many for small teams. Which can be auto-approved on green CI?
4. **Heterogeneous models** — which pairing? Claude + Gemini? Claude + GPT? Cost vs. coverage trade-off.
5. **LLM-judge model pinning** — when do we re-baseline scores after upgrading the judge model? Manually, or on a schedule?
6. **Mutation testing scope** — full suite weekly is feasible for small repos but explodes at scale. Sampling strategy?
7. **Anti-gaming auditor scope** — currently fires on `tests/**`, `mocks/**`, `fixtures/**`. Should it also fire on `governance/`? Probably yes — governance changes from bot accounts should always be human-reviewed.
8. **What happens when MVP is "done"** — do we keep §B running for new features alongside §C? Yes, but does feature work block the quality loop, or do they run concurrently with shared budget?
9. **Tooling concretization** — which actual products do we wire up? E.g., mutation testing → Stryker / mutmut / PIT? LLM-judge → Promptfoo / Braintrust / custom? This becomes the §0 implementation plan.
10. **Bootstrap shortcut** — for a project that already exists (not greenfield), how does §A change? §A.2 probe is replaced by "read existing code into discovery fixtures"?

---

## Appendix — phase-flow at a glance

```
┌─────────────────────────────────────────────────────────────────────────┐
│  §0 Foundations: governance/, CODEOWNERS, agent roles, eval suite       │
│  (built once at project init; read-only to coding agents)               │
└─────────────────────────────────────────────────────────────────────────┘
                                  │
              ┌───────────────────┴───────────────────┐
              │              §A Bootstrap             │
              │  A.1 north-star  →  A.2 throwaway     │
              │  probe (must hit hard slice; code     │
              │  discarded, evidence kept)  →  A.3    │
              │  replan → MVP plan  →  A.4 skeleton   │
              │  features pass                        │
              └───────────────────┬───────────────────┘
                                  │
              ┌───────────────────┴───────────────────┐
              │     §B Per-feature loop (sequential)  │
              │                                       │
              │   B.1 brief  →  B.2 fixtures (author  │
              │   only) → B.3 impl (code-author only) │
              │   → B.4 panel + auditor → B.5 ADR     │
              │   delta → B.6 eval gate → merge       │
              │                                       │
              │              repeat for next feature  │
              └───────────────────┬───────────────────┘
                                  │
                          MVP feature-complete
                                  │
              ┌───────────────────┴───────────────────┐
              │   §C Self-improvement loop (budgeted) │
              │                                       │
              │   C.1 scan  →  C.2 propose  →  C.3    │
              │   panel  →  C.4 PR  →  C.5 keep-or-   │
              │   revert (SICA)  →  cooldown          │
              │                                       │
              │   caps: tokens | open-PRs | cooldown  │
              │   | weekly mutation-test ground truth │
              └───────────────────────────────────────┘
```

# Agentic SDLC — phase-by-phase build & quality flow

**Status:** v0.3 — added living technical spec (§A.1, draft→probe→update) + durable phased task file (§B.3); agent-identity tier parked (§0.3a). v0.2 resolved the v0.1 open questions.
**Purpose:** Make every phase, agent role, tool slot, gate, and exit criterion explicit so the workflow can be reviewed, tightened, and eventually implemented as concrete agent definitions + CI configuration.

**Reading order:** §0 establishes the cross-cutting foundations every phase depends on. §A → §B → §C is the runtime order. §Z is the decision log (one line per resolved question + sibling docs).

**Changelog from v0.1:** Resolved all 10 open questions. Major structural changes: signoff gates are now configurable knobs (Q3), the eval suite is reframed in terms of *capability slots* rather than specific tools (Q6, Q9), judge versioning + anchor sets are now part of §0.4 (Q5), governance changes get adversarial review under specific conditions (Q7), §C explicitly yields to §B (Q8). Brownfield variant and starter-stack addendum are noted but deferred to sibling docs.

**Changelog from v0.2:** Added §A.1 living technical spec between north-star and probe — drafted as bets + non-goals + open questions, then updated in place after the probe (the standard agile spike loop: draft→spike→update). Closes the "why → build with no how" gap so the probe tests the patterns we actually care about (Q11). Restructured §B.3 around a durable, revisable phased task file with per-phase commits + findings compaction + fresh context — aligns with the industry-standard Specify→Plan→Tasks→Implement loop and context-engineering practice (Q12). Parked the agent-identity provenance-vs-authorization decision (§0.3a, Q13). New open sub-decision: task-file home. See §Z Q11–Q13.

---

## Design principles

Three principles govern what goes in this doc vs. somewhere else.

1. **Capability over product.** The doc names what each slot must do; specific tools (mutation tester, LLM-judge, CI provider, model identities) are config decisions handled in `governance/` or a sibling addendum. This lets tool selection evolve without invalidating the workflow.
2. **Knobs over hard rules.** Where a constraint depends on team size, project stakes, or domain risk, the doc declares a configurable knob with a default. Solo-dev, team, and high-stakes profiles can be defined later in `governance/`.
3. **Separation of authorship and verification.** No single agent owns both sides of any contract it's being measured against. Enforced by repo layout (§0.1), agent role boundaries (§0.2), and CODEOWNERS (§0.3).

Each phase section follows the same template:
- **Goal** — one sentence; what this phase produces.
- **Inputs** — durable artifacts read.
- **Outputs** — durable artifacts written.
- **Agents** — role names + their write boundaries.
- **Tool slots** — capability requirements (not products).
- **Flow** — ASCII flow graph of the logic path.
- **Exit criteria** — how we know this phase is done.
- **Failure modes** — concrete things that go wrong here.
- **Knobs** — what's configurable, where the config lives.

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
│   │   ├── datasets/                # Golden inputs, fixtures/discovery/ (from §A.2)
│   │   └── anchors/                 # Pinned scored samples for judge re-baselining (§0.4)
│   ├── lint-configs/                # ESLint, ruff, etc.
│   ├── judge.yaml                   # Heterogeneous review pairing config (Q4)
│   ├── signoff-policy.yaml          # Per-gate human signoff config (Q3)
│   ├── mutation-policy.yaml         # (or equivalent) eval-tool cadences (Q6)
│   └── codeowners-policy.md         # The rules behind CODEOWNERS
├── .github/
│   ├── CODEOWNERS                   # Owns itself: see §0.3
│   └── workflows/                   # CI: eval gate, anti-gaming auditor, §C runner
├── src/                             # Implementation
├── tests/
│   ├── acceptance/                  # Owned by fixture-author agent
│   ├── unit/                        # Owned by code-author agent
│   └── (tool-specific configs)      # Mutation/coverage configs etc. live here
├── fixtures/
│   └── discovery/                   # Failing-test stubs harvested from §A.2 prototype
└── PLANS/                           # Spec, MVP plan, feature briefs (durable docs)
```

### §0.2 Agent roles & write boundaries

Core safety property: **no single agent can both author the test and the implementation it gates.**

| Role | Can write | Cannot write | Notes |
|---|---|---|---|
| `spec-author` | `PLANS/**` | everything else | Human-paired; produces specs/MVP plan. |
| `fixture-author` | `tests/acceptance/**`, `fixtures/**` | `src/**`, `governance/**` | Translates acceptance criteria → executable fixtures. |
| `code-author` | `src/**`, `tests/unit/**` | `tests/acceptance/**`, `fixtures/**`, `governance/**` | Implementation. |
| `reviewer-panel` | nothing (PR comments only) | everything | Read-only. Heterogeneous models (§0.5). |
| `test-integrity-auditor` | nothing (PR block decisions only) | everything | Fires per triggers in §0.4. |
| `quality-prober` | branches under `bot/quality/**` | `governance/**`, `tests/**` (direct) | Self-improvement loop agent (§C). |
| `governance-author` | `governance/**` | `src/**`, `tests/**` | Human-only by default; agents may *propose* via PR. |

Enforcement layers (defense in depth):
1. **PreToolUse hooks** at the agent runtime — block writes outside the role's allowed paths locally.
2. **CODEOWNERS** — block merge if PR touches paths not owned by the PR author's identity (§0.3).
3. **Branch protection** — require PR + CODEOWNERS approval + passing checks. "Include administrators" ON.
4. **Author-identity tagging** — every agent runs under a distinct bot identity (email, GitHub team membership). This is the input the auditor uses to distinguish bot-authored from human-authored PRs (§0.4).

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

Each agent identity is a member of exactly one team. Cross-boundary PRs require human approval.

### §0.3a Agent identity — provenance-grade vs authorization-grade (OPEN — revisit)

**Status:** Unresolved design decision. Parked 2026-05-30; revisit before implementing §0.2/§0.3.

§0.2 layer 4 + §0.3 currently assume each agent role is a **distinct CODEOWNERS-eligible principal** — *authorization-grade* identity, where `fixture-author` literally cannot merge `src/**` because CODEOWNERS + branch protection enforce it per-identity. 2026 research shows this is the **minority, least-paved** path. Two tiers exist:

- **Provenance-grade** (where the ecosystem is converging): record *what* produced a change for audit, but authorize via least-privilege tokens + same-rigor required review — not per-agent accounts. Mechanisms: `Co-Authored-By:` / `AI-Model:` git trailers, env-var git author + per-agent SSH key, or platform-managed identity (GitHub **Agent HQ**, launched 2026-02-04, stamps "opened by &lt;model&gt; via Agent HQ" + signs commits). GitHub's own **Well-Architected: Governing agents** guidance takes this line — agents act within the existing permission model with an `actor_is_agent` audit flag; the control surface is CODEOWNERS + rulesets requiring independent review + least-privilege token scope, *not* one-account-per-agent.
- **Authorization-grade** (what §0.2/§0.3 assume today): each role is a separate principal CODEOWNERS can gate. Hard constraints found: **GitHub Apps cannot appear in CODEOWNERS** (users/teams only) — the "clean" App identity is disqualified from the code-owner role; ToS allows **one free machine account per human**; paid orgs charge **one seat per machine account**; org-mandatory **2FA per account**.

**If we keep authorization-grade:** collapse one-identity-per-*role* → one-identity-per-*enforced-boundary*. The only separation the anti-gaming property truly needs is **fixture-author vs code-author** → 2 machine accounts (2 seats), spec/governance roles staying human. Optionally use an App token for the *acting* layer + a machine account for *commit authorship* so CODEOWNERS/auditor see the right author.

**Decision to make:** is true per-identity authorization worth the seat / 2FA / Apps-not-in-CODEOWNERS friction, or is provenance + least-privilege tokens + same-rigor review (the GitHub-blessed path) sufficient for the anti-gaming model? Depends on §0.4 L6 auditor, which keys off author identity.

Sources: GitHub Well-Architected *Governing agents*; Agent HQ launch (2026-02-04); community discussion #23064 (Apps not valid in CODEOWNERS); GitHub ToS (machine accounts).

### §0.4 Eval suite (capability slots)

The workflow names *what each slot does*, not *which tool fills it*. Tool selection lives in `governance/mutation-policy.yaml` (or equivalent) and the starter-stack sibling doc.

| Slot | Capability requirement | When | Authoritative? |
|---|---|---|---|
| **L1 — Static checks** | Linters, formatters, type checkers, security scanners | Every commit | Blocking |
| **L2 — Functional tests** | Unit + acceptance test suite | Every PR | Yes for functional correctness |
| **L3 — Test-strength ground truth** | Some signal that catches test-weakening gaming (mutation testing is the canonical candidate; property-based testing, fuzzing also qualify) | Periodic (cadence in `governance/mutation-policy.yaml`); always before release | Yes for test strength |
| **L4 — Structural delta signals** | Dependency-cycle delta, assertion density, build-time delta, change-failure rate | Every PR (delta vs main) | Trend signal only |
| **L5 — Rubric-based LLM-judge** | Reads PR diff + relevant ADRs, scores against rubrics in `governance/evals/rubrics/` | Nightly + on PR | Advisory; never sole gate |
| **L6 — Anti-gaming auditor** | Adversarial reviewer with structured trigger rules (below) | Trigger-gated | Blocking on trigger |

**Hard rules:**
- Never combine functional and quality scores into a single composite. Functional gates merge; quality scores inform §C.
- Forbidden as quality signals: raw LOC, class count, file count, naive duplication %. Goodhart magnets — they reward fragmentation and premature abstraction.

**L5 — Judge versioning and re-baselining (Q5):**
- Judge model identity pinned in `governance/judge.yaml`.
- All L5 scoring uses **delta scoring**: every PR re-scores `main` with the same judge version at PR time. Absolute scores are meaningless; only deltas count. This makes scoring version-invariant within any single run.
- `governance/evals/anchors/` holds 30-50 pinned samples (`{input, output, human_label, score_with_pinned_judge}`). Grows as production failures from §B and §C are promoted.
- Judge upgrades happen opportunistically (never mid-feature). Protocol: re-score the anchor set with the candidate judge; accept iff Spearman vs pinned scores ≥ 0.85 AND human agreement on a random 10-sample unchanged. Bump `governance/judge.yaml`; record in an ADR.

**L6 — Auditor trigger rules (Q7):**
The anti-gaming auditor fires when ANY of these conditions hold on the PR:
- Diff touches `tests/**`, `fixtures/**`, `mocks/**`, or coverage-config files (catches test weakening).
- PR is authored by a **bot identity** AND touches `governance/**` (catches rubric/ADR self-serving edits by agents).
- PR touches `governance/**` AND (`src/**` OR `tests/**`) in the same PR, regardless of author (catches "rewrite the rule to fit my code" pattern, even when split-author).

Auditor output is a structured PR comment; the human reviewer still decides. The auditor never auto-merges; it can hard-block on patterns like assertion deletions co-located with implementation changes.

### §0.5 Heterogeneous review (Q4)

Same-model panels manufacture consensus (Fleiss κ across same-model judges is near zero). Reviewer panels MUST use at least two model families, OR single-judge round-robin (CyclicJudge) across PRs.

Specific pairing is config in `governance/judge.yaml`:

```yaml
# Example shape — actual values picked at implementation time
primary:   { provider: <provider>, model: <model_id> }
secondary: { provider: <provider>, model: <model_id> }    # MUST be different family from primary
auditor:   { provider: <provider>, model: <model_id> }    # MUST be different family from code-author
rotation:
  mode: per-pr | weekly | none   # CyclicJudge mode
```

This repo's programmable review-flow architecture is the substrate that makes pairing swappable.

### §0.6 Signoff policy (Q3)

All four candidate human signoff gates exist; each is independently configurable in `governance/signoff-policy.yaml`. Default is the `solo-dev` profile.

```yaml
# Example shape
profile: solo-dev | team | high-stakes
gates:
  feature_brief:    required | optional | off          # §B.1
  fixture_list:     required | optional | off          # §B.2
  pull_request:     required | optional | off          # §B.4 (CODEOWNERS enforced when required)
  adr:              required | batched_weekly | off    # §B.5
```

Profile presets (all overridable):
- **solo-dev:** brief=required, fixture_list=optional, pull_request=required, adr=batched_weekly.
- **team:** brief=required, fixture_list=required, pull_request=required, adr=required.
- **high-stakes:** all four = required.

`batched_weekly` means ADRs auto-open as draft PRs and the human triages all of them in one weekly session — no mid-feature interruption.

### §0.7 Budgets (loop safety)

Any recurring loop (esp. §C) is bounded by three independent budgets enforced **outside the model**:
- **Token budget** per day/week.
- **Max open bot-PRs** (suggested cap: 5).
- **Per-target cooldown** — after merge or rejection on a file, that file is off-limits for K days.

Plus (Q8): **§C yields to §B.** §C pauses new proposals while any §B PR is awaiting human review. See §C.6.

---

## §A — Bootstrap (project init)

Goal: produce a durable spec and a credible MVP plan, with at least one hard-slice unknown validated before commitment.

### §A.0 — North-star spec

**Goal:** Capture intent that survives every implementation rewrite.
**Inputs:** Founder/user vision, constraints (deadlines, compliance, integrations).
**Outputs:** `PLANS/00-north-star.md` — problem, audience, non-goals, success criteria, constraints. No solution detail.
**Agents:** `spec-author` (paired with human).
**Tool slots:** Conversational session; no code.
**Flow:**

```
[Human + spec-author]
     |
     v
  back-and-forth Q&A
     |
     v
  draft 00-north-star.md
     |
     v
  human sign-off  →  committed under governance ownership
```

**Exit criteria:** Document fits on one page. Can a stranger read it and explain the product back in one paragraph? Yes → ship.
**Failure modes:** Spec drifts into solution detail. Founder treats it as "the plan" rather than "the why."
**Knobs:** Template in starter-stack addendum (TBD).

### §A.1 — Technical spec (living doc) (Q11)

**Goal:** Give the "how" a home — and give the probe a target. **One living technical spec**, drafted before the probe and updated after it (the standard agile *spike* loop: draft → spike → update). Pre-probe it is design-doc/RFC-flavored (bets, non-goals, open questions); post-probe (§A.3) the same doc hardens toward a true tech spec ("how we build it") + ADRs. No separate hypotheses artifact — it is one document that *matures*.
**Inputs:** `00-north-star.md`.
**Outputs:** `PLANS/01-tech-spec.md` — candidate architecture, key library/runtime bets, data & IPC/envelope shapes, **non-goals**, and an **open-questions** section listing what the probe must resolve (tagged `must-probe` | `nice-to-probe` | `assume`). Pre-probe, any decision is explicitly marked *provisional*.
**Agents:** `spec-author` (human-paired); may consult a `code-author` in read-only advisory capacity.
**Tool slots:** Conversational session; no code.
**Flow (one artifact, touched at three points):**

```
00-north-star.md
     |
     v
  A.1    DRAFT 01-tech-spec.md: bets + non-goals + open questions
         (decisions marked provisional; open questions tagged must-probe;
          must-probe set feeds the §A.2 hard-slice checklist)
     |
     v
  A.2    probe TESTS the open questions  ───────────┐
     |                                              │ raw learnings → 02-probe-findings.md
     v                                              │
  A.3    UPDATE 01-tech-spec.md in place:  ◀───────┘
         resolve open questions, promote survivors to decisions,
         spawn ADRs for the load-bearing ones
```

**Exit criteria (at A.1):** Every `must-probe` open question is concrete and falsifiable. Anything still a guess lives in the open-questions section, not stated as a decision — so the probe stays a *test*, not a rubber-stamp.
**Failure modes:**
- Pre-probe draft over-commits — decisions written as settled bias the probe toward confirmation. Mitigation: provisional bets sit in the explicit open-questions/assumptions section until the probe clears them.
- Layer skipped → probe runs unguided and surfaces only generic risk (the gap this phase closes).
- Doc goes stale — drafted, never updated post-probe. Mitigation: A.3 exit requires the open-questions section be emptied (each item resolved, or re-flagged still-unknown with rationale).

**Knobs:** Optional for trivial/throwaway-greenfield projects; recommended whenever the team has specific architectural curiosity. Depth scales with stakes — a half-page bets list is fine for small projects.

### §A.2 — Throwaway probe (Q1)

**Goal:** Surface unknowns the spec cannot predict (integration friction, scale assumptions, framework limits). Promote evidence, discard code.
**Inputs:** `00-north-star.md`, `01-tech-spec.md`.
**Outputs:**
- `PLANS/02-probe-findings.md` — what we learned, what surprised us.
- `fixtures/discovery/*.failing.test.*` — every surprise expressed as a failing test/fixture.
- `governance/adrs/0001-*.md` … initial ADRs forced by the probe.
- *Discarded:* the probe codebase itself. Branch deleted.

**Agents:** Single `code-author` agent in unconstrained "scout" mode. Human reviews findings.
**Tool slots:** Disposable branch; whatever agent runtime is in use; no CI.
**Flow:**

```
00-north-star.md
       |
       v
  define hard-slice checklist  ←─── MANDATORY: ≥1 async path, ≥1 auth flow,
       |                            OR ≥1 data migration — AND every "must-probe"
       |                            open question from 01-tech-spec.md
       v
  scout agent builds end-to-end probe
       |
       v
  human + agent: post-mortem
       |
       +──> probe-findings.md   (durable)
       +──> fixtures/discovery/ (durable failing tests)
       +──> ADRs forced by surprises (durable)
       +──> probe code: DELETE
```

**Exit criteria:** Findings-driven (Q1) — every item on the hard-slice checklist has been resolved (validated or explicitly flagged as still-unknown with rationale), and every `must-probe` open question from `01-tech-spec.md` is confirmed or refuted with rationale. Every surprise from the probe exists as a fixture, an ADR, or a documented finding. Probe code is gone.
**Failure modes:**
- Probe validates only the easy half (UI, simple CRUD) — auth/scale/migrations never touched. Mitigation: the hard-slice checklist gates exit.
- Team gets attached to the prototype code and keeps it. The whole point is to discard.
- Findings stay in someone's head, not committed.
- Scope creep — the probe becomes "the build." Convention: keep it short; if duration exceeds project-specific common sense, force exit with documented gaps.

**Knobs:** No hard wall-clock cap. Project-specific norm is "keep it short" — duration scales with scope. Implementations may add an advisory token cap.

### §A.3 — Replan → MVP plan

**Goal:** Produce a feature list informed by the probe.
**Inputs:** `00-north-star.md`, `01-tech-spec.md`, `02-probe-findings.md`, `fixtures/discovery/`, initial ADRs.
**Outputs:** `PLANS/03-mvp-plan.md` — ordered feature list with rough acceptance criteria per feature.
**Agents:** `spec-author` (human-paired).
**Tool slots:** Conversational planning.
**Flow:**

```
north-star + tech-spec + probe-findings + discovery fixtures
       |
       v
  planning session (human-driven):
    update 01-tech-spec.md in place — resolve open questions, firm up
    surviving bets into decisions (load-bearing ones → ADRs at A.4)
       |
       v
  ordered feature list  →  03-mvp-plan.md
       |
       v
  each feature gets a stub brief in PLANS/features/NN-*.md
```

**Exit criteria:** Feature list is ordered (not parallel), each has acceptance criteria in English, dependencies between features are explicit.
**Failure modes:** Premature decomposition into tasks. MVP plan should be features, not tasks.
**Knobs:** None.

### §A.4 — Skeleton features pass

**Goal:** Lay down cross-cutting concerns *before* business features, so they don't fragment.
**Inputs:** `03-mvp-plan.md`.
**Outputs:**
- Stub implementations + ADRs for: auth, observability/logging, error taxonomy, transaction boundaries, config loading, API/IPC envelope.
- One ADR per concern in `governance/adrs/`.

**Agents:** `code-author` + `fixture-author` (full §B loop, but for skeleton concerns rather than business features).
**Tool slots:** Same as §B.
**Flow:** Same shape as §B, but each "feature" is a cross-cutting concern.

**Exit criteria:** A new business feature in §B can use auth, log an error, and emit a metric without inventing its own pattern.
**Failure modes:** Skipped because "it's just plumbing." Five features later, five different error taxonomies exist.
**Knobs:** Which concerns are MUST vs NICE is project-specific in §A.3. MUST list above is the default.

---

## §B — Per-feature build loop (sequential)

Goal: ship one MVP feature with acceptance fixtures, implementation, multi-agent review, and standards delta — then move to the next.

**Sequencing rule:** Features go one at a time. Single-threaded writes, multi-threaded review.

**Feature size (Q2):** No global rule. Each feature's scope is justified in its brief at §A.3 planning. The brief is the contract — if it's too big, the planning agent + human split it before §B.1 begins.

### §B.1 — Feature spec

**Goal:** Translate the MVP-plan feature stub into a brief actionable by `fixture-author`.
**Inputs:** `PLANS/features/NN-feature.md` (stub), `governance/adrs/*`, existing fixtures.
**Outputs:** Enriched feature brief — acceptance criteria as numbered English statements, non-goals, dependencies on existing features.
**Agents:** `spec-author` (human-paired).
**Tool slots:** Conversational session.
**Flow:**

```
feature stub + ADR context
       |
       v
  spec-author refines: "Given X when Y then Z" list
       |
       v
  human signoff per governance/signoff-policy.yaml (default: required for solo-dev)
       |
       v
  feature brief committed to PLANS/features/NN-feature.md
```

**Exit criteria:** Acceptance criteria are testable. Each starts with a precondition, action, observable outcome.
**Failure modes:** Criteria stated as implementation ("uses Redis to cache X") rather than behavior ("Y returns within 100ms").
**Knobs:** Signoff gate (`gates.feature_brief` in `governance/signoff-policy.yaml`).

### §B.2 — Fixture authoring

**Goal:** Translate English acceptance criteria into executable acceptance tests.
**Inputs:** Feature brief, fixtures from §A.2 and prior features.
**Outputs:** New files in `tests/acceptance/` and possibly `fixtures/`. PR labeled `fixtures:NN`.
**Agents:** `fixture-author` only. (No write access to `src/`.)
**Tool slots:** Test framework (project-language-specific), Gherkin or table-driven style.
**Flow:**

```
feature brief
      |
      v
  fixture-author proposes test list
      |
      v
  human approves test list per governance/signoff-policy.yaml (default: optional for solo-dev)
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
- Fixture-author hedges and writes tests that pass trivially (always-true assertions). Mitigation: sanity check — every new acceptance test must fail without impl present.
- Fixtures encode implementation detail. Mitigation: reviewer-panel checks behavior-vs-implementation framing.

**Knobs:** Signoff gate (`gates.fixture_list`). When off or optional, the "tests-fail-as-expected" CI check is the sole gate.

### §B.3 — Implementation

**Goal:** Make the §B.2 fixtures pass without weakening them.
**Inputs:** Failing acceptance tests, feature brief, ADRs.
**Outputs:** Code under `src/**`, unit tests under `tests/unit/**`, and a durable **phased task file** (see *Task-file home* below). PR labeled `impl:NN`.
**Agents:** `code-author` only.
**Tool slots:** Agent runtime + local linters + local test runner.
**Flow:**

```
failing acceptance tests + feature brief + ADRs
      |
      v
  code-author writes a durable, ordered phased task file:
    each phase maps to a subset of the acceptance criteria
    (execution scaffolding — NOT a governed artifact; revisable, not frozen)
      |
      v
  per phase, in a FRESH context window:
    load task file → TDD inner loop (unit test → impl → run unit + acceptance subset)
      → phase's acceptance subset green → commit
      → compact status + findings back into the task file
      |
      v
  next phase loads the compacted file in fresh context (no accumulated baggage)
      |
      v
  all acceptance tests green
      |
      v
  open PR → triggers §B.4 review panel
```

**Task-file home (OPEN sub-decision):** `code-author` cannot write `PLANS/**` (§0.2), so the phased task file needs a code-author-owned home. Candidates: (a) branch-only `worklog/NN-feature.md` that never merges to `main`; (b) a merged, code-author-owned `/worklog/**` path added to §0.2/§0.3. Deferred — pick when implementing §0.2 boundaries.

**Why durable, not scratch:** an ephemeral in-agent task list does not survive context compaction or a session reset — exactly the moment it's needed to resume. Persisting + compacting it per phase is the industry-standard context-engineering pattern (plan distilled to ~15–20% of a fresh context vs. 60–80% of accumulated session). It is *not* a contract: the brief is.

**Exit criteria:** All acceptance tests pass. No acceptance test was modified (CI verifies). Linters green. The **brief — not the task file — remains the contract**; the task file is disposable scaffolding.
**Failure modes:**
- Cheapest path to green is editing the acceptance test. Mitigation: PreToolUse hook blocks writes outside `src/` and `tests/unit/`; CODEOWNERS blocks merge; L6 auditor fires on any test diff.
- Implementation copies fixtures inline. Mitigation: reviewer-panel checks for fixture-shaped constants in `src/`.
- Task file ossifies — agent keeps following a plan it has already discovered is wrong. Mitigation: the file is compacted/revised between phases, never frozen; the brief is the contract, the plan is disposable.
- Ephemeral task list lost on context reset mid-feature. Mitigation: the file is durable precisely so a fresh context (or a resumed session) can pick up at the next phase.

**Knobs:** Whether the phased task file is *required* vs. left to `code-author` discretion is project-specific (large multi-phase features → required; trivial features → optional). Other constraints come from §0.2 boundaries.

### §B.4 — Review panel

**Goal:** Independent multi-perspective verification before merge.
**Inputs:** PR diff (impl).
**Outputs:** PR comments; approve/block decisions. No code writes.
**Agents:**
- `reviewer-panel` (≥2 heterogeneous models per §0.5): correctness, clarity, ADR compliance.
- `test-integrity-auditor` (fires per §0.4 L6 triggers).
- One validator pass that consumes finder output and refutes false positives.

**Tool slots:** CI workflow that spawns reviewer agents in parallel; results posted as PR comments and a status check.
**Flow:**

```
impl PR opened
      |
      +──> parallel: primary reviewer, secondary reviewer, L6 auditor (if triggered)
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
  human approval per governance/signoff-policy.yaml (default: required) + green checks → merge
```

**Exit criteria:** All blocking findings resolved. Human approver signed off per CODEOWNERS (when signoff gate is `required`).
**Failure modes:**
- Panel rubber-stamps when models share blind spots. Mitigation: §0.5 heterogeneous pairing.
- Validator dismisses real findings as false positives. Mitigation: validator must cite the finder's evidence in its refutation; spot-check weekly.

**Knobs:** Signoff gate (`gates.pull_request`). Pairing config (`governance/judge.yaml`).

### §B.5 — ADR append + standards delta

**Goal:** Compound learning. Each feature that introduces a new pattern produces an ADR.
**Inputs:** Merged feature, post-implementation reflection.
**Outputs:** New ADR(s) in `governance/adrs/`, possibly a new lint rule.
**Agents:** `code-author` *proposes* ADR via PR; `governance-author` (human) approves.
**Tool slots:** ADR template; lint config repo.
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
  human review per governance/signoff-policy.yaml:
    - required: synchronous review
    - batched_weekly: queues as draft PR, triaged in weekly session
      |
      v
  merge → if pattern is enforceable, add corresponding lint rule
```

**Exit criteria:** Either an ADR was added, or the team explicitly decided no new pattern emerged.
**Failure modes:**
- Skipped under time pressure. Mitigation: status check requires either an ADR PR linked from the impl PR OR a "no-ADR" tag with rationale.
- ADRs proliferate without supersession. Mitigation: append-only with explicit supersession; weekly triage prunes via supersession.

**Knobs:** Signoff gate (`gates.adr` — `required` | `batched_weekly` | `off`).

### §B.6 — Eval gate (per PR)

**Goal:** Catch regressions in functional + structural signals before merge.
**Inputs:** PR diff + main baseline.
**Outputs:** Status checks (pass/fail) + score deltas posted as PR comment.
**Agents:** None (CI only) for L1-L4. L5 LLM-judge is advisory and uses delta scoring (§0.4).
**Tool slots:** L1-L6 slots per §0.4. Tool cadences in `governance/mutation-policy.yaml` (or equivalent).
**Flow:**

```
PR open
   |
   v
  L1 static checks  ─┐
  L2 functional     ─┤── parallel
  L4 structural Δ   ─┘
        |
        v
  L3 test-strength check (scope: changed files; full run is periodic, not per-PR)
        |
        v
  any L1/L2 fail → block
  L4 delta posted as comment (trend only)
        |
        v
  L5 LLM-judge (delta vs co-scored main) posts advisory comment
        |
        v
  L6 auditor (if §0.4 triggers fired) → blocking on its rules
        |
        v
  merge gated on: L1 + L2 + reviewer-panel approval + L6 if triggered
```

**Exit criteria:** L1 + L2 green, L6 (if fired) green, no blocking finding from §B.4.
**Failure modes:** Treating L4 numbers as gates. They are not. They are signals.
**Knobs:** Tool cadences in `governance/mutation-policy.yaml`. L3 implementation choice deferred to starter-stack addendum.

---

## §C — Self-improvement loop (post-MVP)

Goal: continuously raise code quality without changing functionality, under hard budgets that prevent runaway loops AND that yield to active feature work.

**Activation:** Only after MVP is "feature-complete and functionally passing." Runs concurrently with §B feature work post-MVP, but with §C yielding (Q8 — see §C.6).

### §C.1 — Scan

**Goal:** Find candidate targets for improvement.
**Inputs:** Current main branch, current eval scores, periodic L3 ground-truth results, lint rules, ADRs.
**Outputs:** Prioritized list of (file, issue type, score) tuples. Stored as a transient queue.
**Agents:** `quality-prober` (scan mode).
**Tool slots:** L1, L4, L5 (slot capabilities from §0.4).
**Flow:**

```
main branch + governance/evals/rubrics/*
      |
      v
  check yield rule (§C.6): is any §B PR awaiting human review?
      ├── yes → skip this cycle
      └── no  → continue
      |
      v
  prober scans:
    - lint violations not yet fixed
    - L3 ground-truth misses (e.g., surviving mutants)
    - L4 deltas trending wrong way
    - L5 findings against rubric
      |
      v
  rank by (severity × confidence), filter by cooldown
      |
      v
  prioritized queue (top N where N ≤ remaining PR budget)
```

**Exit criteria:** Queue produced or empty (no candidates → loop idle, no cost).
**Failure modes:** Scan picks up noise. Mitigation: rubrics gate by severity; lint rules tuned so low-value rules don't enter the queue.
**Knobs:** Yield rule, severity thresholds, cooldown duration.

### §C.2 — Propose

**Goal:** Generate a fix proposal for one queued issue.
**Inputs:** One queue item.
**Outputs:** Branch under `bot/quality/**` with proposed diff + reasoning summary.
**Agents:** `quality-prober` (propose mode). Same boundaries as `code-author` — cannot write to `tests/acceptance/`, `fixtures/`, `governance/`.
**Tool slots:** Agent runtime.
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
**Failure modes:** Prober rewrites broad surface area. Mitigation: per-PR file-touch cap; breach → auto-reject.
**Knobs:** File-touch cap.

### §C.3 — Heterogeneous review

Identical to §B.4 (review panel + L6 auditor + validator), with one difference: the bar is higher — the proposal must demonstrably improve a measured signal (see §C.5).

### §C.4 — Apply (open PR)

**Goal:** Open the PR for human review or auto-merge per policy.
**Inputs:** Reviewed branch.
**Outputs:** PR on `main`.
**Agents:** `quality-prober`.
**Tool slots:** VCS CLI.
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
**Knobs:** Auto-merge policy.

### §C.5 — Re-eval + keep-or-revert (SICA rule)

**Goal:** Don't keep changes that didn't actually help.
**Inputs:** Merged PR.
**Outputs:** Either a stable merge or a revert PR.
**Agents:** Automated job (no agent reasoning).
**Tool slots:** §0.4 eval slots; L3 ground truth when its cadence ticks.
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
     └── no  → automatic revert PR; record reason; cooldown the file
```

**Exit criteria:** Either keep with recorded improvement, or revert with recorded reason.
**Failure modes:**
- Noisy metrics flip-flop. Mitigation: require improvement to exceed measurement noise floor.
- L3 ground-truth signal lags (runs periodically). Mitigation: when L3 cadence ticks, regressions in that interval's bot-PRs are reverted in batch.

**Knobs:** Noise floor; cooldown duration.

### §C.6 — Budgets & yield rule (Q8)

**Goal:** Prevent runaway loops AND keep feature work first-class.
**Tool slots:** CI scheduler + repo-level state file under `governance/bot-state.json` (read-only to agents, written by CI).
**Rules:**
- **Yield rule (Q8):** §C pauses new proposals while any §B PR is awaiting human review. §C compute can continue (scanning is cheap); only the PR-opening throttle is gated. Resumes when human-review queue drains.
- **Daily token budget** — exceeded → loop pauses until next day.
- **Max open `bot/quality/**` PRs** — default 5. Cap reached → scan halts.
- **Per-file cooldown** — after merge or revert, file off-limits for K days (default 30).
- **L3 health gate** — required green at its most recent run before next cycle. Red → quality loop disabled until human fixes.

---

## §Z — Decision log

All v0.1 open questions resolved (Q1–Q10). v0.2→v0.3 added Q11–Q13 (Q13 still open). Each row links the decision to where it lives in the doc.

| # | Question | Decision | Where it lives |
|---|---|---|---|
| Q1 | Probe budget | Findings-driven, no hard wall-clock; scope-dependent norm | §A.2 (exit criteria + knobs) |
| Q2 | Feature granularity | Per-feature decision at planning time; no global rule. The brief is the contract | §B intro |
| Q3 | Signoff gates | All four gates exist; each independently configurable per profile | §0.6 + per-phase knobs |
| Q4 | Model pairing | ≥2 model families required; specific pairing in `governance/judge.yaml` | §0.5 |
| Q5 | Judge re-baselining | Delta scoring + 30-50 anchor set + opportunistic upgrade w/ Spearman ≥ 0.85 | §0.4 L5 |
| Q6 | Mutation testing scope | Reframed: L3 is a "test-strength ground-truth" slot; mutation is one candidate, not THE one. Tool selection deferred | §0.4 L3 |
| Q7 | Auditor on `governance/` | Fires on bot-authored governance changes OR mixed-content PRs (governance + src/tests) | §0.4 L6 trigger rules |
| Q8 | §B/§C concurrency | Priority queue: §C pauses new proposals while §B review queue non-empty | §0.7 + §C.6 yield rule |
| Q9 | Tool naming | Capability-only main doc; sibling `agentic-sdlc-starter-stack.md` names concrete options | Design principles + sibling doc (TBD) |
| Q10 | Brownfield variant | Sibling doc `agentic-sdlc-brownfield.md` covers Extend + Rewrite scenarios | Sibling doc (TBD) |
| Q11 | "Why → build" gap | Added §A.1 living tech spec (`01-tech-spec.md`) — drafted pre-probe (bets + non-goals + open questions), updated in place post-probe. The agile spike loop (draft→spike→update); aims the probe at project-specific patterns | §A.1 + §A.2/§A.3 |
| Q12 | Per-feature task list | Durable, revisable phased task file in §B.3 (per-phase commit + findings compaction + fresh context); brief stays the contract. Matches industry Tasks→Implement + context engineering | §B.3 |
| Q13 | Agent identity tier | **OPEN** — provenance-grade vs authorization-grade; parked pending §0.2/§0.3 implementation | §0.3a |

### Sibling docs (not yet written)

- **`agentic-sdlc-starter-stack.md`** — one concrete tool option per slot in §0.4 / §0.5 / §B.6, marked illustrative and swappable.
- **`agentic-sdlc-brownfield.md`** — phase-by-phase delta from greenfield for Extend (add features to existing codebase) and Rewrite (modernize existing codebase) scenarios.

---

## Appendix — phase-flow at a glance

```
┌─────────────────────────────────────────────────────────────────────────┐
│  §0 Foundations: governance/, CODEOWNERS, agent roles, eval slots,      │
│  judge.yaml, signoff-policy.yaml, budgets + yield rule                  │
│  (built once at project init; read-only to coding agents)               │
└─────────────────────────────────────────────────────────────────────────┘
                                  │
              ┌───────────────────┴───────────────────┐
              │              §A Bootstrap             │
              │  A.0 north-star  →  A.2 throwaway     │
              │  probe (findings-driven; must hit     │
              │  hard-slice checklist; code           │
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
              │   → B.4 panel + L6 auditor → B.5 ADR  │
              │   delta → B.6 eval gate (L1–L6) →     │
              │   merge                               │
              │                                       │
              │   gates gov'd by signoff-policy.yaml  │
              │   feature size decided in B.1 brief   │
              └───────────────────┬───────────────────┘
                                  │
                          MVP feature-complete
                                  │
              ┌───────────────────┴───────────────────┐
              │   §C Self-improvement loop (budgeted) │
              │                                       │
              │   C.1 scan (yields to §B)  →  C.2     │
              │   propose  →  C.3 panel  →  C.4 PR    │
              │   →  C.5 keep-or-revert (SICA)        │
              │   →  cooldown                         │
              │                                       │
              │   caps: tokens | ≤5 open bot-PRs |    │
              │   per-file cooldown | L3 ground-truth │
              │   green required                      │
              │                                       │
              │   yield: §C pauses new proposals      │
              │   while §B review queue non-empty     │
              └───────────────────────────────────────┘
```

*v0.3 additions not yet redrawn above: §A is now 0-indexed (A.0 north-star) and inserts **A.1 tech spec** (`01-tech-spec.md`, draft→probe→update) between A.0 north-star and A.2 probe; **§B.3** produces a durable, revisable **phased task file** (per-phase commit → findings compaction → fresh context). Identity tier (§0.3a) is parked.*

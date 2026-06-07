# Stacked Review Orchestrator — design plan

> **Status:** Exploration / not part of the locked rebuild. This sketches a
> *new* capability (swipe-style review over a self-healing PR stack). It does
> **not** amend the rebuild specs in `PLANS/rebuild/` or the oracle. Treat as a
> proposal until promoted to a PRD + ADRs.

## 1. One-liner

Agents build a planned feature phase-by-phase without waiting for a human. Each
phase lands as a stacked PR rendered as a **card**. A human swipes:
**right → merge**, **left → reject with feedback**. A rejection rolls back that
phase, rebuilds it, **cascade-rebases every descendant**, re-derives their
correctness, and re-serves the affected sub-stack as fresh cards. The human is a
fast judgment layer; the machine owns all the bookkeeping.

## 2. Scope, and what is deliberately abstracted

This document specifies the **orchestration spine and its seams** — the state
machine, the restack engine, the conflict-tier ladder, and the contracts that
cross each boundary. Two things are intentionally **black boxes behind ports**,
because they are someone else's problem and must stay swappable:

- **`IPlanner` / `IPhaseBuilder` — "how a PR gets made."** Turning a goal into
  phases, and a phase into committed code + a PR, is opaque. We only consume the
  *result* (a `PhaseBuild`). Could be an LLM agent, a human, a script.
- **`IResolver` — "the actual thinking after a problem is surfaced."** Given a
  conflict and its context, producing a corrected diff is opaque reasoning. We
  only consume the *outcome* (resolved / needs-human / gave-up).

Everything else — branch/PR mechanics, the rebase cascade, the detect-conflict
ladder, the card lifecycle, approval invalidation — is **ours** and is specified
here. The decoupling rule: **every external capability is a port with a fake;**
the spine depends only on ports, never on a concrete agent/SCM/UI.

### Non-goals (here)

- The planning/decomposition algorithm.
- The agent's code-generation or repair strategy.
- The card UI's visual design (only its input/output contract).
- A specific SCM vendor. GitHub is the first adapter, not the model.

## 3. Domain model

```
Plan ──< Phase ──1:1── Node ──1:1── PullRequest
                          │
                          └─ parent: Node?      (the stack edge)
```

- **Plan** — a feature decomposed into ordered phases. Owns the stack's root
  (target branch, usually `main`).
- **Phase** — one reviewable unit of work. Carries the card template payload
  (what/why/risk/test-evidence). The atom of review.
- **Node** — the orchestrator's stateful wrapper around a phase: its branch, its
  PR, its parent edge, its status, and the **approval stamp** (see §5).
- **Stack** — the DAG of nodes rooted at the plan's target. Usually linear
  (`1→2→3`) but the model is a DAG; siblings are allowed.
- **Card** — the projection of a node into the review surface. One node ⇒ at
  most one *active* card.

A node's identity is stable across rebuilds; its **branch SHAs are not.** Never
key anything on a SHA.

## 4. Components (ports & adapters)

Each component is a port (interface). The spine is the only thing that knows the
graph; everything below is replaceable, faked in tests, and ignorant of the
others.

| Port | Owns | Abstracted? | First adapter |
|---|---|---|---|
| `IPlanner` | goal → ordered phases | **yes (black box)** | agent planner |
| `IPhaseBuilder` | phase → committed code + PR (`PhaseBuild`) | **yes (black box)** | agent builder |
| `IStackHost` | branch/PR CRUD, base retarget, merge | no | GitHub adapter |
| `IRestackEngine` | cascade-rebase a sub-stack in order | no | git-town / libgit2 |
| `IConflictClassifier` | run the oracle ladder, label a tier | no | rebase+build+test |
| `IResolver` | conflict + context → resolution | **yes (black box)** | agent resolver |
| `IReviewSurface` | emit cards, receive swipes | no | the app |
| `INotifier` | nudge the human when a card needs them | no | push/Slack |
| `Orchestrator` | the state machine + invariants | no (the spine) | — |

The **Orchestrator** is the only stateful coordinator. It consumes events from
`IReviewSurface` and `IPhaseBuilder`, drives `IRestackEngine` /
`IConflictClassifier` / `IResolver`, and persists node state. It contains the
business rules; the adapters contain the mechanism.

## 5. Node state machine

```
            ┌─────────────────────────────────────────────┐
            ▼                                             │
 building → queued → reviewing → ┬─ approved → merging → merged
                                 │                         │
                                 └─ rejected → rebuilding ─┘ (re-enters building)
                                                  │
   any ancestor change ──────────────────────────┴──► descendants forced to `queued`
                                                       (their approval stamp is voided)
```

### Two load-bearing invariants

1. **Merge gate.** A node may enter `merging` only when
   `parent.status == merged` **AND** `node.approvalStamp > parent.lastChangedAt`.
   This single rule produces "block all sequential merges of a rejected stack"
   for free — it is not special-cased.
2. **Approval is stamped against a content version, not a node.** Approving
   records `approvalStamp = node.contentVersion`. Any ancestor change bumps a
   descendant's `contentVersion`, so its old approval no longer satisfies the
   gate. **Code the human never saw can never merge.** This is the safety wall.

`lastChangedAt` / `contentVersion` is a monotonic counter per node, bumped on
every (re)build of itself or any ancestor.

## 6. The conflict-tier ladder

The restack of each descendant runs a **detect-then-escalate** pipeline, cheapest
oracle first. Git only sees tier 1; the compiler catches tier 2; tests catch
tier 3. (Background: `design/` discussion + Fowler's "Semantic Conflict".)

```
rebase (via syntax-aware merge driver)
  └ conflict markers?      → TIER 1 textual     → IResolver(markers)
build / typecheck
  └ fails?                 → TIER 2 referential → IResolver(compiler errors)
run the phase's tests
  └ fails?                 → TIER 3 semantic    → IResolver(parent-diff + failing tests)
all green                  → TIER 0 clean       → descendant valid, re-card
```

- **Tier 0/1** are mechanical; a syntax-aware merge driver (Mergiraf / weave) and
  `git rerere` resolve most without reasoning.
- **Tier 2** is deterministic from compiler output — cheap `IResolver` input.
- **Tier 3** is the frontier: only the **test suite** detects it and only
  **reasoning** resolves it. `IResolver` gets two inputs — *what the parent
  changed* and *which behavior broke* — and may need to **refactor**, not patch.
- **Coverage is infrastructure.** Tier-3 detection is bounded by the suite's
  behavioral coverage. Thin tests ⇒ semantic conflicts slip through as "green."
  State this as a hard dependency of the whole system.

`IConflictClassifier` returns a `ConflictTier` + evidence; the Orchestrator
decides whether to call `IResolver` or escalate to a human card (e.g. Tier 3 with
low confidence = a *product* decision, not a mechanical fix).

## 7. Core flows

### 7.1 Happy path

1. `IPlanner` → phases. `IPhaseBuilder` builds phase *n* on top of node *n-1*'s
   branch, opens a PR ⇒ node enters `queued`.
2. Orchestrator emits a card. Node `reviewing`.
3. Swipe right ⇒ `approved` (stamp recorded). Merge gate checked.
4. When parent is `merged` and stamp is fresh ⇒ `merging` via `IStackHost`;
   on success `merged`, descendants' bases retargeted.

### 7.2 Reject → rebuild → cascade (the whole point)

1. Swipe left + feedback on node *k* ⇒ node *k* `rejected`.
2. **Freeze the sub-stack:** every descendant of *k* → `queued`, approval stamps
   voided (invariant 2). Merge gate now blocks them all.
3. `IPhaseBuilder` rebuilds node *k* from the feedback ⇒ new SHAs, `contentVersion++`.
4. `IRestackEngine` cascade-rebases `k+1, k+2, …` **in order** onto the rebuilt
   parent. For each, run the §6 ladder; route conflicts to `IResolver`.
5. Each descendant that comes out green is re-served as a **fresh card** (its old
   approval is gone — the human re-judges the changed code).
6. Nodes outside *k*'s subtree are untouched.

### 7.3 Conflict escalation

`IConflictClassifier` labels a tier → Orchestrator calls `IResolver`. If the
resolver returns `Resolved`, continue the cascade. If `NeedsHuman` (or low
confidence on Tier 3), emit a card framed as *"the ground shifted under this
phase; here's my reconciliation — confirm?"* rather than silently merging
reinterpreted logic.

## 8. Contracts crossing the seams (shape, not detail)

Kept abstract on purpose — these are the only messages the spine knows.

- `PhaseBuild { NodeId, ParentNodeId?, BranchRef, PrRef, CardPayload }`
  — what `IPhaseBuilder` returns. *How* it was produced is opaque.
- `RestackRequest { rootNodeId }` → `RestackOutcome { perNode: NodeId → ConflictTier|Clean|Escalated }`.
- `ConflictReport { NodeId, Tier, Evidence (markers | compilerErrors | failingTests), ParentDiffRef }`
  — the `IResolver` input. The resolver returns
  `Resolution { Resolved(diff) | NeedsHuman(reason) | GaveUp(reason) }`.
- `Swipe { NodeId, Direction, Feedback? }` — from `IReviewSurface`.
- `CardPayload` — the templated phase summary (what/why/risk/test-evidence).
  Machine-parseable; the card UI is a pure function of it.

The spine never sees a prompt, a model, a diff algorithm, or a GitHub API shape.

## 9. Tech mapping — reuse vs. build

| Concern | Decision | Why |
|---|---|---|
| Stack/branch/PR mechanics | **reuse** `IStackHost` over GitHub (+ native Stacked PRs / `gh stack` when GA) | platform primitive; don't reinvent |
| Cascade rebase | **reuse** git-town (scriptable) or libgit2; wrap in `IRestackEngine` | restacking is solved, just orchestrate it |
| Tier 0/1 merges | **reuse** Mergiraf / weave merge driver + `git rerere` | kills false-positive textual conflicts; rerere replays the one human resolution across repeated cascades |
| Tier 2 detection | **reuse** the compiler/type-checker | typed lang (.NET) turns referential breaks into cheap deterministic signals |
| Tier 3 detection | **build thin** harness around the **test suite** | no SCM tool exists; tests are the only oracle |
| Tier 3 resolution | **abstract** behind `IResolver` | reasoning over behavior — the product's actual value |
| Merge ordering / "don't merge 2 before 1" | **reuse** a merge queue *or* enforce via §5 invariant 1 | either is fine; the invariant alone suffices early |
| Planning & building | **abstract** behind `IPlanner`/`IPhaseBuilder` | out of scope here |

Closest prior art to clone *conceptually*: **Composio Agent Orchestrator** — agents
in isolated worktrees, each own PR, self-healing CI, one dashboard. Its lifecycle
(*CI fails → fix; review requests changes → address; green+approved → notify to
merge*) is this spine minus the swipe skin and minus the cascade-rebase-on-reject.

## 10. Build order (walking skeleton first)

Mirror the rebuild's ethos: thinnest end-to-end thread, then deepen. Every step
ships with fakes behind the ports.

1. **Skeleton.** Linear 2-node stack, `FakePlanner`/`FakePhaseBuilder` (canned
   PRs), `FakeStackHost` (in-memory branches), no conflicts. Swipe right merges,
   gate enforced. Proves the state machine + invariants.
2. **Reject loop.** Add `rejected → rebuilding`, approval voiding, re-card. Still
   conflict-free. Proves the cascade *control flow*.
3. **Real SCM.** Replace `FakeStackHost` with GitHub; real branches/PRs/retarget.
4. **Restack engine.** Real cascade-rebase; Tier 0/1 only, `FakeResolver`
   returns markers-as-resolved. Add Mergiraf + rerere.
5. **Oracle ladder.** Wire build + tests ⇒ Tier 2/3 detection. `FakeResolver`
   still. Proves *detection*.
6. **Real resolver + cards.** Swap in the agent `IResolver` and the real review
   surface. Now it's the product.

Done-when per step: warning-free build, green tests, behavior *run* (not just
compiled), one coherent commit — same bar as the rebuild.

## 11. Open decisions

- **Reject intent.** One left-swipe can mean "redo this / tweak this / abandon
  this." Resolve via a follow-up sheet or 3-way swipe before building step 2.
- **Merge strategy.** Force **rebase-merge** for stacked PRs (squash orphans
  descendants — see the earlier stacking discussion). Decide whether `IStackHost`
  forbids squash on stacked nodes.
- **Resolver autonomy bound.** When may `IResolver` silently re-card vs. must it
  escalate? Propose: Tier 0–2 silent; Tier 3 escalates unless coverage +
  confidence exceed a threshold.
- **Sibling/DAG stacks.** Linear first; specify sibling merge ordering before
  allowing non-linear plans.
- **Persistence.** Where node state + stamps live across container restarts
  (this orchestrator is ephemeral; state must outlive it).
```

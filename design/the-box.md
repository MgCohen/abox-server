# The Box — design plan

> **Status:** Exploration / **not** part of the locked rebuild. Built *on top of*
> the L1→L12 spine — it consumes the rebuild's Flows/Steps (deterministic git Steps,
> agent Steps) and does **not** amend the oracle or `PLANS/rebuild/` specs. Treat as
> a proposal until promoted to a PRD + ADRs. **Cold-readable** (assumes no prior
> context). **Supersedes `stacked-review-orchestrator.md`** — that doc's mechanics
> (state machine, restack engine, conflict-tier ladder, ports, build order) are
> folded in here; where they differ, this doc governs.

## 1. One-liner

A human sets a **stream of work** in motion, then acts only as a **fast judgment
layer**. The machine plans it, builds it phase-by-phase **without waiting for per-step
approval**, and surfaces every choice the human must make as a swipeable **card**. Each
phase lands as a **stacked PR** inside a **Box** — the container that **wraps the whole
work-stream**: its PRs, the flow through them, the workspace/infra it runs on, the docs
that define it, and all of the run's throwaway scaffolding, on its own integration
branch. Review is **ground-up**; **left = approve, right = deny**. When the stack is
approved the Box **closes**: its scaffolding is stripped and a **single final PR** lands
the work on `main`. Many Boxes run at once.

A **Box is a stream of work, not necessarily a feature** — it may be a whole feature,
part of one, or any coherent stream. What makes it a Box is that it *wraps the
structure*: it owns the PRs and their flow, declares its infrastructure, carries its
defining docs, and holds its ephemerals until it's done and clean.

The **Inbox** that surfaces those cards is a **separate, parallel system** (§2.2) — the
Box is just one of its producers.

**Two aims shape every part below.** *Wrap as much determinism as possible* into
structured units — Boxes, Nodes, cards — that enforce behavior; and keep the
infrastructure **composable** — lifecycle stages, data-gather steps, and adapters
add / remove / swap as the workflow evolves, rather than hardcoding one shape. The
specifics below are the *current composition*, not a fixed law.

## 2. Two systems: the Box, and the Inbox

These are **independent systems** that meet at a seam. The Box is a work-stream
orchestrator; the Inbox is a general human-decision + notification surface. **A Decision
does not belong to a Box, and the Inbox does not belong to a Box** — the Box *emits*
decisions and notifications into the Inbox the way any producer could. We build the Box
feature (on top of agents + flow); the Inbox/Decision system is a **parallel track**.

### 2.1 The Box system

```
Box ──< Phase ──1:1── Node ──1:1── PullRequest        (Node.parent: Node?  ← the stack edge)
```

- **Box** — a **stream of work** on its **own integration branch**. Wraps the **stack**
  of PRs, the **flow** through them, its **workspace/infra profile** (§11), its
  **defining docs** (§4.0), and all **ephemeral artifacts** of the run (§4.3). Not
  necessarily a feature — any coherent stream.
- **Stack** — the ordered PRs inside a Box, rooted at the box branch. Linear to start
  (`1→2→3`); the model is a **DAG** so siblings can be added later without rework (§7).
- **Node** — the orchestrator's stateful wrapper around a phase (its branch, PR, parent
  edge, status, approval stamp). **Node identity is stable across rebuilds; branch SHAs
  are not — never key anything on a SHA.**

### 2.2 The Inbox system  *(parallel, independent)*

```
Inbox ──< Item ── { Notification | Decision }
                        Decision ── { PR-approval | binary(yes/no) | choice(A/B/N) | critical-confirm }
```

- **Inbox** — *everything the human receives*, across every Box and any other producer.
  The single delivery surface. Unifies what were two ports (a "notifier" and a "review
  surface") into one stream. **A standalone system the Box depends on — not a box part.**
- **Notification** — FYI (ack/dismiss; gates nothing); may carry a quick-action that
  promotes it to a Decision.
- **Decision** — an item that **gates work**: the producer waits on it. Subtypes:
  **PR-approval** (the stack card), **binary** (yes/no), **choice** (A/B/N — e.g. pick
  between speculative paths, §10), **critical-confirm** (high-stakes; extra friction,
  §6). General-purpose — the Box is the main producer today, but nothing about a
  Decision is box-specific.

## 3. Scope, and what is deliberately abstracted

This document specifies the **Box lifecycle, the Inbox, and the stack orchestration
spine** — the state machine, the two-level merge, the restack engine, the
conflict-tier ladder, and the contracts crossing each seam. The following are
**black boxes behind ports**, because they are someone else's problem and must stay
swappable:

- **`IPlanner` — "how a goal becomes a phased plan."** Planning is an **open
  back-and-forth conversation**, not a swipe — the agent does **not** auto-plan. That
  conversational flow (the ICS → specs → back-and-forth → a final plan file) sits
  upstream and is **out of scope here.** When the conversation settles it emits a phased
  plan; the Box consumes that plan and presents it for **approval** before building
  (§4.1). The **flow** is opaque, but its **output contract is pinned** (`Plan`, §14) so
  `FakePlanner` and the real planner agree.
- **`IPhaseBuilder` — "how a phase becomes committed code + a PR."** Could be an LLM
  agent, a human, a script. We consume the *result* (a `PhaseBuild`).
- **`IResolver` — "the reasoning after a conflict is surfaced."** Given a conflict and
  context, produce a corrected diff. We consume the *outcome* (resolved / needs-human
  / gave-up).

### Non-goals (here)

- The conversational planning/decomposition flow (the plan arrives as an input).
- The agent's code-generation or repair strategy.
- The card UI's visual design (only its input/output contract).
- A specific SCM vendor. GitHub is the first adapter, not the model.
- Building the workspace-isolation infrastructure (§11 designs the *seam*, not the rigs).

## 4. The Box lifecycle

The lifecycle is **composed, not hardcoded.** Each stage is a **Step in a Flow**
(reusing the rebuild's Step/Flow model): deterministic stages are plain code Steps,
agent-driven stages are agent Steps, and the sequence can be reordered or extended
without touching the spine. The stages below are the current composition.

```
  create ─► plan ─► plan-approved ─► open ─► building ─► reviewing(ground-up) ─► all-approved ─► closing ─► landed
     │       │           │ (decision)        │  (agent runs ahead, no per-step wait)           │
 author    open          │                   └─► emits stacked PRs onto the box branch         │ final PR box→main
 ICS docs  convo         └─ deny ⇒ back to planning conversation                               │ (critical-confirm),
 + pick    (not a                                                          interactive cleanup ┘ then strip .box/
 infra     card)                                                          (you choose what to keep) — det. Step
```

### 4.0 Creation (authoring the Box)

Creating a Box is a **guided authoring flow**, not a bare `mkdir`. It walks you through:

- **Defining docs (ICS format)** — **I**ntent · **C**onstraints · **S**uccess-criteria,
  per the [`ics-template.md`](ics-template.md) — written before planning so they anchor
  both the planning conversation and ground-up review.
- **Infrastructure choice** — the workspace/isolation profile this Box runs on (full
  clone / worktree / container-VM, §11) plus any per-Box overrides.

These inputs are committed into the Box (its `.box/` docs, §4.3) and parameterize
everything downstream.

### 4.1 The plan gate (entry)

Planning is an **open conversation** (§3, `IPlanner`), not a swipe — the agent won't
auto-plan. When the conversation settles into a **phased plan**, that plan is presented
for **approval** before any building — the cheapest place to redirect, since reshaping a
plan beats rejecting five PRs. Approve ⇒ the Box opens and building starts; deny ⇒ back
into the planning conversation. The Box doesn't open until a plan is approved.

### 4.2 Two-level merge

A Box has **its own integration branch**; stacked PRs target *it*, never `main`.

- **Level 1 (inside the Box):** as each PR is approved **ground-up**, it merges into
  the box branch via a **merge commit** (§16) — the merged phase's commits stay
  ancestors of the box branch, so descendants only **retarget** onto it (often
  automatically), no rebase. The box branch grows one approved phase at a time.
- **Level 2 (close):** when the whole stack is approved, the Box closes with **one
  final PR, box branch → `main`** — a `critical-confirm` decision. `main` only ever
  sees a single, fully-reviewed Box.

### 4.3 The `.box/` scaffolding + cleanup

The Box holds the run's **ephemeral artifacts** — the defining docs (§4.0), throwaway
plans, scratch notes, temporary rules/comments, intermediate analysis — under a
**`.box/` directory** on the integration branch. Agents read/write it like any code, so
it versions and PRs normally *inside* the Box.

Closing a Box runs an **interactive cleanup**: a decision asks **what to keep, discard,
or promote** (e.g. a hard-won note worth saving to real docs). On top of that human
choice sits a **mechanical guarantee**: a **deterministic close Step** (code, **never
the agent**) strips `.box/` from the **final PR**, so whatever is left as temporary
never reaches `main`. The human curates; the Step enforces.

## 5. The Inbox

Independence is established in §2.2; this section specifies the Inbox's *behavior*. One
stream, all Boxes, both item types interleaved.

- **Ordering:** **flat chronological** by default, with **filters** (by Box, by item
  type, criticals-only) when you want to focus. Simplicity over a priority engine; the
  agent keeps working on other Boxes while a card waits, so a "blocking-first" sort
  isn't needed to keep throughput up.
- **Notifications** are FYI (ack/dismiss) and may carry a **quick-action that promotes
  them to a Decision** (e.g. *"CI failed on phase 3 — [open as fix decision]"*). Items
  are tagged **box-scoped** ("phase 3 needs you") or **global** ("token expiring").
- **Decisions** gate work. PR-approval cards swipe **left = approve / right = deny**.
  Deny **forces a free-text note**; **gestures add optional scope hints**
  (e.g. *local-only* vs *breaks-downstream*) that bias the reject classifier (§8).
- **Criticality** is a tag that **escalates friction** — a `critical-confirm` requires
  extra confirmation and renders a **structured view of the file types you'd normally
  inspect** for that kind of change, rather than a bare diff.
- **Delivery** rides the existing transport ([`remote-access.md`](remote-access.md)):
  the Host pushes inbox updates over **SSE** to the phone/clients; no new pipe.
- **Identity (load-bearing):** the **read feed** (cards, diffs, CI status) is served
  **as the bot** (`ABox-Agent`); the **approve/merge action authenticates as you, the
  owner, from the phone** — never the bot. This is required by the agent/owner split
  (the bot cannot self-approve; see [`PLANS/agent-controls/`](../PLANS/agent-controls/README.md)).
  Keeping approval authority on the phone is *stronger* than approving on the build
  machine — the box where the agent runs never holds the power to land code.

## 6. Node state machine + the gates

```
            ┌───────────────────────────────────────────────┐
            ▼                                               │
 building → queued → reviewing → ┬─ approved → merging → merged(→box)
                                 │                           │
                                 └─ denied → (classify §8) ──┘
                                            quick-update → restack descendants, keep their state
                                            rebuild      → re-enters building, cascade §8

 merged(→box) ──[explicit reopen]──► revert + re-enter building (cascades to descendants)
 any ancestor change ─────────────► descendants forced to `queued`; stale approval voided
```

### Invariants

1. **Merge gate.** A node may enter `merging` only when
   `parent.status == merged` **AND** `node.approvalStamp` is fresh (matches current
   content). This single rule blocks merging a rejected stack out of order — not
   special-cased.
2. **Ground-up makes invalidation rare, not the primary mechanism.** Because review
   flows strictly bottom-up, **a descendant is never approved before its ancestor is
   settled** — so there is usually no stale approval to void. Approval-voiding (an
   ancestor change forcing descendants back to `queued`) is a **cheap backstop** for
   the rare out-of-order/concurrent case, not the workhorse. (This is why we don't
   need a heavyweight per-node diff-equality check; see §8.)
3. **Reopen is deliberate.** A phase already merged into the box branch is **frozen** —
   it won't re-card by accident. Reverting it is an **explicit `reopen` action** that
   reverts the box-branch merge and cascades to descendants. Normal swiping never
   reopens settled work.

`approvalStamp` / `contentVersion` is a monotonic counter per node, bumped on every
(re)build of itself or an ancestor.

## 7. Stack shape — linear first, DAG seam

Ship **linear stacks** (`1→2→3`) first: one parent edge per node, ground-up review,
sequential merge. Model the graph as a **DAG** from day one (a node's `parent` is a
nullable edge; nothing assumes a single child) so **sibling sub-stacks** — independent
phases reviewed/merged in parallel within a Box — drop in later without reshaping the
state machine. **Defer** sibling merge-ordering rules until we actually allow non-linear
plans.

## 8. Reject → classify → cascade

A **deny** never just stops the line; it triggers a scoped repair.

1. Deny on node *k* with a forced note (+ optional gesture hint) ⇒ node *k* `denied`.
2. **Classify the fix** (the gesture hint biases this; the note + diff inform it):
   - **Quick update** — the fix is local and does **not** change descendants'
     intended content ⇒ rebuild *k*, **restack descendants in place**; their state is
     preserved (nothing above *k* was approved yet under ground-up, so there's nothing
     to re-ask).
   - **Rebuild (breaking)** — the fix changes the contract descendants build on ⇒
     node *k* re-enters `building`; **cascade-rebuild/rebase `k+1, k+2, …` in order**
     onto the rebuilt parent, running the §9 ladder per descendant.
3. Each descendant that comes out green is (re-)served as a fresh card. Nodes outside
   *k*'s subtree are untouched.

The classifier is a **heuristic** (deliberately, per the approval decision): it decides
*how much to rebuild*, with the merge gate (§6.1) as the safety net if it's wrong. It
is **not** a diff-equality proof — ground-up ordering already removes the "approved
stale code merges" risk.

## 9. The conflict-tier ladder

Each descendant's restack runs a **detect-then-escalate** pipeline, cheapest oracle
first. Git sees tier 1; the compiler catches tier 2; tests catch tier 3.

```
rebase (syntax-aware merge driver)
  └ conflict markers?  → TIER 1 textual     → IResolver(markers)
build / typecheck
  └ fails?             → TIER 2 referential → IResolver(compiler errors)
run the phase's tests
  └ fails?             → TIER 3 semantic    → IResolver(parent-diff + failing tests)
all green              → TIER 0 clean       → descendant valid, re-card
```

- **Tier 0/1** are mechanical — a syntax-aware merge driver (Mergiraf / weave) +
  `git rerere` resolve most without reasoning.
- **Tier 2** is deterministic from compiler output — cheap `IResolver` input. .NET's
  type system turns referential breaks into reliable signals.
- **Tier 3** is the frontier: only the **test suite** detects it and only **reasoning**
  resolves it; `IResolver` gets *what the parent changed* + *which behavior broke* and
  may need to refactor, not patch.
- **Coverage is infrastructure.** Tier-3 detection is bounded by behavioral coverage —
  thin tests let semantic conflicts pass as "green." A hard dependency of the system.

`IConflictClassifier` returns a tier + evidence; the orchestrator decides whether to
call `IResolver` or escalate to a human card (§16, resolver-autonomy bound).

## 10. Speculative dual-path

The agent **keeps building under doubt** rather than blocking on the human. At a
divergence (path A vs path B), a **heuristic policy** (confidence × cost) decides
whether to **hedge** — build *both* paths on sibling branches — or just pick one. When
it hedges, the fork is surfaced as a **`choice` decision card**; you pick, and the
unchosen branch is **pruned**. The policy is tunable; the default leans conservative
(hedge only when confidence is low *and* the branch is cheap).

## 11. Workspace / infrastructure profiles

Different project types need different **workspace strategies**, and **the workspace
strategy and the security isolation tier are the same axis** — choosing one chooses
the trust boundary (see [`control-plane.research.md`](../PLANS/agent-controls/control-plane.research.md)
§3.3, §8).

| Project type | Workspace strategy | Why | Isolation tier |
|---|---|---|---|
| **Unity** | full project clone | worktrees break the Library/asset pipeline; no cloud session | separate dir, same host — *practical* |
| **C#** (this repo) | **git worktree** | cheap, shared object store (we run in one today) | same host — *practical* |
| **JS** | sandboxed VM / container | fully isolatable | container / microVM — *airtight* |

A Box carries a **workspace profile** behind a port (`IWorkspaceProvisioner`) that
materializes the worker's working copy. The profile is **declared per project** (the
repo states its profile), **overridable per Box**. **Design the seam now; build the
rigs later** — the implementation maps onto the control-plane research's isolation
spectrum (env-scrub → separate OS user → container → microVM). The airtight tiers
(VM/container) are also where the "agent can't reach a credential or the network"
guarantee becomes structural rather than conventional.

## 12. Persistence

**Hybrid.** GitHub is the source of truth for **PR/branch state** — don't mirror it. A
**Host-owned durable store** holds the orchestrator's *own* graph that GitHub can't
represent: nodes + parent edges, approval stamps / content versions, decisions and
their outcomes, Box metadata + lifecycle status, and pointers to `.box/` plans. The
Host is ephemeral; this store is what lets a Box survive a restart. On boot, GitHub is
queried to reconcile PR/branch facts against the stored graph.

## 13. Components — ports & adapters

The **Orchestrator** is the only stateful coordinator and the only thing that knows the
graph. Everything else is a port with a fake, ignorant of the others.

| Port | Owns | Abstracted? | First adapter |
|---|---|---|---|
| `IPlanner` | goal → approved plan file (via the out-of-scope planning flow) | **yes (black box)** | conversational planner |
| `IPhaseBuilder` | phase → committed code + PR (`PhaseBuild`) | **yes (black box)** | agent builder |
| `IStackHost` | branch/PR CRUD, base retarget, two-level merge | no | GitHub adapter |
| `IRestackEngine` | cascade-rebase a sub-stack in order | no | git-town / libgit2 |
| `IConflictClassifier` | run the §9 ladder, label a tier | no | rebase+build+test |
| `IResolver` | conflict + context → resolution | **yes (black box)** | agent resolver |
| `IInbox` | emit items (notifications + decisions), receive swipes | no | the app + SSE |
| `INodeProjector` | gather a node's review data deterministically → `CardPayload` (§14) | no | diff-summary + changed-class render |
| `IWorkspaceProvisioner` | materialize the worker's workspace per profile (§11) | no (seam now) | worktree; later clone / container |
| `Orchestrator` | the state machine + invariants + Box lifecycle | no (the spine) | — |

`IInbox` replaces the old `IReviewSurface` + `INotifier` pair (§2); for the Box build it's
a **high-level seam** (port + fake), fleshed out on its own track (§2.2). Every row is a
swappable seam: because each capability is a port with a fake, lifecycle stages and
data-gathering recompose without touching the spine (§1, §4).

## 14. Contracts crossing the seams (shape, not detail)

- `Plan { BoxRef, Phases: [ PlanPhase ] }` / `PlanPhase { Id, Intent, ParentId?, Acceptance }`
  — what `IPlanner` emits and the Box turns into Phase→Node (§2.1; `ParentId?` keeps it
  DAG-capable, §7). The authoring flow is out of scope (§3); this shape is fixed.
- `PhaseBuild { NodeId, ParentNodeId?, BranchRef, PrRef, CardPayload }` — what
  `IPhaseBuilder` returns; *how* it was produced is opaque.
- `RestackRequest { rootNodeId }` → `RestackOutcome { perNode: NodeId → ConflictTier | Clean | Escalated }`.
- `ConflictReport { NodeId, Tier, Evidence(markers | compilerErrors | failingTests), ParentDiffRef }`
  → `Resolution { Resolved(diff) | NeedsHuman(reason) | GaveUp(reason) }`.
- `InboxItem { Id, BoxId?, Kind(Notification | Decision), Tags, Critical, Payload }`.
- `Decision : InboxItem { Subtype, … }` — `Subtype` is the §2.2 enum.
- `Swipe { ItemId, Direction(approve | deny), Note?, ScopeHint? }` — deny requires Note.
- `CardPayload` — the review payload, assembled by `INodeProjector` through a
  **deterministic, customizable gather pipeline.** The PR diff lives on Git, but the
  *card's data* is wrapped on the backend: diff summary, **key classes changed +
  rendered**, the originating Task/Step info, CI status, risk/test-evidence. Each
  gather-step is composable (add/remove/swap), so what a card carries adapts to the
  workflow without changing the card contract. The card UI is a pure function of the payload.

The spine never sees a prompt, a model, a diff algorithm, or a GitHub API shape.

## 15. Build order & sequencing

Build order, workstream decomposition, and the done-when bar live in the **single
authoritative plan**, [`PLANS/the-box-implementation.md`](../PLANS/the-box-implementation.md)
— kept in one place so there is one sequencing story, not two. This doc stays *what/why*;
that doc owns *how/order*.

## 16. Open decisions / recommendations

Recommendations are leans, not locks:

- **Merge strategy** *(decided: merge commit)*: Level-1 stacked-node merges use a **true
  merge commit**, not squash or rebase-merge. Merge commit is the only method that keeps
  the merged parent's commits as **ancestors** of the box branch — squash and rebase-merge
  both rewrite those SHAs and orphan descendants, forcing a cascade-rebase on *every* clean
  merge. With merge commits a descendant needs only a **base retarget** on the happy path;
  the local cascade-rebase (§8) is reserved for **reject/rebuild**, where SHAs genuinely
  change. Box-branch history goes non-linear, but never reaches `main` cleanly anyway
  (Level 2 is one final PR + `.box/` strip). Level-2 (box → `main`) merge method is
  independent and unspecified here. (`research/stacked-prs.md` §1.)
- **Resolver autonomy bound** *(lean)*: Tier 0–2 resolve silently; **Tier 3 escalates
  to a card** unless coverage + confidence exceed a threshold.
- **Dual-path policy thresholds** (§10): the actual confidence/cost cutoffs — tune
  empirically.
- **Box abort/discard**: a Box that's abandoned mid-flight — drop the branch + `.box/`,
  emit a notification. Lifecycle completeness; specify before S2 (PR/git stack).
- **Sibling/DAG merge ordering** (§7): unspecified until non-linear plans are allowed.
- **Profile defaults** (§11): exact per-project declaration format + per-Box override
  mechanism.
- **Persistence store** (§12): concrete choice (embedded SQLite vs. document store) at
  B2.
- **Reject-intent gestures** (§5/§8): the gesture vocabulary for scope hints.
- **Inbox/Decision standalone design** (§2.2): authored in its own doc,
  [`inbox-decision.md`](inbox-decision.md) (the first S1 task). It governs the
  Inbox/Decision/Notification model — note it models the three as **independent
  concepts bridged by adapters**, refining §2.2's `InboxItem { Notification | Decision }`
  sketch (composition, not inheritance). §2.2/§5 here remain only the Box-facing seam.
- **ICS template** (§4.0): [`ics-template.md`](ics-template.md) is **provisional/minimal**
  — extend on real need.

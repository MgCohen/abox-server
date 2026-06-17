# The Box — implementation plan

> **Status:** Exploration / not part of the locked rebuild. The **how** that pairs
> with the **what/why** in [`design/the-box.md`](../design/the-box.md) — and the **single
> authoritative build order** (the design doc defers all sequencing here, §15). Built on
> top of the L1→L12 spine (it consumes the rebuild's Step/Flow + agent Steps) and does
> not amend the oracle or `PLANS/rebuild/` specs. Ordered **builds + tasks**; refine a
> build into concrete tasks when you start it.

## Strategy — substrate-first, Box composes on top

The Box is a **composition** over capabilities it does not own — decisions, PR/git,
workspace, projection, authoring flows (`design/the-box.md` §2). So build those
**independent capabilities first** (the *substrate*, S1–S6), each a first-class component
with its own home, then **compose the Box thinly** on top (B1–B3). This retires the
biggest design risk directly: building the shared systems *through the Box's lens* bakes
Box-shaped assumptions into things that aren't Box-specific.

**Guardrail — the Box is the forcing function.** Build "the decision / inbox / PR surface
the Box needs (plus obvious adjacent value)," **not** a speculative general system for all
futures. Each piece ships standalone, run-not-just-compiled, behind its own seam; the
Box's requirements drive the contracts even though the components land first (YAGNI /
least mechanism).

Principles carried over:
- **Wrap determinism in composable units** — lifecycle stages and node data-gathering are
  Steps you add/remove/swap (`design/the-box.md` §1, §4, §14).
- **Determinism first, reasoning last** — the deterministic substrate is independent of
  the LLM; the agent-coupled bits (builder, resolver, planner) trail at the very end.
- **Box and Inbox are separate systems** (`design/the-box.md` §2) — now expressed
  *structurally*: the Inbox/Decision is substrate (S1), not a Box component.

## Placement (repo pattern)

Box and substrate code follow the repo's existing pattern: **`Domain/<Concept>`** for the
model + **`Features/<Feature>`** for the vertical slice (endpoints, contracts, module, SSE
`Watch`) — exactly as `Domain/Flow` + `Features/Flows` and `Domain/Projects` + `Features/Git`
do today. The Box itself is `Domain/Box/` + `Features/Box/`; each substrate capability gets
its own `Domain/<Concept>` (+ a `Features/` slice where it exposes endpoints). **No new
assemblies** — folders in the existing solution (YAGNI / least mechanism).

## Build map

| # | Build | Home / port | Seed to grow | Depends on |
|---|---|---|---|---|
| **S1** | Decision / Inbox / Notification | `Domain/Inbox` · `IInbox` | `Domain/Flow/Operations`, `Domain/Agents` decision model | — |
| **S2** | PR / Git stack | `Features/Git` · `IStackHost` | `IPullRequests`, `MergeResult`, `StubPullRequests` | — |
| **S3** | Workspace provisioning | `Domain/Workspace` · `IWorkspaceProvisioner` | — | — |
| **S4** | Node projection | `Domain/Box` · `INodeProjector` | — | S2 |
| **S5** | Transport + identity (+ client) | `Features/*/Watch` (SSE) | `Features/Flows/Watch/Sse`, `remote-access.md` | S1, S4 |
| **S6** | ICS authoring flow | `Domain/Flow` (reuse) | Flow/Steps engine | S1 |
| **B1** | Box domain + state machine | `Domain/Box` · `Orchestrator` | — | (consumes S1) |
| **B2** | Lifecycle (composed Flow) | `Domain/Box` · `Features/Box` | `Domain/Flow` | B1, S2, S4, S6 |
| **B3** | Cascade restack + conflict-tier | `IRestackEngine`, `IConflictClassifier` | — | B1, S2 |
| **—** | Agent frontier (last) | `IPhaseBuilder`, `IResolver`, `IPlanner` | — | B-tier |

**First mover (lean):** **S1**, with **S2 in parallel.** S1/S2/S3 are mutually independent;
S1 unblocks the most downstream (S5, S6, the whole inbox UX), while S2 carries the highest
*technical* risk (git mechanics) so it's worth starting early too.

---

## Substrate

### S1 — Decision / Inbox / Notification  *(the parallel system)*

**Goal:** a standalone human-decision + notification system — **its own**, not a Box part
(`design/the-box.md` §2.2). **Extend the existing decision model**, don't greenfield:
`Domain/Flow/Operations` (`IDecisionSource`, `DecisionDto`) and `Domain/Agents`
(`PendingDecision`, `IDecisionResolver`, `DecisionKind`, `Resolution`).

- **First task: author its own design doc** (non-Box producers, routing, decision
  persistence) — `design/the-box.md` §2.2/§5 are only the Box-facing seam (§16).
- General model: notification + decision items; decision subtypes (PR-approval, binary,
  choice, critical-confirm); flat-chronological + filters; criticality friction;
  notification→decision promote-action (`design/the-box.md` §5).
- Swipe semantics: left=approve / right=deny, **forced note on deny**, gesture scope-hints.

**Independent?** Fully — Box-agnostic. **Done-when:** a **test producer** raises
notifications/decisions; they're queried/filtered; swipes round-trip with the deny-note
rule enforced. (Delivery + client are S5.)

### S2 — PR / Git stack  *(highest technical risk)*

**Goal:** branch/PR mechanics against a **throwaway GitHub repo** — fully testable without
any LLM. **Grow `Features/Git`** (`IPullRequests`, `MergeResult`, `StubPullRequests`,
`PrList`/`PrMerge`) into `IStackHost`.

- Branch/PR CRUD, **base retarget**, **rebase-merge** primitive (`design/the-box.md` §16).
- The genuine unknowns live here — rebase-merge rewrites SHAs, so descendants must
  retarget onto the merged parent. **Prove this early** (a throwaway spike is the natural
  way, if we choose to — not locked).
- Identity wiring: **read-as-bot** here; approve-as-owner is S5.

**Independent?** Yes (canned branches/diffs, no agent). **Done-when:** a hand-made
2-branch stack opens, rebase-merges one onto a base, retargets the descendant — all driven
by test code against a throwaway repo.

### S3 — Workspace provisioning  *(infrastructure)*

**Goal:** materialize a worker's working copy per **profile**, behind a port.

- `IWorkspaceProvisioner` + the profile model (declared per project, override per Box).
- **Worktree adapter first** (this repo's tier). Stub `full-clone` (Unity) and
  `container/VM` (JS) as named, unimplemented profiles (`design/the-box.md` §11).
- Map profiles to the control-plane isolation tiers
  ([`agent-controls/control-plane.research.md`](agent-controls/control-plane.research.md)).

**Independent?** Yes. **Done-when:** worktree provisioning works end-to-end; profile
selection + override tested; other profiles fail loud as "not yet built."

### S4 — Node projection  *(wrap the determinism)*

**Goal:** turn a PR/Node into a review-ready `CardPayload` via a **composable gather
pipeline** (`design/the-box.md` §14).

- `INodeProjector` + gather-steps: diff summary, **key classes changed + rendered**,
  originating Task/Step info, CI status, risk/test-evidence. Each step add/remove/swap.
- Criticality tagging + the structured "file types you'd normally inspect" view.

**Independent?** Depends on S2 git; no agent. **Done-when:** a real PR projects to a
complete, deterministic `CardPayload`; pipeline recomposable in a test.

### S5 — Transport + identity (+ client)  *(the surface)*

**Goal:** deliver the inbox to a human and let them act with the right identity.

- **SSE delivery** over the existing transport ([remote-access](../design/remote-access.md),
  reusing `Features/Flows/Watch/Sse`) — no new pipe.
- The swipe client (contract + thin view) rendering S4 payloads.
- **Approve-as-owner from the phone** / read-feed-as-bot (`design/the-box.md` §5;
  [agent-controls](agent-controls/README.md)).

**Independent?** Mostly (needs S1 items + S4 payloads). **Done-when:** items stream to a
client over SSE, swipes round-trip, approve authenticates as **owner** against the S2
throwaway repo.

### S6 — ICS authoring flow  *(guided authoring)*

**Goal:** a guided back-and-forth that produces an ICS doc ([`ics-template.md`](../design/ics-template.md)
shape) — Box creation (`design/the-box.md` §4.0) consumes its output.

- **Composes** `Domain/Flow` (Flow/Steps) + S1 (decision/inbox for the prompts).
- The **bounded sibling of the planning flow** (fixed template → finite Q&A); building it
  first de-risks the open-ended `IPlanner` later. Possibly one "guided authoring flow"
  substrate with two configs.

**Independent?** Composes S1 + Flow. **Done-when:** a guided flow turns a finite Q&A into a
valid ICS doc; its steps are recomposable.

---

## Box  *(composes the substrate)*

### B1 — Box domain + state machine  *(the novel core)*

**Goal:** the Box graph and its rules as pure, testable code.

- `Domain/Box`: `Box`, `Phase`, `Node`, `Stack` (stable id, nullable `parent` edge →
  DAG-capable); the **pinned input contract** `Plan`/`PlanPhase` (`design/the-box.md` §14).
- Node state machine + the three invariants (merge gate, ground-up backstop, deliberate
  reopen) as pure logic (`design/the-box.md` §6).
- Consumes the **S1 decision port** + a `Persistence` port (in-memory fake; real store at B2).

**Done-when:** state machine + invariants proven by unit tests; warning-free, green.

### B2 — Lifecycle (composed Flow) + integration thread

**Goal:** compose the substrate into the end-to-end Box lifecycle — **the thin vertical
thread**, with the agent faked.

- **Lifecycle as a composed Flow of Steps** (reuse `Domain/Flow`), stages add/remove/swap
  (`design/the-box.md` §4): create/author (**S6**) → plan-gate (decision via **S1**) →
  building (`FakePhaseBuilder`) → ground-up review (**S4** payloads via **S5**) →
  **two-level merge** (**S2** primitives; box→main + deterministic `.box/` strip Step) →
  **interactive close-cleanup** (decision via **S1**).
- The durable **persistence store** behind B1's port (hybrid: GitHub = PR truth, store =
  graph) (`design/the-box.md` §12).

**Done-when:** one Box goes create→plan→build(faked)→ground-up approve→two-level
merge→close→final PR, driven only by swipes + fakes; the §6 invariants hold **live**.

### B3 — Cascade restack + conflict-tier ladder

**Goal:** self-healing cascade on reject/rebuild.

- `IRestackEngine` cascade-rebase in order; `IConflictClassifier` running the Tier 0–3
  ladder (Mergiraf/`rerere` → build/typecheck → tests) (`design/the-box.md` §9).
- Reject → classify (quick-update vs rebuild) → cascade (`design/the-box.md` §8).
- `FakeResolver` returns canned resolutions.

**Done-when:** a denied node rebuilds, descendants cascade-restack with tiers detected,
reopen works. Detection proven; resolution still faked.

---

## Agent frontier  *(last — the interconnected, fuzziest bits)*

Deferred on purpose — these need the substrate + Box above and carry the most
coupling/uncertainty.

- **`IPhaseBuilder`** — the agent that *does the work* (phase → code + PR).
- **`IResolver`** — the agent that resolves Tier 2/3 conflicts (`design/the-box.md` §9, §16).
- **`IPlanner`** — the open-ended conversational planning flow (**out of scope today**;
  `design/the-box.md` §3). **S6 is its bounded prototype.**
- **Speculative dual-path** — the heuristic hedge-and-prune policy (`design/the-box.md` §10).

**Done-when:** real agents replace the B2/B3 fakes; the product runs for real.

---

## Why substrate-first

The Box is mostly *composition*; its genuinely novel, owned part is small (the stack state
machine, two-level merge, cascade restack, conflict-tier ladder). The unknowns it depends
on — decisions, git mechanics, projection — are **used by** the Box, not **owned by** it.
Building them first means: (1) each is independently testable and useful on its own (a real
decision/inbox even helps the current flows UI); (2) the shared systems don't get
Box-shaped; (3) by B2 the Box is a thin, demonstrable thread over proven components instead
of plumbing-plus-novelty debugged at once. The agent-coupled bits — the fuzziest problems —
plug into a finished harness at the very end.

## Done-when bar (every build)

Same as the rebuild: warning-free build, green tests, behavior **run** (not just
compiled), one coherent commit. Each build ships behind its seam with a fake so the next
can start.

## Open sequencing risks

- **Speculative generality on S1 / S6.** The biggest trap of substrate-first: over-build a
  "general" inbox/authoring flow. Keep the Box as the forcing function — scope to its needs
  plus obvious adjacent value, nothing more.
- **S2 git unknowns are the top technical risk** — rebase-merge + retarget + cascade. Start
  early; a throwaway spike is the natural way to retire it (not locked).
- **Persistence store choice** (embedded SQLite vs document store) lands in **B2** — decide
  then (`design/the-box.md` §16).
- **Tier-3 detection is bounded by test coverage** (B3) — thin tests let semantic conflicts
  pass as green (`design/the-box.md` §9).
- **`IPlanner` out-of-scope** — the agent frontier may pull in the separate planning-flow
  design; treat it as its own track, with **S6 as the bounded prototype**, not a blocker.

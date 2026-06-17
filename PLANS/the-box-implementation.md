# The Box — implementation plan

> **Status:** Exploration / not part of the locked rebuild. The **how** that pairs
> with the **what/why** in [`design/the-box.md`](../design/the-box.md) — and the **single
> authoritative build order** (the design doc defers all sequencing here, §15). Built on
> top of the L1→L12 spine (it consumes the rebuild's Step/Flow + agent Steps) and does
> not amend the oracle or `PLANS/rebuild/` specs. Ordered **workstreams + tasks**; refine
> a workstream into concrete tasks when you start it.

## Strategy — independent-first, ports-and-fakes

Build the **loosely-coupled, deterministic** parts first; defer the **agent-coupled**
parts (doing the work, resolving conflicts, planning) to the end. The seam that makes
this possible is already in the design: **every capability is a port with a fake**
(`design/the-box.md` §13), so each piece is built and tested against fakes of its
neighbors and integrated late.

This **reconciles with the repo's walking-skeleton ethos** rather than replacing it:
each workstream still ships standalone, run-not-just-compiled, behind a port; the thin
end-to-end thread is **WS5**, once the independent pieces are real. The difference is
*order* — we front-load the parts that don't need an agent.

Three principles from the design drive the decomposition:
- **Wrap determinism in composable units** — lifecycle stages and node data-gathering
  are Steps you add/remove/swap (`design/the-box.md` §1, §4, §14).
- **Determinism first, reasoning last** — the deterministic spine (git, projection,
  lifecycle) is independent of the LLM; the LLM-coupled ports come last.
- **Box and Inbox are separate systems** (`design/the-box.md` §2). We build the **Box
  feature** on top of agents + flow; the **Inbox/Decision/Notification** system is a
  **parallel, independent track**. A Decision does not belong to a Box. For the Box
  build, the Inbox is only a **high-level seam** (`IInbox`/decision port + fake); the
  real Inbox system + client is fleshed out on its own track and integrated late.

## Dependency map

| Component | Port | Depends on | Coupling | When |
|---|---|---|---|---|
| Domain core + composable lifecycle | `Orchestrator` | — | low (pure) | WS0 |
| Workspace provisioning | `IWorkspaceProvisioner` | WS0 | low | WS1 |
| PR/Git connection | `IStackHost` | WS0 | low (deterministic, throwaway repo) | WS2 |
| Node projection (data-gathering) | `INodeProjector` | WS0, WS2 | low (deterministic) | WS3 |
| **Inbox/Decision system + client** *(parallel track)* | `IInbox` | WS3, [remote-access](../design/remote-access.md) | low–med | WS4 |
| Integration thread (fakes for agent) | — | WS0–WS4 | med | WS5 |
| Restack + conflict-tier ladder | `IRestackEngine`, `IConflictClassifier` | WS2, real build/test | med | WS6 |
| **Agent build** | `IPhaseBuilder` | WS5 | **high** | WS7 |
| **Conflict resolver** | `IResolver` | WS6 | **high** | WS7 |
| **Planning flow** | `IPlanner` | WS5 | **high (out of scope today)** | WS7 |
| Speculative dual-path | — | WS7 builder | **high** | WS7 |

---

## WS0 — Domain core + composable lifecycle  *(the deterministic spine)*

**Goal:** the Box/Stack model and its rules as pure, testable code — no GitHub, no
agent, no UI.

- Domain records: `Box`, `Phase`, `Node` (stable id, nullable `parent` edge so the
  graph is DAG-capable), `Stack`. **Box-only — no Inbox/Decision types here** (those
  belong to the parallel WS4 system; the Box consumes them through a port).
- The **pinned input contract** `Plan`/`PlanPhase` (`design/the-box.md` §14) the Box
  turns into Phase→Node — so `FakePlanner` (WS5) and the real planner (WS7) agree.
- The **create/author stage** consumes an ICS doc per
  [`design/ics-template.md`](../design/ics-template.md) (§4.0).
- Node state machine + the three invariants (merge gate, ground-up backstop, deliberate
  reopen) as pure logic (`design/the-box.md` §6).
- **Lifecycle as a composed Flow of Steps** — stages (incl. **create/author** and
  **interactive close-cleanup**) are add/remove/swap, reusing the rebuild's Step/Flow
  model (`design/the-box.md` §4). Deterministic stages = code Steps.
- The **`IInbox`/decision seam** — a high-level port + **in-memory fake** so the
  lifecycle can raise decisions (plan-approval, close-cleanup) without the real Inbox
  system existing yet (`design/the-box.md` §2.2, §13).
- `Persistence` port + an **in-memory fake** (real durable store deferred to WS2/WS4).
- Fakes for every neighbor port.

**Independent?** Fully. **Done-when:** state machine + invariants proven by unit tests;
a lifecycle composed/recomposed from Steps in a test; warning-free, green, one commit.

## WS1 — Workspace provisioning  *(infrastructure)*

**Goal:** materialize a worker's working copy per **profile**, behind a port.

- `IWorkspaceProvisioner` + the profile model (declared per project, override per Box).
- **Worktree adapter first** (this repo's tier — we run in one today).
- Stub the `full-clone` (Unity) and `container/VM` (JS) adapters as named, unimplemented
  profiles so the seam is real (`design/the-box.md` §11).
- Map profiles to the control-plane isolation tiers
  ([`agent-controls/control-plane.research.md`](agent-controls/control-plane.research.md)).

**Independent?** Yes. **Done-when:** worktree provisioning works end-to-end; profile
selection + override tested; other profiles fail loud as "not yet built."

## WS2 — PR / Git connection  *(deterministic, no agent)*

**Goal:** all branch/PR mechanics against a **throwaway GitHub repo** — fully testable
without any LLM.

- `IStackHost` GitHub adapter: branch/PR CRUD, base retarget, **two-level merge**
  (rebase-merge; PR→box branch, box→main) (`design/the-box.md` §4.2, §16).
- The **`.box/` strip Step** (deterministic) + final-PR mechanics (`design/the-box.md` §4.3).
- The durable **persistence store** behind WS0's port (hybrid: GitHub = PR truth, store
  = orchestrator graph) (`design/the-box.md` §12).
- Identity wiring: **read-as-bot** here; approve-as-owner deferred to WS4.

**Independent?** Yes (canned branches/diffs, no agent). **Done-when:** a hand-made
2-node stack opens, merges ground-up into a box branch, closes with `.box/` stripped and
a final PR — all driven by test code.

## WS3 — Node projection (deterministic data-gathering)  *(wrap the determinism)*

**Goal:** turn a Node into a review-ready `CardPayload` via a **composable gather
pipeline** (`design/the-box.md` §14) — the core of "wrap determinism in structured
units."

- `INodeProjector` + gather-steps: diff summary, **key classes changed + rendered**,
  originating Task/Step info, CI status, risk/test-evidence.
- Each gather-step is add/remove/swap; the payload shape is configuration.
- Criticality tagging + the structured "file types you'd normally inspect" view.

**Independent?** Yes (depends on WS2 git + WS0 model; no agent). **Done-when:** a real PR
projects to a complete, deterministic `CardPayload`; pipeline recomposable in a test.

## WS4 — Inbox/Decision system + client  *(parallel, independent track)*

**Goal:** the standalone human-decision + notification system and its surface. **This is
its own system, not a box component** (`design/the-box.md` §2.2) — the Box only depends
on its high-level seam (a fake from WS0). Built independently and integrated at WS5.

- **First task: author its own design doc** (the standalone Inbox/Decision design —
  non-Box producers, decision routing + persistence). `design/the-box.md` §2.2/§5 are
  only the Box-facing seam; this track owns the full design (`design/the-box.md` §16).

- `IInbox` + the **general Decision/Notification model**: decision subtypes
  (PR-approval, binary, choice, critical-confirm), notification + promote-to-decision,
  **flat-chronological + filters**, criticality friction (`design/the-box.md` §5).
  Box-agnostic — a Box is just one producer.
- **SSE delivery** over the existing transport ([remote-access](../design/remote-access.md)) —
  no new pipe.
- The swipe client (contract + thin view): left=approve / right=deny, **forced note on
  deny**, gesture scope-hints.
- **Approve-as-owner from the phone** / read-feed-as-bot
  (`design/the-box.md` §5; [agent-controls](agent-controls/README.md)).

**Independent?** Yes — fully standalone; renders WS3 payloads but needs no Box to exist.
**Done-when:** decisions/notifications stream to a client, swipes round-trip, approve
authenticates as owner against the WS2 throwaway repo — exercised by a test producer, no
Box required.

## WS5 — Integration thread  *(thin vertical, fakes for the agent)*

**Goal:** prove the whole spine end-to-end **before any real agent.**

- Wire WS0–WS4 with `FakePlanner` (canned approved plan) + `FakePhaseBuilder` (canned
  PRs).
- Run: plan-approval card → agent-less stacked PRs → ground-up approve → two-level
  merge → close → final PR.

**Done-when:** one Box goes plan→landed driven only by swipes + fakes; the §6 invariants
hold live (run it, not just compiled).

## WS6 — Restack + conflict-tier ladder  *(more coupled, still no LLM)*

**Goal:** self-healing cascade on reject/rebuild.

- `IRestackEngine` cascade-rebase in order; `IConflictClassifier` running the Tier 0–3
  ladder (Mergiraf/`rerere` → build/typecheck → tests) (`design/the-box.md` §9).
- Reject → classify (quick-update vs rebuild) → cascade (`design/the-box.md` §8).
- `FakeResolver` returns canned resolutions.

**Done-when:** a denied low node rebuilds and descendants cascade-restack with tiers
detected; reopen action works. Detection proven; resolution still faked.

## WS7 — Interconnected last  *(the agent-coupled frontier)*

Deferred on purpose — these need the spine above and carry the most coupling/uncertainty.

- **`IPhaseBuilder`** — the agent that *does the work* (phase → code + PR).
- **`IResolver`** — the agent that resolves Tier 2/3 conflicts (`design/the-box.md` §9, §16).
- **`IPlanner`** — the conversational planning flow that emits the approved plan file
  (**currently out of scope**; `design/the-box.md` §3).
- **Speculative dual-path** — the heuristic hedge-and-prune policy (`design/the-box.md` §10).

**Done-when:** real agents replace the WS5/WS6 fakes; the product runs for real.

---

## Why the agent bits are last

The agent is the **least deterministic and most coupled** part. Building the git
mechanics, the deterministic projection, the inbox, and the lifecycle first means: (1)
each is independently testable without an LLM in the loop; (2) by WS5 we have a working,
demonstrable system driven by fakes; (3) when the agent lands, it plugs into proven
seams instead of being debugged simultaneously with everything else. "How to plan" and
"how to do the work" are the two hardest, fuzziest problems — they benefit most from a
solid, finished harness underneath.

## Done-when bar (every workstream)

Same as the rebuild: warning-free build, green tests, behavior **run** (not just
compiled), one coherent commit. Each workstream ships behind its port with a fake so the
next can start.

## Open sequencing risks

- **WS4 before a real builder** assumes `FakePhaseBuilder` payloads are representative —
  keep the fake's `CardPayload` shape honest against WS3.
- **WS6 needs real code to conflict on** — it can run against the WS2 throwaway repo with
  scripted diffs; doesn't need WS7.
- **Persistence choice** (embedded SQLite vs document store) lands in WS2 — decide then,
  not now (`design/the-box.md` §16).
- **`IPlanner` out-of-scope** means WS7 may pull in the separate planning-flow design;
  treat that as its own track, not a blocker for WS0–WS6.

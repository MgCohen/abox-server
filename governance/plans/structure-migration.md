---
type: plan
status: draft
tags: [#architecture, #migration, #vsa, #assemblies, #flow, #agents, #signalr]
related: [[architecture-proposal]] [[structure]] [[rebuild]]
---

# Structure migration — apply the VSA/assembly-wall layout to the built spine

> **What this is.** A concrete, ordered plan to migrate the **already-built
> spine** out of technical-layer folders (`Engine/`, `Actors/`, `Tools/`) and
> into the converged structure from [`architecture-proposal.md`](architecture-proposal.md)
> / [`structure.md`](structure.md). It is the bridge from "rebuilt in layers" to
> "organized by capability + walls."
>
> **What this is NOT.** Not a redesign — the target structure is already settled;
> this only *relocates* and *walls* what exists. Not a build of new capabilities.

---

## 1. Scope — only what's actually built

The capability map names many features (Validation, Evaluation, Tasks,
Project-Setup, Notifications, …). **This migration touches none of them.** It
deals only with what is implemented and proven:

- **Flows** — the flow engine + its HTTP surface (`/flows`, `/catalog`).
- **Agents** — the agent runtime (providers, PTY, billing safety, decision intercom).
- The **supporting floor** they already stand on (subprocess, paths, json) and
  the **transport/contracts** that serve them.

Everything mapped-but-unbuilt stays on paper until it's built — it is **out of
scope by definition**, not deferred work in this plan.

### Guardrails for this pass (explicit)

1. **No speculative shared code.** We do **not** invent a generic
   `IRepository<T>`, an event bus, a mediator, a `Result<T>`, or a `Kernel` just
   because the target structure has slots for them. `FileFlowHistory` stays a
   **concrete file store** living with the Flows feature. Abstractions are
   extracted **later** — in Movement 2 when a detected dependency forces one (via
   the sharing ladder), or in the final opportunistic review — never assumed now.
2. **SignalR is in.** It's a known need (bidirectional intercom across web/PC/
   mobile), so we adopt it in this pass rather than re-hand-rolling SSE. This is
   the *one* library we pull now; everything else (Wolverine/MediatR/Marten)
   stays deferred. It lands in **Movement 2** (a new behavior, not organization).
3. **Organize first, fix dependencies second — don't interleave.** Movement 1
   puts every file in its correct assembly **per the proposal as written** (one
   per use case, tight) and adds whatever `<ProjectReference>` is needed to
   **compile — including edges the reference graph forbids** (slice→slice). We do
   **not** judge or refactor those edges during organization. Movement 2 then
   *detects* the forbidden edges and resolves each (Contract/event, downward
   port, or extract-to-`Domain`). Separating *placement* from *dependency shape*
   keeps each decision clean and avoids one-step-at-a-time paralysis.
4. **Relocation, not rewrite.** Files move; behavior is identical. Movement 1
   ends with a **green build + green behavior tests** (the reference-graph test is
   the deliberate exception — see §3); one coherent commit per step.
5. **UI is last; validate by running, not compiling.** No visual UI work until
   the final phase — every earlier phase is backend/transport only and is proven
   by *running the spine* (the V0→V1→V2 gates in §3), not by a green build alone.
   "UI" = visual Blazor screens/components (the *fake UI*); the Web project's
   API/transport plumbing (`HostApiClient`, the SignalR client) is slice plumbing,
   not UI, and may change in Movement 2.

---

## 2. Source → destination map (coarse)

Current layout → target. Bold = a wall that earns its assembly in this pass.

| Today (`src/`) | Moves to | Assembly? |
|---|---|---|
| `ABox/Actors/Agents/**` (Agent, providers, resolvers, intercom) | **`Domain/Agents/`** | **Y — walled** |
| `Actors/Agents/Claude/**`, `Actors/Agents/Codex/**` | `Domain/Agents/` (internal) | (same) |
| `Tools/CommandLine/PtySession.cs` | `Domain/Agents/` (**`internal`**) | (same) — the spawn wall |
| `Tools/CommandLine/SubscriptionGuard.cs` (billing safety) | `Domain/Agents/` | (same) |
| `Tools/CommandLine/{SubprocessSession,RunCommand,Shell,AnsiHelpers,RunCommand*}.cs` | `Infrastructure/` | Y (floor) |
| `Tools/Json/JsonLine.cs`, `Tools/Paths/**` | `Infrastructure/` | (floor) |
| `ABox/Engine/Flows/**` + `Engine/Operations/**` | `Features/Flows/Shared/` (feature engine) | Y |
| `Engine/Flows/{IFlowHistory,FileFlowHistory}.cs` | `Features/Flows/Shared/` (concrete store — **stays concrete**) | (same) |
| `Host/Endpoints.cs` handlers (start/list/get/cancel/watch) | `Features/Flows/{Start,List,Get,Cancel,Watch}/` — **one assembly each** | Y |
| `Host/Sse.cs`, `Web/Api/FlowStreamClient.cs` | **deleted** → SignalR (Movement 2B) | — |
| `ABox.Contracts/**` (flow DTOs) | `Features/Flows/Contracts/` | Y (leaf) |
| `Actors/Git/**` | independent `Features/Git/` slice in M1; → `Infrastructure` later when shared (**OQ-6**) | Y |
| `Tools/Projects/**` (`/projects`) | placed in M1; substrate vs `Features/Projects` (**OQ-6b**) | Y |
| `Host/Composition.cs` | `Host/` + each feature's `Module/` (`AddFlows()`, `AddAgents()`) | Y |
| `Web/Api/{HostApiClient,WebJson}.cs` | `Web/` (+ canonical JSON moved to Contracts) | Y |

---

## 3. The two movements

**Movement 1 = organization** (every file in its correct assembly, walls
standing, compiling — dirty edges *allowed*). **Movement 2 = dependencies + new
behaviors** (resolve the forbidden edges, then add SignalR & the new surfaces).
Placement and dependency-shape are decided separately, in that order.

---

### Movement 1 — Organization (assemblies + walls; accept dirty deps)

Goal: the **proposal's structure physically exists** for the built scope and the
solution **compiles**. We add whatever `<ProjectReference>` makes it build,
**including slice→slice edges the graph forbids**. We do not refactor a single
one of those edges here — that is Movement 2's job. Steps end green
(build + behavior tests); the reference-graph test is the deliberate exception.

- **M1.0 — Prep + baseline (V0).** Skeleton (`Features/`, `Domain/`,
  `Infrastructure/` containers); root `Directory.Build.props` (`UseArtifactsOutput`
  → `/artifacts`, nullable, warnings-as-errors, net10.0). One canonical JSON
  options hoisted into the contracts leaf; delete the `WebJson`/`WireJson`
  duplicate pair (mechanical). **Capture the V0 behavioral baseline** (§Validation)
  on today's code *before* moving anything — it's the oracle V1 diffs against.
- **M1.1 — `Infrastructure/` floor.** Relocate the genuinely-generic plumbing
  (`SubprocessSession`, `RunCommand*`, `Shell`, `AnsiHelpers`, `JsonLine`,
  paths). Pure relocation — no new abstractions.
- **M1.2 — `Domain/Agents/` (walled).** Relocate `Actors/Agents/**` (+ Claude/
  Codex) into one assembly. `PtySession` moves here and becomes **`internal`**;
  `SubscriptionGuard` moves here (billing = business rule). Public surface = one
  port (`IAgentRuntime`, consolidating `IAgentFactory`/`IProvider` — **OQ-1**).
  The intercom (`PendingDecisions`, resolvers) rides along as runtime behavior.
  **The spawn wall is closed by the compiler from here on.**
- **M1.3 — `Features/Flows/` (per-use-case assemblies).** Engine + Operation
  framework + concrete `FileFlowHistory` → `Features/Flows/Shared/`. Flow DTOs →
  `Features/Flows/Contracts/` (leaf). Each handler split out of `Endpoints.cs`
  into its **own** assembly: `Start`, `List`, `Get`, `Cancel`, `Watch`. `Module/`
  exposes `AddFlows()`.
- **M1.4 — `Projects` / `Git`.** Stand each up in its correct home (slice or
  substrate) — **OQ-6**. Don't agonize over the edge; if `Start` needs
  `Projects`, just reference it for now (that edge is M2's worklist).
- **M1.5 — `Host` / `Web` compose.** `Host` references the `*.Module`
  assemblies; `Web` references the `*.Contracts` leaves. Add any extra reference
  needed to compile.
- **M1.6 — Stand up the violation detector.** Add `tests/Architecture/` with an
  ArchUnitNET reference-graph test encoding the proposal's DAG. **Run it: it
  fails, and its failure list enumerates every illegal edge we knowingly added.**
  That list **is** the Movement-2 worklist. Quarantine it (skipped/known-fail) so
  CI stays green; it flips to enforced at the end of Movement 2.

**Movement 1 done-when:** structure matches the proposal, `PtySession`
unreachable from outside, build warning-free, behavior tests green, the
architecture test is red-by-design with a concrete edge list, **and the V1 gate
passes** (re-run smoke == V0 baseline — see §Validation).

---

### Movement 2 — Dependencies + new behaviors

Two concerns, both *after* organization. Do **2A before 2B** so the new SignalR
work lands on a clean graph.

**2A — Resolve the forbidden edges** (work the architecture-test list). For each
illegal edge, apply the sharing ladder (cheapest first):
1. **Contract / event** — convert a feature→feature call into a trigger/read of
   the peer's `*.Contracts`, or a reaction to its event. (Expected case:
   `Flows/Start → Projects` becomes a contract read; the Flows↔Agents intercom
   coupling becomes a port/event.)
2. **Downward port** — a substrate call returning a flat result (`IAgentRuntime.
   Run(req) → AgentResult`), the model never leaving its assembly.
3. **Extract to `Domain/<Aggregate>`** — *only* when two consumers must enforce
   the **same invariant** on a type. This is the only sanctioned way new shared
   domain code is born — forced by a real edge, never assumed.
   Flip the architecture test to **enforced (green)** when the list is empty.

**2B — New behaviors / transport (SignalR).**
- Replace the SSE snapshot stream with a SignalR hub; **delete** `Sse.cs` +
  `FlowStreamClient.cs`.
- Add the **bidirectional intercom**: push pending decisions/questions to the
  client and accept answers back — this **fills the missing "answer a decision"
  surface as a `Features/Agents/` use case** (resolution is an agent concern; a
  flow only *requests* a decision — **OQ-5**). The resolver mechanism already
  lives in `Domain/Agents/` from M1.2; 2B adds the use-case surface over the hub.
- Land `design/adr/0002-transport-signalr.md`: SignalR supersedes SSE,
  **reconciling oracle Tier-A A7** (which names `/flows/{id}/events` SSE).
- Web keeps `HostApiClient` for request/response; adds a SignalR **client**
  (transport plumbing, not visual UI) for the live stream + intercom.

**Movement 2 done-when: the V2 gate passes** (§Validation) — new transport
delivers V0's snapshots over the hub *and* answer-a-decision works end-to-end.

---

### Validation gates — run it, don't just compile it

Two backend/integration-level gates prove the spine still *works*, not merely
builds. **No visual UI is touched or required for either** (see guardrail 5).

- **V0 — Baseline (capture now, on today's code, at M1.0).** The oracle for
  "behaves like today": full test-suite output + an end-to-end smoke transcript —
  start a flow, stream its snapshots to completion, cancel one; capture
  `/catalog`, `/projects`, `/flows`, `/flows/{id}` (incl. ETag/304) responses.
- **V1 — Post-organization (end of Movement 1).** Test suite green **and** re-run
  the V0 smoke → **identical observable behavior**. A pure reorg changes nothing a
  caller can see. Gate to enter Movement 2.
- **V2 — Post-SignalR (end of Movement 2).** Test suite green **and** an
  integration smoke over the new transport: snapshots arrive via the hub matching
  V0's stream, **and** the new answer-a-decision path works end-to-end. The smoke
  uses an integration test client, not a Blazor screen.

---

### Movement 3 (LAST-BACKEND) — Opportunistic extraction (evidence-gated)

A short review for recurrence that *no edge forced* but that clearly repeated.
Extract only on real second use:
- A repository abstraction — **only if** a second persistent store appeared
  (`IFlowHistory` → generic `IRepository<T>` lives here, never earlier).
- `Result<T>`, `Kernel` primitives, a shared handler/dispatch base, the `Module`
  pattern.

Output: a one-page findings note + the agreed extractions — evidence, not assumption.

---

### Movement 4 (FINAL) — Fake UI

The **only** phase that touches visual Blazor components. With the backend
reorganized, transport on SignalR, and V2 green, build the fake UI screens as
needed and validate them with `tools/frontend-verify/`
(real-browser render + console/network errors + animation). Nothing before this
phase renders or depends on a screen.

---

## 4. Open questions

**Resolved (this revision):**
- **OQ-3 — Contracts granularity → per-feature leaf**, honoring the proposal.
  `Features/Flows/Contracts/` is a leaf; Web binds to it.
- **OQ-4 — Assemblies vs folders → honor the proposal: one assembly per use
  case, tight.** Resolved by the two-movement split — Movement 1 creates the full
  assembly structure and *accepts* the forbidden edges; Movement 2 resolves them.
  No softening of Q5.
- **OQ-8 — Reference-graph test → promoted into the method.** ArchUnitNET is
  stood up red-by-design at M1.6 as the violation detector / Movement-2 worklist,
  then flipped to enforced at the end of 2A. (Broader Unit/Integration/Acceptance
  reorg stays its own later task.)
- **OQ-9 — Assembly naming → `ABox.` prefix, simple, no `.Features.`
  segment.** `ABox.Flows.Start`, `ABox.Flows.Shared`,
  `ABox.Flows.Contracts`, `ABox.Flows.Module`,
  `ABox.Domain.Agents`, `ABox.Infrastructure`. The folder path
  carries the `Features/` grouping; the assembly name stays short.
- **OQ-10 — The monolithic `ABox` project dissolves.** `ABox` is
  the **repository/concept name, not a csproj** — the one big class library
  fragments into `Domain.Agents` + `Infrastructure` + the Flows assemblies and
  ceases to exist; `ABox` survives only as a namespace prefix. For M1 a
  single `ABox.Tests` is re-pointed at the new assemblies (per-assembly
  test split deferred to OQ-8's later task).
- **OQ-11 — Slice endpoint shape → each use case ships its own `MapXxx`; the
  `Module` is the single composition seam.** Every use-case assembly exposes a
  thin `MapXxx(IEndpointRouteBuilder)` + handler. The feature `Module` aggregates
  them (calls each `MapXxx`, registers services) **and** wires the SignalR hub as
  needed; `Host` only calls the Module's `AddFlows()` / `MapFlows()`. The hub is
  the streaming use case's exposed surface — the one place the "use case =
  Minimal-API handler" rule bends, hosted by the Module.
- **OQ-2 — SignalR fully replaces SSE.** One transport, no coexistence. Both
  hand-rolled SSE files deleted in 2B; ADR 0002 reconciles oracle A7.
- **OQ-5 — Decision resolution is an *Agents* use case, not a Flow's.** A flow
  only **requests** a decision (at most a step that reaches across) — resolving it
  is owned by Agents. So `Features/Agents/` **materializes** with the answer/
  resolve use case (a *new behavior*, added in 2B), sitting beside `Domain/Agents/`
  (the runtime + the pending-decision mechanism, organized in M1.2). This is the
  proposal's two-Agents-homes split, confirmed by a real case. The flow-step →
  Agents-use-case call is an expected M1-dirty / M2A-cleaned edge.
- **OQ-6 (Git) — `Git` is its own independent slice now**, a deliberate candidate
  to demote into `Infrastructure` later once its cross-feature use is real (a flow
  step *and* PR-stacks *and* others). The flow-step → Git-slice call is another
  expected M1-dirty / M2A edge; Git's pull toward Infra *is* that cleanup pressure.

**Still open — resolve in-movement, each with my lean:**
- **OQ-1 — Dispatch shape & runtime port name.** Endpoints call feature handlers
  **directly** (no mediator) this pass? Consolidate `IAgentFactory`/`IProvider`
  into one `IAgentRuntime`?
  *Lean: yes to both. MediatR/Wolverine deferred to a real trigger (durable flows surviving restart).*
- **OQ-6b — `Projects` placement.** `Projects` (`/projects`, `/catalog`):
  supporting substrate or a `Features/Projects` slice?
  *Lean: supporting registry, promoted to a feature only if it grows. M1 just places it; the arch test flags any bad edge.*
- **OQ-7 — Concrete operation placement.** The Operation framework → Flows
  `Shared/`; but the step *bridges* (`DelayOperation` provisional, a `Git` step,
  an `agent-invoke`/`answer` step) sit where, now that Git and the Agents use case
  are their own slices?
  *Lean: the bridges live in Flows `Shared/` and reach **down to `Domain/Agents`** (runtime port, legal) or **across to the Git/Agents slices** (dirty in M1 → contract/event in M2A). Settle during M1.3.*

---

## 5. Done-when

**End of Movement 1 (organization):**
- Spine lives under `Features/Flows/` (per-use-case assemblies), `Domain/Agents/`,
  `Infrastructure/`; no `Engine/`/`Actors/`/`Tools/` technical-layer folders remain.
- `PtySession` is `internal` to `Domain/Agents`; nothing outside can `new` it.
- One JSON options object, one runtime port (`IAgentRuntime`).
- Build warning-free, behavior tests green; the architecture test is **red with a
  concrete edge list** (the Movement-2 worklist).

**End of Movement 2 (dependencies + behaviors):**
- Architecture test **green** — no feature↔feature or use-case↔use-case edges
  remain; every cross-slice need flows through a Contract, event, or port.
- One transport (SignalR); `Sse.cs` + `FlowStreamClient.cs` deleted; ADR 0002
  reconciles oracle A7.
- The "answer a decision" surface exists as a `Features/Agents/` use case.
  **V2 gate green** — behavior identical-or-better than today over the new
  transport (run it): start a flow, watch it stream, **answer a decision**, cancel.
- `FileFlowHistory` still concrete — no speculative repository.

**End of Movement 3:** a one-page findings note listing what (if anything) earned
extraction — with evidence.

**End of Movement 4:** fake UI screens render and pass `frontend-verify` — the
first and only time UI is touched.

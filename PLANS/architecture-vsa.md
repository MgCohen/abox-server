---
type: spec
status: brainstorm
tags: [#architecture, #vsa, #vertical-slice, #contracts, #agent-first]
---

# Architecture — Vertical Slice (agent-first)

> **Purpose.** The backbone for building the product's capabilities (Validation,
> Evaluation, Tasks, Project-Setup, …) on top of the existing engine, derived
> *from the features* rather than imposed on them. This is a structure + rules
> doc: how we organize, what becomes an assembly, what's shared, the dependency
> law, and how the whole thing is made enforceable for agent authors.
>
> **Why it exists.** The engine was organized by *mechanism* (Tools/Actors/Flows)
> — right for the walking skeleton, wrong for adding many independent
> capabilities. Each new capability is a *vertical*, not a mechanism. This doc
> makes the vertical the primary axis.
>
> **Status.** Brainstorm. The shape is settled; three dials are still open (§9).

---

## 0. The backbone

```
        ┌──────────────────────────────────────────────┐
        │  UI ADAPTER  (Web / Mobile / PC)              │  ← decoupled, talks to *.Contracts only
        └──────────────────────────────────────────────┘
        ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐
        │ API  │ │ API  │ │ API  │ │ API  │ │ API  │      ← each slice's public contract
        │ HNDL │ │ HNDL │ │ HNDL │ │ HNDL │ │ HNDL │      ← internal handler
        │ FEAT │ │ FEAT │ │ FEAT │ │ FEAT │ │ FEAT │      ← vertical slice (one feature area)
        └──────┘ └──────┘ └──────┘ └──────┘ └──────┘
        ┌──────────────────────────────────────────────┐
        │  DOMAIN  (earned commons — NOT one big layer) │  ← shared aggregates where ≥2 slices need them
        └──────────────────────────────────────────────┘
        ┌──────────────────────────────────────────────┐
        │  INFRA.AgentRuntime  (forced pipeline, PTY,   │  ← the moat: agentic invariants
        │  sessions, ledger, bus, budgets)              │
        ├──────────────────────────────────────────────┤
        │  INFRA.Platform  (observability, persistence, │  ← generic, replaceable plumbing
        │  net, scheduling runtime)                     │
        └──────────────────────────────────────────────┘
```

Four principles (the ones we locked):

1. **Decoupled UI** reacting to contracts + APIs (UI is across a *network*
   boundary — Tailscale — so contracts are the wire, not an in-process ref).
2. **Vertical slice per feature**, each with its own API + handler.
3. **Shared domain models where it matters** — *earned commons*, not a default
   dumping layer; domain is **not** a single layer.
4. **Shared infra** — split into generic *Platform* and agentic *Runtime*.

---

## 1. How to organize a vertical slice

A slice contains everything one use case needs, co-located: its request,
handler, validation, any slice-local model, its mapping to a DTO, its response.
"Things that change together live together." The folder layout:

```
Feature.Flows/                  (assembly)
  RunFlow/
    RunFlowHandler.cs           command — orchestrates the domain
  GetFlowSnapshot/
    GetFlowSnapshotHandler.cs   query — maps domain → DTO
  ListFlows/
    ListFlowsHandler.cs
    FlowListItem.cs             slice-local model (internal, nobody else sees it)
  FlowsFeature.cs               public AddFlows() — the ONE public type
```

Everything except the registration extension is `internal`. The feature's public
*face* is its Contracts assembly (§6), not its handlers.

---

## 2. Feature vs Use case vs Domain aggregate

Three concepts that get wrongly collapsed into the word "feature":

| Concept | What it is | Lives in | Is it a slice? |
|---|---|---|---|
| **Domain aggregate** | the *noun* with state + invariants (`Flow`, `Agent`, `Session`) | Domain / Runtime | **No** — it's the model a slice orchestrates |
| **Use case (slice)** | one thing a caller does (`RunFlow`, `GetSnapshot`) — request + handler + response | a folder in the feature assembly | **Yes** — the unit of API + the unit the pipeline wraps |
| **Feature area** | cohesive grouping of related use cases (`Flows`) | an assembly | No — it's the *container* |

**The slice is the use case, not the entity.** `RunFlow` is a slice; `Flow` is
never a slice.

### The "is it even a Feature?" test

> **Does an external client (the UI adapter) drive this operation through an API?**
> - **Yes** → it's a use-case slice in a feature area.
> - **No** (only other handlers call it) → it's **Domain/Runtime**, no slice.

Worked through our two examples:

- **Flow** — UI drives Run/Stop/GetSnapshot/CheckStatus → real feature area
  `Feature.Flows` with four slices. The `Flow` *aggregate* + engine sit in
  Domain/Runtime, referenced by the handlers.
- **Agent** — `Agent.Run` *inside a flow* is **not** a slice; it's the Flow
  handler calling `IAgentRuntime`. The only user-facing agent operations (start
  chat, send turn, get logs) become a thin `Feature.Chat`. **A capability used
  only by other features is Runtime, not a Feature.**

### Command vs query weight

Don't make slices uniform. Commands (`Run`, `Stop`) touch domain behavior + the
full pipeline. Queries (`GetSnapshot`, `GetLogs`, `CheckStatus`) are often a
one-liner reading store → DTO. Thin is correct for getters.

---

## 3. What gets an assembly — and what does NOT

Assemblies are a **compile/deploy boundary**, not the slicing mechanism. The rule:

| Gets its own assembly | Stays a folder/namespace |
|---|---|
| A **feature area** (`Feature.Flows`) | A **use case** inside it (`RunFlow/`) |
| A **feature's Contracts** (`Flows.Contracts`) | A **layer within** a feature (API/Handler/model) |
| The **shared bands** (Domain, Infra.Platform, Infra.AgentRuntime) | An individual **handler** or **DTO** |
| **Host** (composition root) and **Web** (UI) | — |

**Never split the layers *within* one capability into assemblies.** That was the
L1 mistake (Agents/Steps/Flows as separate assemblies), collapsed 2026-05-31:
those are tightly-coupled collaborators in one vertical. Splitting independent
*features* is the opposite — and correct.

> **Reconciling the collapse:** the lesson was "don't wall apart things that
> change together." Layers-within-a-capability change together (collapse them);
> independent features change separately (wall them).

A use case is **never** its own assembly. One feature assembly holds many
use-case folders.

---

## 4. What gets shared

Three shared things, each with a strict rule:

### 4.1 Domain — *earned commons*

A concept earns a place in the shared Domain only when **≥2 slices actually need
it** (Project, Agent, Session, Flow, Task, Plan, Verdict, Notification).
Otherwise it stays a slice-local model. Default = the slice owns its logic;
promote on the **second** real use (the CLAUDE.md YAGNI rule). This is what stops
the shared domain from rotting into a god-blob — the failure mode of the
VSA/Clean hybrid we chose.

Three model tiers per slice are normal, not a smell:
- **aggregate** (shared, rich) — `Flow`
- **slice-local model** (internal, computed for one slice) — `FlowListItem`
- **DTO** (flat, wire) — `FlowSnapshotDto`

Most queries use only two. Reach for a local model *only* when the slice needs a
shape neither the aggregate nor the DTO gives you.

### 4.2 Contracts — the leaf (see §6 for the deep dive)

Zero-dependency, UI-facing. The public face of a feature.

### 4.3 Infra — two strata

- **Infra.Platform** — observability, logging, persistence, networking,
  scheduling runtime. Domain-agnostic, replaceable. **No business logic.**
- **Infra.AgentRuntime** — the forced pipeline, sessions, PTY/ConPTY, decision
  ledger, snapshot pipe, event bus, budgets. Not business logic, but *not
  generic either* — **this is the product's moat**, the agentic choreography.

Keep them apart (≥2 namespaces, ideally 2 assemblies) so generic plumbing and
hard-won agentic behavior don't rot into each other (maps to R-ARCH-1
framework ≠ implementation).

---

## 5. Valid dependency order

The project graph **is** the architecture. One law:

> **Depend DOWN, never SIDEWAYS. Slices that must react talk via EVENTS.**

```
Contracts (per feature)   → (nothing)                       LEAF
Domain                    → (nothing)
Infra.Platform            → (nothing domain-specific)
Infra.AgentRuntime        → Domain, Infra.Platform
Feature.X                 → X.Contracts, Domain, Infra.*     (NEVER Feature.Y)
Host                      → every Feature.* + Contracts + Infra   (composition root)
Web (UI)                  → *.Contracts ONLY
```

What the graph enforces automatically:

- **Down-only**: a feature can't name a sibling — it's not in the reference
  graph → won't compile.
- **Sideways = events**: `Feature.Evaluation` publishes `EvalDegraded` to the bus
  (in Runtime/Contracts); `Feature.Notifications` subscribes by referencing
  `Evaluation.Contracts` — the *public face*, never the internals. Neither
  references the other's code.
- **Down resolves most "leans on" edges for free**: e.g. Evaluation→Validation
  is just both using the shared `Verdict` concept in Domain — no slice-to-slice
  call at all.

Each feature spec's **"leans on"** line resolves to exactly one of four
destinations: **shared Domain · generic Infra · agentic Runtime · sideways
event.** That four-way sort *is* the dependency design, derived from specs.

---

## 6. Contracts — placement and implications

**The hard rule: Contracts is a leaf — zero deps on Domain/Infra/Features.**
Because the UI is across a network boundary (WASM/mobile over Tailscale),
referencing a feature assembly to get a DTO would transitively drag the entire
engine (PTY, process code) into the client bundle. Non-negotiable.

**Recommendation: a Contracts assembly *per feature*** (`Flows.Contracts`) — the
module's public API. Gives co-location + UI decoupling + compiler-enforced
encapsulation at once. (Lighter alternative: one shared `Contracts` with
folders-per-feature; simpler but a shared write-surface. See dial in §9.)

Three things people cram into "Contracts" — keep them separate:

1. **API contracts** (request/response/DTO) → the feature's `*.Contracts` leaf.
2. **Event contracts** (`FlowCompleted`, `EvalDegraded`) → the **publishing**
   feature's Contracts (a subscriber takes a *contract* dep on the publisher,
   never a code dep).
3. **Service interfaces** (`IFlowEngine`, `IAgentRuntime`) → **NOT contracts.**
   They live in Runtime/Domain.

**Contracts ≠ Domain models.** The aggregate is internal and rich; the DTO is a
flat *projection*. The handler maps domain → DTO. That mapping **is** the
firewall — it's what lets the inside refactor freely while the wire stays stable.

### Worked example (condensed)

`Flows.Contracts` (leaf):
```csharp
namespace Flows.Contracts;

public sealed record RunFlowRequest(string FlowName, string Project, string Prompt);
public sealed record RunFlowResponse(Guid RunId);

public sealed record GetFlowSnapshotRequest(Guid Id);
public sealed record FlowSnapshotDto(Guid Id, string Flow, string Project,
    FlowPhaseDto Phase, IReadOnlyList<OperationDto> Operations, DateTimeOffset CreatedAt);
public sealed record OperationDto(string Name, string Status, string? Summary);
public enum FlowPhaseDto { Pending, Running, Completed, Failed, Canceled }

public sealed record FlowCompleted(Guid RunId, string Project, FlowPhaseDto FinalPhase);  // event
```

`Domain` (shared aggregate, rich, internal):
```csharp
namespace Domain;

public sealed class Flow
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public string Project { get; }
    public FlowPhase Phase { get; private set; } = FlowPhase.Pending;
    public IReadOnlyList<Operation> Operations { get; }
    public DateTimeOffset CreatedAt { get; }
    public void Start() => Transition(FlowPhase.Running);
    public void Complete() => Transition(FlowPhase.Completed);
    private void Transition(FlowPhase next) { /* invariants */ Phase = next; }
}
public enum FlowPhase { Pending, Running, Completed, Failed, Canceled }   // ≠ FlowPhaseDto
```

`Runtime` (the handler seam + engine interface):
```csharp
namespace Runtime;

public interface IApiHandler<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface IFlowEngine
{
    Task<Domain.Flow> Launch(string flowName, string project, string prompt, CancellationToken ct);
    Domain.Flow? Get(Guid id);
    IReadOnlyList<Domain.Flow> List(string? project);
    Task Stop(Guid id, CancellationToken ct);
}
```

`Feature.Flows` (handlers `internal`; query shows the mapping firewall):
```csharp
namespace Flows;
using Flows.Contracts; using Runtime;

internal sealed class RunFlowHandler(IFlowEngine engine)
    : IApiHandler<RunFlowRequest, RunFlowResponse>
{
    public async Task<RunFlowResponse> Handle(RunFlowRequest r, CancellationToken ct)
        => new RunFlowResponse((await engine.Launch(r.FlowName, r.Project, r.Prompt, ct)).Id);
}

internal sealed class GetFlowSnapshotHandler(IFlowEngine engine)
    : IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?>
{
    public Task<FlowSnapshotDto?> Handle(GetFlowSnapshotRequest r, CancellationToken ct)
    {
        Domain.Flow? f = engine.Get(r.Id);
        return Task.FromResult(f is null ? null : new FlowSnapshotDto(
            f.Id, f.Name, f.Project, MapPhase(f.Phase),
            f.Operations.Select(o => new OperationDto(o.Name, o.Status.ToString(), o.Summary)).ToList(),
            f.CreatedAt));
    }
    private static FlowPhaseDto MapPhase(Domain.FlowPhase p) => p switch { /* explicit */ _ => FlowPhaseDto.Pending };
}
```

`Feature.Flows` registration — the ONE public type, so internal handlers wire
without leaking:
```csharp
namespace Flows;
public static class FlowsFeature
{
    public static IServiceCollection AddFlows(this IServiceCollection s)
    {
        s.AddScoped<IApiHandler<RunFlowRequest, RunFlowResponse>, RunFlowHandler>();
        s.AddScoped<IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?>, GetFlowSnapshotHandler>();
        return s;
    }
}
```

`Host` (the only place that sees both sides — contract in the signature, handler
via DI):
```csharp
builder.Services.AddFlows();

app.MapPost("/flows", (RunFlowRequest req, IApiHandler<RunFlowRequest, RunFlowResponse> h, CancellationToken ct)
    => h.Handle(req, ct));
app.MapGet("/flows/{id:guid}", (Guid id, IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?> h, CancellationToken ct)
    => h.Handle(new GetFlowSnapshotRequest(id), ct));
```

`Web` (references `Flows.Contracts` only — cannot name `Flow` or any handler):
```csharp
using Flows.Contracts;
public sealed class FlowsClient(HttpClient http)
{
    public Task<FlowSnapshotDto?> Get(Guid id) => http.GetFromJsonAsync<FlowSnapshotDto>($"/flows/{id}");
}
```

Implications:
- You write the `Map`. That cost **is** the firewall — refactor the aggregate,
  zero client impact, as long as the DTO is unchanged.
- The UI's reachable surface = exactly the Contracts assemblies.
- `FlowPhase` vs `FlowPhaseDto` duplication is the first friction; map explicitly,
  or put a plain shared-primitive enum in a kernel both reference — decide
  per-type, never reference Domain from Contracts.

---

## 7. How we make this agent-first

The bet: **enforced/deterministic boundaries beat convention**, because agents
(and humans) drift from prose. The compiler is the one gate an agent cannot route
around. Defense in depth, four rings (maps to SDLC §0.2):

1. **Assemblies — compile-time reference walls.** A sibling not referenced
   *does not exist* to the compiler. Inner-loop, unskippable. `internal` only
   encapsulates across an assembly boundary, so per-feature assemblies are what
   make handler encapsulation real.
2. **Forced pipeline — behavioral invariants.** Every `IApiHandler.Handle` is
   wrapped by Runtime middleware: audit-ledger write, snapshot/event emit,
   key-scrub, subscription guard, budget enforcement, resume checkpoint,
   anti-zombie teardown. A feature **physically cannot** emit an unlogged
   decision or an unscrubbed spawn. (This generalizes today's `Flow.Run<T>`
   bookkeeping from flows to every handler.)
3. **ArchTests — fine-grained structural rules** the compiler can't express:
   "no `Feature.X` → `Feature.Y`", "handlers don't touch `System.Net`",
   "Contracts has zero project refs", naming, intra-assembly layering. Work
   without assemblies (reason over namespaces) — assemblies just upgrade the
   violation from a red test to a broken build.
4. **CODEOWNERS / anti-gaming auditor — protect the walls themselves**, so an
   agent can't "fix" a failing ArchTest by weakening it.

**The cost/benefit inverts for an agent repo:** the ceremony cost of many
assemblies is paid by whoever writes boilerplate — agents, who are excellent at
it and scaffold from a template. The benefit (hard walls) lands exactly where
it's most needed: machines authoring the code. So assemblies tilt *toward* worth
it precisely *because* it's agentic — the opposite of the human-repo default.

Assemblies are **necessary, not sufficient**: they wall *references*, not
*behavior*. Rings 2–4 cover the rest.

---

## 8. Templating a feature (the repeatable shape)

A new feature = two nested templates:

- **Feature-area shell** (once per feature): the `Feature.X` assembly, its
  `X.Contracts` leaf assembly, the `AddX()` registration extension, and an
  ArchTest asserting `Feature.X` references no sibling + `X.Contracts` is a leaf.
- **Use-case slice** (once per operation): `Request` + `Response`/`Dto` in
  Contracts, a `Handler : IApiHandler<,>` in the feature (internal), a `Map` if
  it's a query, a DI line in `AddX()`, an endpoint in Host.

---

## 9. Open dials (decisions not yet locked)

- **DIAL-1 — Contracts granularity.** Per-feature Contracts assemblies (rec —
  consistent with the assembly-walls stance) vs one shared `Contracts` with
  folders (lighter start, shared write-surface).
- **DIAL-2 — Infra split.** Two assemblies (Platform / AgentRuntime, rec) vs one
  band with two namespaces.
- **DIAL-3 — Domain richness.** Earned-commons / promote-on-2nd-use (rec) vs a
  rich domain core up front.
- **Enum/primitive duplication.** Per-type call: explicit map (default) vs a
  shared-primitive kernel enum.

---

## 10. Decision log

- **DA1 — Vertical-slice backbone with a shared domain + two-stratum infra.**
  *VSA/Clean hybrid; slices are the primary axis, mechanism is the kernel.*
- **DA2 — Slice = use case, not entity.** Feature area = grouping = assembly;
  domain aggregate = shared noun, not a slice.
- **DA3 — "Is it a feature?" = is it driven by an external client via API.**
  Else it's Domain/Runtime (e.g. `Agent.Run` inside a flow).
- **DA4 — Assembly per feature area + per shared band; never per use-case, never
  per layer-within-a-feature.** Reconciles the L1 collapse.
- **DA5 — Contracts is a zero-dep leaf; the public face of a feature.** Events
  belong to the publisher's contracts; service interfaces are not contracts.
- **DA6 — Dependency law: down-only; sideways via events.** The project graph
  enforces it; ArchTests catch the rest.
- **DA7 — Agent-first = 4-ring defense in depth** (assemblies → forced pipeline →
  ArchTests → CODEOWNERS/auditor). Compiler over convention.

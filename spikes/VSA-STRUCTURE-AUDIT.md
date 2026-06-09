# VSA structure spike — audit & findings

Throwaway spikes to feel out **DIAL-1** (assembly / Contracts granularity) for the
rebuild: how vertical should the slices be, and where do the walls go. Two layouts
built side-by-side with **identical stubbed behavior** so the only variable is
structure. Then audited by sub-agents against two external VSA opinions.

- Date: 2026-06-09
- Spikes: [`flat-vsa/`](flat-vsa/README.md), [`layered-vsa/`](layered-vsa/README.md)
- External references:
  - Oskar Dudycz — *My Thoughts on Vertical Slices & CQRS*
    (architecture-weekly.com/p/my-thoughts-on-vertical-slices-cqrs)
  - Milan Jovanović — *Vertical Slice Architecture: Where Does the Shared Logic Live?*
    (milanjovanovic.tech/blog/vertical-slice-architecture-where-does-the-shared-logic-live)

> Status: **spike, not a decision.** This file records what we saw; DIAL-1 is not
> locked yet. Nothing here touches `/src` or `/tests`.

---

## The two spikes

Both model the same tiny behavior: **Flows** (`RunFlow`, `GetFlowSnapshot`) +
**Notifications** (`ListNotifications`), wired so `RunFlow` → publishes
`FlowCompleted` → a Notifications subscriber reacts → `ListNotifications` reads it
back. Same forced pipeline (`Dispatcher` wraps `IApiHandler` with
`IPipelineBehavior`/`AuditBehavior`) and in-process `EventBus` in both.

### `flat-vsa` — one project, folders are the verticality
- **1 csproj, ~21 files.** A use case is a single file (`RunFlow.cs` = request +
  response + handler together). Bands are folders: `Domain/`, `Runtime/`,
  `Features/Flows/`, `Features/Notifications/`.
- **Buys:** fewest moving parts, max co-location, trivial refactor (move a file, no
  project edits).
- **Gives up:** zero compiler walls. `FlowCompletedSubscriber` does
  `using App.Features.Flows;` and can touch anything in Flows. Nothing is `internal`
  across features. Boundaries are convention + ArchTests only — defeatable in the
  same PR that breaks them.

### `layered-vsa` — two assemblies per vertical, the graph is the wall
- **9 csproj, ~34 files.** Each vertical = `*.Contracts` (leaf, zero deps) +
  `*.Feature` (internal handlers, one public `AddX()`), on shared `Domain` +
  `Infra.Platform`/`Infra.AgentRuntime`, plus `Host` + `ArchCheck`.
- **Buys:** sideways `Feature→Feature` reach is a **CS0246 compile error**. Handlers
  and `NotificationStore` are genuinely `internal`. A UI references `*.Contracts`
  only; the subscriber reacts via `Flows.Contracts`, never `Flows.Feature`.
- **Costs:** project sprawl (9 projects for 2 features), a slice spans two
  projects/folders, csproj wiring per new reference.

---

## What the two blogs actually say

### Oskar Dudycz — slices, not ceremony
- **"Maximize coupling *in* a slice, minimize coupling *between* slices."** This is
  the ruler everything else is measured by.
- **"VSA is an architectural pattern, not a library choice."** Mediators/MediatR are
  optional, incidental — not architecture.
- Rejects **"absolutist interpretations"** and **"semantic diffusion"** — invented
  constraints ("each slice needs its own table / its own assemblies") that were never
  part of VSA.
- CQRS = just "commands mutate state, query handlers read." A conceptual split, not
  machinery.
- Cross-slice reaction via **events**, async, is the right tool.

### Milan Jovanović — graded sharing, shared domain is clean
A **three-tier sharing model** (the load-bearing idea):
1. **Tier 1 — Infrastructure, shared by default.** *"Database contexts, HTTP clients,
   logging. These are technical concerns."*
2. **Tier 2 — Domain concepts shared via entities/value objects.** *"different
   vertical slices can share the same domain model"*; *"Push business logic into the
   domain. Entities and value objects are the best place to share business rules."*
3. **Tier 3 — feature-family local `Shared` folder** (e.g. `Features/Flows/Shared`)
   for logic shared by sibling slices only.
- Two hard rules: **"Features own their request/response models. No exceptions."**
  and the **Rule of Three** — *"Don't abstract until you hit three… Duplication is
  cheaper than the wrong abstraction."* Explicit warning against junk-drawer `Common`.
- Cross-slice via events or a feature's **public-API facade**; discourages direct
  service coupling between unrelated features.

**The tension that matters for us:** Dudycz treats a shared `Domain/` as a smell
("slice owns its state"); Milan treats it as **correct** (Tier 2). Our verticals
(Flows, Notifications, Validation, Evaluation, Tasks) revolve around the *same*
Flow/Phase/Agent aggregates — so Milan's shared-domain model fits our reality better
than Dudycz's "slices are independent" assumption.

---

## Audit findings

### `flat-vsa` — **qualified yes**
Genuine VSA at the slice file (request+response+handler co-located, per-feature
`AddX()` registration). Leaks into accidental layering at the edges.

**Strengths**
- True per-use-case co-location; adding a feature is additive, not a central-registry edit.
- Cross-slice reaction is an event (`FlowCompleted`), not a direct call — cleanest
  possible decoupling for a flat layout.
- README is honest: states plainly that boundaries are convention-only and ArchTests
  are defeatable in the PR that breaks them.

**Smells**
- `Domain/Flow.cs`, `Runtime/FlowEngine.cs` — Flows' own aggregate + state store live
  *outside* the Flows slice. Per Dudycz a smell; per Milan acceptable **only** if
  truly shared (Flow is; the engine is Flows-specific and should move into the slice).
- `Runtime/Dispatcher.cs` + `IPipelineBehavior` — speculative mediator. One audit
  behavior (a `Console.WriteLine`) doesn't pay for runtime handler resolution.
  Violates our own YAGNI rule and Milan's Rule of Three.
- Single `IApiHandler<TRequest,TResponse>` for both commands and queries erases the
  C/Q distinction Dudycz says is the whole point.
- `FlowCompletedSubscriber` can reach `RunFlowHandler`/`StubFlowEngine`, not just the
  event — nothing isolates the published contract.

### `layered-vsa` — **qualified no**
Defensible assembly-per-vertical: the compiler walls and `internal` handlers are
real. But it reintroduced a layered architecture around two trivial features and
**breaks in-slice cohesion**.

**Strengths**
- Real encapsulation, not convention: handlers/`NotificationStore`/subscriber are
  `internal`; only `AddX()` is public. Other assemblies physically can't bind to them.
- Sideways reach is CS0246 — an inner-loop gate an agent literally cannot skip.
- Contracts-as-leaf has one legit payoff: a Blazor/WASM client can reference
  `Flows.Contracts` (zero deps) without dragging the engine into the bundle.

**Smells**
- **Cohesion break (core flaw):** changing `RunFlowRequest` edits `Flows.Contracts`;
  changing behavior edits `Flows.Feature` — two assemblies, two folders, for one use
  case. Splits "things that change together," and **violates Milan's "features own
  their request/response models, no exceptions."**
- `FlowCompleted` lives in the **producer's** `Flows.Contracts`, so
  `Notifications.Feature → Flows.Contracts` is a compile-time dependency on Flows. At
  N consumers this is a fan-in hub; integration events want a neutral leaf.
- **`ArchCheck` is dead weight** — it asserts no `Feature→Feature` reference, but that
  is *already* a compile error since the csprojs don't reference each other. It can
  only fail after someone adds the forbidden reference, which the build already reports.
- `IFlowEngine` sits in `Infra.AgentRuntime` ("the moat") but it's Flows domain logic,
  not generic plumbing — the shared infra is a god-layer every feature depends on.
- `Notification` (1 consumer) lives in the shared `Domain` assembly — coupling with no payoff.

---

## Scorecard

| Criterion | flat-vsa | layered-vsa |
|---|---|---|
| Reviewer verdict | Qualified **yes** | Qualified **no** |
| In-slice cohesion (Dudycz / Milan) | Strong | **Broken** by Contracts/Feature split |
| Enforcement of boundaries | Convention only (defeatable) | **Compiler wall** (CS0246) + `internal` |
| Milan: "features own request/response, no exceptions" | ✅ | ❌ |
| Milan: shared `Domain` (Tier 2) | ✅ present | ✅ present (but over-populated) |
| Rule of Three / no premature abstraction | ⚠️ Dispatcher premature | ❌ Contracts + ArchCheck + Dispatcher premature |
| Cross-slice via events | ✅ | ✅ (but event in producer's leaf) |
| Project count @ ~25 features | ~1 | ~30–55 csproj |
| Refactor cost | Trivial (move a file) | csproj edit per reference |

---

## Insight & recommendation

Three sources triangulate on nearly the same place:

- **flat-vsa** already *is* roughly Milan's recommended structure — minus the premature
  Dispatcher, and with the shared `Domain/` blessed (Tier 2) rather than apologized
  for. Its only real gaps vs. Milan: **enforcement** is convention-only, and it has no
  Tier-3 `Shared` folders yet (hasn't needed them).
- **layered-vsa** buys enforcement but pays by breaking *both* blogs' central tenet:
  Milan's "no exceptions" rule and Dudycz's "maximize cohesion in a slice." Two
  assemblies per vertical for two features is the big-design-up-front both warn against.

**Cleanest target (the un-built third option):**
> **Flat-VSA folder layout + a deliberately-blessed shared `Domain` (Tier 2) +
> Tier-3 `Shared` folders when siblings actually share + drop the speculative
> Dispatcher.** Compiler walls become a *later, per-feature extraction* (Rule of Three
> applied to assemblies), only if/when agent-drift is a demonstrated problem.

**The one thing neither blog solves for us:** *multiple agents editing concurrently
drift past conventions.* That — not packaging or deployment — is the real motive
behind the layered spike's walls. If that pain is real, the answer is **not** 9
projects: it's flat + one ArchUnitNET test, escalating to an assembly wall on a
specific feature only where drift actually bites.

### Concrete next steps (not started — awaiting go)
1. Build a third **"Milan-shaped"** spike: flat layout, blessed shared `Domain`,
   a Tier-3 `Shared` folder, **no** Dispatcher — to see the cleanest version concretely.
2. Decide the enforcement question explicitly: convention + ArchUnitNET vs. selective
   assembly walls, driven by *real* agent-drift evidence, not speculation.
3. Lock DIAL-1 with that evidence, then revisit DIAL-2 (infra split) and DIAL-3
   (domain richness).

### Open question for the owner
Is concurrent-agent drift a real, observed problem worth paying enforcement cost for —
or is convention + one arch test enough until it bites?

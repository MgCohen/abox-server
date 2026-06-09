---
type: research
status: reference
tags: [#architecture, #vsa, #modular-monolith, #contracts, #sharing, #agent-runtime]
source: https://github.com/ardalis/RiverBooks/tree/main/src
related: [[architecture-vsa]]
---

# RiverBooks — how domain models cross (or don't cross) module boundaries

> **Why this exists.** We studied ardalis/RiverBooks (a reference modular
> monolith) to answer one question — *are rich domain models ever shared across
> modules?* — and then to pressure-test our own plan, where `Agent`/`Flow` is a
> capability many modules want to **invoke**, not just read. The headline: the
> RiverBooks contract-firewall is the right rule for the **wrong relationship**
> when applied to Agent. See §3.

---

## 1. RiverBooks findings (the evidence)

**Domain models are never shared across modules.** Not via the SharedKernel, not
via Contracts, not directly. Each module owns its rich domain types privately;
the only things that cross a boundary are flat DTOs + MediatR messages in the
`*.Contracts` projects.

### 1.1 SharedKernel holds only infra — zero entities

`DomainEventBase`, `IntegrationEventBase`, `IDomainEventDispatcher`,
`IHaveDomainEvents`, MediatR `LoggingBehavior` / `FluentValidationBehavior`,
`MediatRDomainEventDispatcher`. No `Book`, no `Order`, no `User`.

### 1.2 The proof is the project-reference graph

No module references another module's **implementation** project — only
`*.Contracts` (plus SharedKernel). Only `Web` (composition root) wires the real
modules together.

| Project | References |
|---|---|
| `Books` | `Books.Contracts` |
| `Users` | `Books.Contracts`, `EmailSending.Contracts`, `OrderProcessing.Contracts`, `Users.Contracts` |
| `OrderProcessing` | `EmailSending.Contracts`, `OrderProcessing.Contracts`, `Users.Contracts` |
| `Reporting` | `Books.Contracts`, `OrderProcessing.Contracts` |
| `Web` (composition root) | every module + SharedKernel |

Nobody references `RiverBooks.Books`, and `Book` is declared
**`internal class Book`** — it is *physically invisible* across the assembly
boundary.

### 1.3 How book data actually crosses

A consumer never gets a `Book`. It sends `BookDetailsQuery(Guid BookId)` over
MediatR and receives a flat `BookDetailsResponse(BookId, Title, Author, Price)`.
The handler inside Books unwraps the real entity and never lets it escape:

```csharp
// RiverBooks.Books/Integrations/BookDetailsQueryHandler.cs
var book = await _bookService.GetBookByIdAsync(request.BookId);   // internal Book
return new BookDetailsResponse(book.Id, book.Title, book.Author, book.Price);  // flat DTO leaves
```

Same everywhere: `UserDetailsByEmailQuery → UserDetailsResponse`,
`CreateOrderCommand`, `OrderCreatedIntegrationEvent`, `UserAddressDetails`.

### 1.4 Strongest signal it's deliberate: `Address` is duplicated, not shared

Two independent `Address` records — `RiverBooks.Users.Domain.Address` and
`RiverBooks.OrderProcessing.Domain.Address` — each defined in its own module. A
naive design hoists one into SharedKernel; this repo refuses. Each bounded
context models its own Address; it travels between them as the contract DTO
`UserAddressDetails`, and OrderProcessing rebuilds *its own* `Address` from those
primitives. Classic DDD: no shared mutable domain type between contexts.

### 1.5 The one apparent leak that isn't

Three OrderProcessing files carry `using RiverBooks.Users.UseCases;` /
`.CartEndpoints` (the half-built `ListOrdersForUser` feature). **Dead leftover
imports** — the `OrderSummary` they use is defined *twice inside OrderProcessing
itself*. OrderProcessing doesn't even reference the Users assembly. Cruft in a
teaching repo, not real sharing.

### 1.6 Enforcement

- **Compiler (the real guarantee):** `internal` visibility + the reference graph.
  You cannot depend on another module's internals because the assembly isn't
  referenced.
- **Tests:** `OrderProcessingTests/Arch/InfrastructureDependencyTests.cs` uses
  ArchUnitNET — but only for *intra*-module layering (Domain ↛ Infrastructure).
  The author notes NsDepCop is his preferred tool for cross-module rules.

### 1.7 The mental model RiverBooks demonstrates

- Rich domain objects stay inside their module (`internal` where possible).
- SharedKernel = cross-cutting **infra only**, never entities.
- Module-to-module = flat anemic DTOs + messages in a `*.Contracts` assembly.
- Duplicate a concept (`Address`) per context rather than share one rich type.

---

## 2. Two axes of sharing (the distinction that resolves everything)

RiverBooks' Book→Contracts dance exists **only** for the *sideways* case. There
is a second axis it also uses — and uses directly, with no firewall.

| | **Sideways** (peer → peer) | **Downward** (feature → substrate) |
|---|---|---|
| RiverBooks example | `Book` ← Orders/Reporting | `SharedKernel`, MediatR behaviors, `DbContext` |
| Our example | `Validation` ↔ `Evaluation` | `Validation` → **`Agent`/Runtime** |
| What the consumer needs | a **projection** (id + a few fields) | **rich invocation + structured result** |
| Mechanism | Contracts DTO + MediatR message/event — **the firewall** | direct interface reference, plain method call — **a platform API** |
| Why | the other side's *behavior is irrelevant*; only data crosses | the other side's *behavior is the entire point* |

In RiverBooks **nobody** goes through contracts to use the SharedKernel — every
module references it directly and uses its types richly, in-process. Because a
down-dependency is direct. The DTO firewall is reserved for peers.

---

## 3. Applying it to our plan: Agent is NOT a peer module

**The category error to avoid:** reasoning "Book lives in Books and is reached
via Contracts → therefore Agent lives in Agents and Validation must dispatch an
event through `Agents.Contracts` with no reference to Agents."

That conclusion is wrong, and the tell is exactly the thing we already noticed —
*we want to run the agent with proper input and output.* In RiverBooks terms,
**Agent is not `Book`. Agent is the SharedKernel + pipeline + EF** — the
substrate every feature sits on top of and calls directly.

This is already locked in [[architecture-vsa]] (**DA3** + the "is it even a
Feature?" test, §2):

> **Agent** — `Agent.Run` inside a flow is **not** a slice; it's the Flow handler
> calling `IAgentRuntime`. A capability used only by other features is **Runtime,
> not a Feature.**

### 3.1 Concrete shape

- **Agent is not `Feature.Agents`.** It splits into two **down-band** homes:
  - the **`Agent`/`Session` aggregate** (noun + state + invariants) → shared
    **Domain** (already on the earned-commons list, §4.1).
  - the **`IAgentRuntime` service** (the verb — run it, with the forced
    pipeline) → **Infra.AgentRuntime**, the moat.
- **Validation references both directly** (`Feature.X → Domain, Infra.*`, §5 law)
  and calls:
  ```csharp
  AgentResult result = await _agentRuntime.Run(new AgentRequest(...), ct);
  ```
  No event. No `Agents.Contracts`. No dispatch. Validation depends *down* onto
  Runtime exactly like Orders depends down onto SharedKernel.
- **Evaluation does the identical thing.** Two features sharing a substrate is
  *not* coupling between them — they never reference each other; they both
  reference down. (Same shape as "Evaluation→Validation is just both using shared
  `Verdict` in Domain — no slice-to-slice call at all," §5.)

### 3.2 Where the firewall *does* still belong (don't over-correct)

- **Validation ↔ Evaluation** (genuine peers): sideways → shared `Verdict` in
  Domain if it's just data, or an event via `*.Contracts` if one must *react* to
  the other.
- **Reactions to an agent**, not invocations: "agent emitted decision-needed",
  "flow completed" → `Feature.Notifications` subscribes via the publisher's
  Contracts.

**Discriminator:** *"I need a result back to continue my logic"* → downward
service call. *"Someone might want to react later"* → sideways event. Running an
agent for its output is always the first. Never model a value you depend on as a
fire-and-forget event.

### 3.3 The new question Agent raises that Book never did

The agent call carries rich typed I/O, so `AgentRequest`/`AgentResult` need a
home. **In Runtime/Domain, not in `*.Contracts`.** Contract assemblies are
UI-wire DTOs across the Tailscale boundary; agent I/O are internal platform types
shared *down*, riding the down-reference for free — no leaf constraint, no
mapping firewall. The map-to-DTO firewall reappears only where the UI drives an
agent directly: the thin `Feature.Chat` with its own `Chat.Contracts`.

### 3.4 The risk to watch

Making Agent a down-substrate is correct, but it concentrates power in
`IAgentRuntime`; the failure mode is that interface bloating into a god-service
and Runtime rotting into Platform. Guardrails already in the plan: keep
`Infra.AgentRuntime` strictly agentic choreography (PTY, sessions, ledger,
budgets, forced pipeline) and out of `Infra.Platform` (R-ARCH-1); let **Ring 2
(the forced pipeline)** make calling it safe — every handler invocation wrapped
with audit/scrub/budget so a feature *physically cannot* run an unlogged agent.

---

## 4. Bottom line

The instinct "Validation can't reference Agents, it must go through contracts" is
the right rule for the **wrong relationship**. `Validation → Agent` isn't
`Orders → Book`; it's `Orders → SharedKernel`. Depend **down**, call it directly,
pass rich input, get rich output. The Contracts firewall is reserved for **peer
features that only trade projections** — which Agent, by definition, is not.

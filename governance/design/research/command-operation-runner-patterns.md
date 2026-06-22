# Command / Operation + Runner — pattern research

Research behind the question: *Git began as a flow `Operation`; now it must also run
standalone (PR API, Tasks) — how do we keep "operations run inside the flow gate" while
letting the same work run independently?* Two cases were researched: **(1)** commands with an
enforced execution seam, and **(2)** chaining a boundary command pipeline into an internal
(flow/git) command pipeline.

> **Part 1 is findings only — no proposal.** The proposal is isolated in Part 2.

---

## Part 1 — Findings

### 0. Naming: this is not Unit of Work

"Unit of Work" is a specific Fowler/PoEAA pattern: *"maintains a list of objects affected by a
business transaction and coordinates the writing out of changes and the resolution of concurrency
problems."* It is about batching mutations into one atomic DB commit — change tracking and
transaction coordination. It is **not** the "reify work, run it through a policy-applying
executor" idea, which the name only superficially resembles.

The accurate lineage:

- The reified work object = **Command** (GoF behavioral pattern). Roles: Command / Invoker /
  Receiver / Client. The Receiver (GoF) / **Supplier** (POSA) is the capability that does the real
  work.
- The executor that runs commands and wraps them in orthogonal services (logging, scheduling,
  recording) = **Command Processor** (POSA — Buschmann et al.). Its defining motivation, verbatim:
  *"separates the request for a service from its execution… implement orthogonal operations such as
  logging or scheduling without forcing the sender or receiver to be aware of them."*
- Modern incarnation = **Command Bus** with a **middleware / interceptor pipeline**; a
  **Command Dispatcher** is the sibling whose motivation is decoupling caller from invoker rather
  than adding services.

Sources: [Unit of Work — Fowler](https://martinfowler.com/eaaCatalog/unitOfWork.html) ·
[Command Processor — software-pattern.org](http://software-pattern.org/Command-Processor) ·
[POSA Command Processor (Vanderbilt PDF)](https://www.dre.vanderbilt.edu/~schmidt/cs282/PDFs/CommandProcessor.pdf) ·
[Command pattern — refactoring.guru](https://refactoring.guru/design-patterns/command) ·
[Commands / Dispatcher / Processor — Brighter docs](https://brightercommand.gitbook.io/paramore-brighter-documentation/command-processors-and-dispatchers-1/commandscommanddispatcherandprocessor)

### Current state of our code (for reference)

- `Flow.Operation<TArgs,TResult>` is nested in `Flow`, implements a **`private` nested
  `IGate<,>`**; `Invoke` is `protected abstract`; only `Flow.Run` can cast to `IGate` and call
  `Execute`. This is the single-path-of-success gate.
- `Operations.Operation<,>` is a public re-export so capabilities can subclass it.
- `Agent` is the **only** `IDecisionSource`; the gate drains its decisions after `Invoke`.
- `OperationArgs(string Name)` carries the step label; Git's `CommitArgs` etc. inherit it.
- Git's commit/push/pull are `Operation` subclasses, so `Domain/Git` depends on `Domain/Flow`.

---

### Case 1 — Commands with an enforced execution seam

**What "enforced seam" means here:** it must be structurally impossible (compiler-enforced, not
convention) to execute a command except through a sanctioned runner.

**GoF Command does not formalize this.** The literature treats "commands run through the invoker"
as a *collaboration/convention* — `execute()` is conventionally public and any caller may invoke
it. The enforced invoker-only path is therefore a *strengthening* of Command, not part of it.
([Command pattern — Wikipedia](https://en.wikipedia.org/wiki/Command_pattern))

**The enforcement mechanism has names across languages:**

| Name | What it is | Enforcement | Context |
|---|---|---|---|
| **Object-capability model** | The only way to act on an object is to hold an unforgeable reference; possession *is* authority | No ambient authority; non-holders cannot name the operation | Security theory; E, Pony, WASM component model; the general framing |
| **Passkey idiom** | A public method takes an empty *badge* token whose constructor only sanctioned callers can mint | Compiler-enforced, zero runtime cost | C++ (C++20 fixed an aggregate-init bypass) |
| **Attorney-Client idiom** | All access to internals funnels through one sanctioned proxy class | A single structural bridge to private members | C++ |
| **Sealed trait (against calling)** | A method takes a token from a private module; downstream can't construct it | Visibility of the token | Rust ("sealed against use") |
| **`sealed` / package-private gate** | Seals who can implement/extend, or who can call (same package/module) | Subtype/visibility restriction | Scala, Kotlin, Java 17+ |
| **Policy Enforcement Point / chokepoint** | All traffic routed through one mandatory node that applies policy | Single bypass-proof seam | Security architecture / microservices |

Our current C# trick (`protected Invoke` + `private` nested gate interface) is a **hybrid of
Passkey (unforgeable access) and Attorney (single bridge)**. The processor's role — the single
mandatory point where cross-cutting policy is applied — is the **Policy Enforcement Point /
chokepoint**.

**Concrete .NET mechanisms to enforce "only a sanctioned runner executes, across assemblies"**
(the relevant constraint once the kernel moves to Infrastructure and runners live elsewhere):

1. **Passkey badge minted by a `protected` runner base (no `InternalsVisibleTo`).** Kernel defines
   `RunnerBadge` (private ctor) + `abstract RunnerBase { protected RunnerBadge Mint(); }`. The
   command's execute method is public but takes a `RunnerBadge`; any runner in any assembly
   subclasses `RunnerBase` to mint one, nobody else can. Works across assemblies, open runner set,
   zero kernel edits, no strong-name brittleness.
2. **`internal` execute + `[InternalsVisibleTo]` per runner assembly.** Simplest, genuinely
   compiler-enforced, but coarse (exposes *all* kernel internals to each friend), strong-name
   brittle, and the kernel must *name its consumers* (inverted dependency direction).
3. **Roslyn analyzer / `BannedApiAnalyzers`** banning `.Invoke()` outside runner assemblies. The
   only option that yields a real compile *error with an actionable message*, but it is lint
   (suppressable via `#pragma`/`SuppressMessage`), not type-system law.

**Two traps found:**

- **`protected` alone does not work.** C#'s **CS1540** forbids accessing a `protected` member
  through a base-typed reference or a sibling instance — so a `FlowRunner` holding a `Command cmd`
  cannot call `cmd.Invoke(...)`. The natural "runner executes the command handed to it" shape does
  not compile unless the bridge is **inverted** (the command calls back into the runner / takes a
  badge the runner minted).
- **Our current `private` nested-interface gate cannot span assemblies.** A private nested
  interface is visible to exactly one class, so a runner in another assembly cannot name it. This
  is the wall hit by moving the kernel out of `Flow`.

**No mainstream library enforces this.** MediatR, Wolverine, and Brighter all rely on
**convention + DI discipline** — nothing stops you resolving a handler and calling it directly,
you just bypass the pipeline's value. Enforcing a *structural* single execution seam is past the
state of the art for these libraries.

**The type-level vs convention tradeoff (as reported):**

- For type-level ("make illegal states unrepresentable"): the invariant holds globally and by
  construction; fewer tests needed; private constructors + sealed types are the canonical family.
- Cautions: *"names are not type safety"* — wrapping tricks can give an *illusion* of safety; the
  seam must genuinely make the bad call inexpressible (and all of these are compile-time only —
  reflection bypasses them, so it is a guard-rail, not a runtime trust boundary). Type-level seams
  also add boilerplate and obscure signatures (token params, proxy classes).
- Architecture tests (ArchUnit/NetArchTest, our `tests/ArchTests`) are the *complementary*
  detective control: cheaper, refactor-friendly, but fail only after a violation is written and
  only where the test's scope reaches. The literature's center of gravity: spend the type system
  on the one invariant that must never break; use arch-tests for the looser conventions.

Sources: [object-capability model](https://en.wikipedia.org/wiki/Object-capability_model) ·
[Passkey idiom (ACCU)](https://accu.org/journals/overload/31/176/mertz/) ·
[Attorney-Client idiom](https://en.wikibooks.org/wiki/More_C%2B%2B_Idioms/Friendship_and_the_Attorney-Client) ·
[Definitive guide to sealed traits in Rust](https://predr.ag/blog/definitive-guide-to-sealed-traits-in-rust/) ·
[Kotlin sealed classes](https://kotlinlang.org/docs/sealed-classes.html) ·
[chokepoint](https://www.threatngsecurity.com/glossary/choke-point) ·
[InternalsVisibleTo / friend assemblies](https://learn.microsoft.com/en-us/dotnet/standard/assembly/friend) ·
[`protected` + CS1540](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected) ·
[BannedApiAnalyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md) ·
[MediatR is convention, not enforcement](https://www.jimmybogard.com/you-probably-dont-need-to-worry-about-mediatr/) ·
[Wolverine POCO handlers](https://wolverinefx.net/tutorials/mediator) ·
[make illegal states unrepresentable](https://deviq.com/principles/make-illegal-states-unrepresentable/) ·
[names are not type safety](https://lexi-lambda.github.io/blog/2020/11/01/names-are-not-type-safety/)

---

### Case 2 — Chaining a boundary command pipeline into an internal one

**The shape is a vertical composition of two command processors**, distinct and one-directional
(outer → inner). It has clean canonical names and is explicitly sanctioned in the literature.

- **Outer = a driving/primary (inbound) adapter + boundary middleware** (Cockburn, Hexagonal /
  Ports & Adapters). The HTTP request becomes a *boundary command*; transport concerns (auth,
  shape-validation, request-audit) run here. The inbound adapter does not call business logic
  directly — it sends a command through the port.
- **The boundary handler is a Translator across an Anti-Corruption seam** (Evans' ACL; Facade +
  Adapter + Translator) / a **thin Application Service** (Vernon — coordination, no business
  logic). Its job is to map the boundary command vocabulary into the inner command vocabulary. This
  is the named answer to "two command vocabularies meeting at a layer boundary."
- **Inner = its own command processor with its own pipeline behaviors** (recording, snapshot,
  gating) — these live here because only this layer has the flow/git command semantics.
- **Nesting is formally legal.** Pipes & Filters: *because every filter shares the same interface,
  a filter can itself be an entire pipeline* — the composite-filter is the canonical
  "pipeline-of-pipelines." Rust's **Tower** proves it with types: a built stack *is itself a
  `Service`*, so a stack composes as the inner of another stack; nesting needs no special machinery,
  only a uniform interface.

**The anti-pattern and the safe form:**

- **Anti-pattern:** command-calls-command on the *same* bus → re-entrance, infinite dispatch
  loops, and a lost call graph (the mediator hides who calls whom).
- **Safe form:** *downward delegation to a distinct lower-level processor*, with a strictly
  **one-directional reference graph (outer → inner, never reverse)** — which structurally forbids
  dispatch loops. (This is the same property as a depend-down DAG.)

**Concern ownership — how the frameworks avoid double-applying a concern:**

- **Own-by-information:** a concern lives in the layer that has the data to decide it. Auth needs
  HTTP identity → boundary. Recording/snapshot/gating needs flow/git command semantics → inner.
  Observability is a *request* span at the boundary that *wraps* operation spans inside
  (parent/child), not two copies.
- **Distinct context/command types per pipeline.** The rigorous frameworks (NServiceBus's stages
  with `IInvokeHandlerContext` etc.; MassTransit's `ReceiveContext` vs `ConsumeContext` vs
  `SendContext`/`PublishContext`) type each pipeline's context so a behavior physically cannot
  register in both. Retry/recoverability is kept in its own scope so it never double-fires.
- **Carry, don't re-derive.** Correlation/audit data is propagated across the connector between
  pipelines rather than recomputed in the inner pipeline.

**Handoff mechanics (near-universal across the frameworks surveyed):** the API command is first
**translated** into a domain command, then handed off by one of two explicit moves:

| Mechanic | When favored | Examples |
|---|---|---|
| **(a) Direct call** into the inner processor | synchronous, in-process; want a visible call site | MediatR `Send`, Tower `inner.call`, Wolverine direct `IMessageBus` |
| **(b) Return a derived / "cascading" command** the infrastructure dispatches | deferred/durable/out-of-process; fire only on success; independent retry loop | Wolverine cascading, Brighter `Post` → Outbox → Dispatcher |

In all cases the inner pipeline is re-entered identically regardless of which path delivered the
command; translation (c) is the universal precondition to both.

Sources: [Ports & Adapters — Cockburn (jmgarridopaz)](https://jmgarridopaz.github.io/content/hexagonalarchitecture.html) ·
[Anti-Corruption Layer — Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/anti-corruption-layer) ·
[ACL for mapping between boundaries — CodeOpinion](https://codeopinion.com/anti-corruption-layer-for-mapping-between-boundaries/) ·
[Application services stay thin (Vernon)](http://gorodinski.com/blog/2012/04/14/services-in-domain-driven-design-ddd/) ·
[Pipes and Filters — EIP](https://www.enterpriseintegrationpatterns.com/patterns/messaging/PipesAndFilters.html) ·
[Mediator pipeline behaviors — Bogard](https://lostechies.com/jimmybogard/2014/09/09/tackling-cross-cutting-concerns-with-a-mediator-pipeline/) ·
[Why command should not call command — Workleap](https://medium.com/workleap/why-command-should-not-call-command-in-cqrs-5da046a9fed1) ·
[Balancing cross-cutting concerns in Clean Architecture — Jovanović](https://www.milanjovanovic.tech/blog/balancing-cross-cutting-concerns-in-clean-architecture) ·
[Tower `ServiceBuilder`](https://docs.rs/tower/latest/tower/struct.ServiceBuilder.html) ·
[MediatR behaviors wiki](https://github.com/jbogard/MediatR/wiki/Behaviors) ·
[Brighter — building a pipeline](https://brightercommand.gitbook.io/paramore-brighter-documentation/brighter-request-handlers-and-middleware-pipelines/buildingapipeline) ·
[Wolverine cascading messages](https://wolverinefx.net/guide/handlers/cascading.html) ·
[NServiceBus steps/stages/connectors](https://docs.particular.net/nservicebus/pipeline/steps-stages-connectors) ·
[MassTransit middleware](https://masstransit.io/documentation/configuration/middleware)

---
---

## Part 2 — Proposal (converged after review)

> Working names; this is the source for the ADR. The review trimmed the speculative
> machinery (Passkey badge, boundary pipeline, kernel assembly) and kept the seam that
> already works in `Flow`, hoisted so it spans assemblies through inheritance.

### The shape

1. **`Operation<TArgs,TResult>` keeps its name** (not "Command") and **moves to Infrastructure**,
   beside the other floor plumbing (`IProjectRegistry`, `RunCommand`). It is a generic,
   business-free unit of work: `protected abstract Invoke`. It **keeps `OperationArgs(Name)`** as
   its typed, closed args envelope — the floor contract is *"an operation is a **named** unit of
   work that only a runner can execute."* The `Name` is operation identity (set once by the
   capability) and lets the flow record distinguish repeated uses of the same operation type.
2. **`RunnerBase` is an abstract class (not an interface)** in Infrastructure, and it is the only
   type that can execute an operation. It owns the seam: `protected Execute<TArgs,TResult>(op, args,
   ct)` casts the operation to the **`internal` `IGate<,>`** and runs it. Becoming a sanctioned
   runner = subclassing `RunnerBase`. A runner is a **policy bundle**; `Flow` is the richest one.
   *Class, not interface:* a `protected` interface member is callable only from derived interfaces,
   not from implementing classes — so an interface would force `Execute` to be `public` (seam
   destroyed). There is no polymorphic dispatch over runners, so an interface buys nothing. If a DI
   discovery need ever appears, add a thin marker `IRunner` (`RunnerBase : IRunner`) for identity —
   the `Execute` door stays on the class.
3. **No separate "kernel" assembly, no Passkey badge.** (A "kernel assembly" would have been a new
   project holding just the operation/gate primitives; instead they live in the existing
   Infrastructure project, beside the other floor plumbing.) Enforcement is the same
   internal-type-structure trick `Flow` uses today, hoisted: `IGate` is
   **`internal`-to-Infrastructure** instead of `private`-to-`Flow`. The seam spans assemblies
   *through inheritance* — a runner calls its own inherited `Execute` (no CS1540, no token-param
   boilerplate).

### Enforcement (type-first, arch-test backstop)

- **Type wall (primary):** `internal IGate` + `protected Operation.Invoke` + `protected
  RunnerBase.Execute` ⇒ an operation is executable **only** by a `RunnerBase` subclass, and that is
  unforgeable from any other assembly (no other type can name `IGate`). Identical idiom to the
  current Flow gate, now cross-assembly.
- **Arch rules (backstop + intent):** capabilities depend down-only (no `Domain.Git → Domain.Flow`);
  `Operation` subclasses never live in Infrastructure; keep the existing down-only DAG and the
  `Domain.Agents` internal-primitives wall. Treat the wall as a guard-rail, not a runtime trust
  boundary (reflection bypasses any compile-time seam).

### Consumer shape is unchanged

`Flow : RunnerBase` keeps `Run(ctx, op, args, ct)` verbatim; it now wraps the **inherited**
`Execute` with the flow policy (record, in-flight dedup, `Changed`, decision-drain). A flow author
writes exactly what they write today:

```csharp
public sealed class CustomFlow : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
        => await Run(ctx, git.Commit, commitArgs, ct);   // byte-identical to today
}
```

### What moves / collapses

- The nested `Flow.Operation<,>` class **and** its `Operations.Operation<,>` re-export collapse into
  **one** `Operation<,>` — which, with `OperationArgs`, `internal IGate`, and `RunnerBase`, lands in
  `Infrastructure/Operations/`.
- `Flow` gains `: RunnerBase`; its inline gate-cast becomes the inherited `Execute`. The
  `IDecisionSource` drain stays in `Flow.Run` (a flow policy), so the generic `Execute` never names
  it.
- `Domain.Git` operations subclass the Infrastructure `Operation` and **drop the `Domain.Flow`
  project reference** — the motivating win.
- The decision types stay where they already are: `IDecisionSource` + `DecisionDto` in
  `Domain.Flow.Operations`, the `DecisionKind` enum in `Domain.Agents` (it is not moving). The
  deferred `Domain.Decisions` leaf, if ever extracted, would gather these.

### Deferred (build on the second real use, not now)

- **Boundary command-pipeline** (ACL / request→command translation): build it by extracting from the
  **first** real endpoint→operation path, not up front — you cannot template a boundary you have
  never built once. Today's endpoints are lambdas over stubs; nothing drives an operation yet.
- **`Domain.Agents → Domain.Flow` edge:** `Agent` still implements `IDecisionSource` (a Domain.Flow
  contract), so Agents keeps one thin downward edge for *decisions only*. Leave it. When a **third**
  consumer needs decisions, extract a `Domain.Decisions` leaf (Agents produces, Flow records, neither
  depends on the other) and the edge dies. Two consumers ≠ "many" — no speculative leaf now.

### Migration (walking-skeleton)

1. Land `Operation` / `OperationArgs` / `internal IGate` / `RunnerBase` in `Infrastructure/Operations/`;
   `Flow : RunnerBase` reproduces today's behavior — green, consumers byte-identical. **Invariant:**
   the Infrastructure `Operation` and `RunnerBase.Execute` must not reference `IDecisionSource` (it
   stays in `Domain.Flow`); the decision-drain lives only in `Flow.Run`.
2. Repoint `Domain.Git` operations at Infrastructure; delete its `Domain.Flow` reference.
3. Delete the old `Flow.Operation` nesting + the `Operations.Operation` re-export.
4. Add/adjust the arch rules (down-only still green; new "Operation subclasses not in Infrastructure").

### Open decisions (one left)

- **Decisions ownership:** keep `IDecisionSource`/`DecisionDto` in `Domain.Flow` (Agents keeps its
  thin edge) vs extract `Domain.Decisions` now. Resolution: keep until a third consumer appears.

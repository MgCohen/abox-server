# Refactor Proposal — Actors, the `Run` gate, and `OperationArgs`

- **Status:** Proposed (2026-06-05). Design-on-paper; every snippet below was
  compile-validated in a scratch project, but **no `/src` code is written yet.**
- **Scope:** the rebuild (`/src`) run contract, the actor/operation model, and the
  Flow execution gate.
- **Refines / supersedes:** [ADR 0003](../../design/adr/0003-actors-operations-run-contract.md)
  (un-fuses actor from operation — **re-fused here**), [ADR 0004](../../design/adr/0004-provider-seam.md)
  (provider seam — **stands**), [ADR 0005](../../design/adr/0005-operation-args-generic.md)
  (args-as-generic — **adopted and hardened with the gate it lacked**). Grows out of
  [`l8-operation-pattern-and-shell-refactor.md`](l8-operation-pattern-and-shell-refactor.md).

## TL;DR

One reusable runnable — the **actor** — implements a gated interface keyed by a
**named args record**. A single `Flow.Run(actor, args)` is the only thing that can
execute it; it records the operation to a private ledger and streams progress. The
execution seam is hidden by C#'s type system (an `internal` gate interface + a
default-interface-method bridge), so flow-authors and actor-authors never see it and
cannot call it by following the grain of the code. Agents fuse into single-verb
actors that carry their session; multi-verb tools (Git) group their verbs on one
object. The name lives on the args, which collapses the no-arg/arg split into a
single shape.

## The shape

### The spine — `Flow` owns the gate, the ledger, and the only run path

```csharp
public abstract record OperationArgs(string Name);                 // name travels with the input

public abstract class Flow
{
    private readonly Ledger _ledger = new();                        // FlowContext dissolved into here
    public event Action? Changed;                                  // SSE / UI streaming

    internal interface IRunnable<TArgs, TResult> where TArgs : OperationArgs
    {
        Task<TResult> Execute(TArgs arg, CancellationToken ct);
    }

    private protected async Task<TResult> Run<TArgs, TResult>(
        IActor<TArgs, TResult> actor, TArgs arg, CancellationToken ct)
        where TArgs : OperationArgs
    {
        IRunnable<TArgs, TResult> runnable = actor;                 // only Flow can name IRunnable
        _ledger.Start(arg.Name, arg); Changed?.Invoke();           // label + args are inspectable
        try
        {
            var result = await runnable.Execute(arg, ct);
            _ledger.Complete(arg.Name, result?.ToString()); Changed?.Invoke();
            return result;
        }
        catch (Exception e) { _ledger.Fail(arg.Name, e.Message); Changed?.Invoke(); throw; }
    }
}
```

### The author surface — pure behavior, no `Name`, no gate visible

```csharp
internal interface IActor<TArgs, TResult> : Flow.IRunnable<TArgs, TResult>
    where TArgs : OperationArgs
{
    protected Task<TResult> Body(TArgs arg, CancellationToken ct);
    Task<TResult> Flow.IRunnable<TArgs, TResult>.Execute(TArgs a, CancellationToken ct) => Body(a, ct);  // DIM bridge
}
```

To author an actor you provide exactly one thing: `Body`. `Execute` is the inherited,
hidden bridge; you cannot see it.

### Args — the name is declared with the type, or threaded per call

```csharp
public sealed record CommitArgs(string Message, IReadOnlyList<string> Files) : OperationArgs("git-commit");
public sealed record PushArgs(string Branch)                                 : OperationArgs("git-push");
public sealed record DiffArgs()                                             : OperationArgs("git-diff");   // name only
public sealed record AgentArgs(string Intent, string Prompt)                : OperationArgs(Intent);       // call-level label
```

### Actors — agents fuse (single-verb, carry session); tools group (multi-verb)

```csharp
internal sealed class Agent(IProvider provider, AgentConfig config) : IActor<AgentArgs, AgentResult>
{
    private string? _sessionId;                                    // durable across runs
    async Task<AgentResult> IActor<AgentArgs, AgentResult>.Body(AgentArgs a, CancellationToken ct)
    {
        var r = await provider.DriveAsync(a.Prompt, _sessionId, ct);   // provider stays private
        _sessionId = r.SessionId;                                  // thread the session forward
        return new AgentResult(r.Text);
    }
}

internal sealed class Git(string projectDir)
    : IActor<CommitArgs, CommitResult>, IActor<PushArgs, PushResult>, IActor<DiffArgs, string>
{
    Task<CommitResult> IActor<CommitArgs, CommitResult>.Body(CommitArgs a, CancellationToken ct)
        => a.Files.Count == 0 ? throw new InvalidOperationException("no -A") : /* projectDir + a */;
    Task<PushResult> IActor<PushArgs, PushResult>.Body(PushArgs a, CancellationToken ct) => /* … */;
    Task<string>     IActor<DiffArgs, string>.Body(DiffArgs a, CancellationToken ct)     => /* … */;
}
```

### The call site — one `Run`, heterogeneous and interleaved

```csharp
public sealed class FullReview : Flow
{
    public async Task Go(string request, CancellationToken ct)
    {
        await Run(_implementer, new AgentArgs("implement", request), ct);                  // single-impl → infers
        var diff = await Run<DiffArgs, string>(_git, new DiffArgs(), ct);                  // multi-verb → explicit
        await Run(_implementer, new AgentArgs("fix", $"address: {diff}"), ct);             // same session continues
        await Run<CommitArgs, CommitResult>(_git, new CommitArgs("done", ["a.cs"]), ct);
        await Run<PushArgs, PushResult>(_git, new PushArgs("main"), ct);
    }
}
```

## Core decisions

- **D1 — One runnable, one executor.** `Flow.Run(actor, args)` is the sole execution
  entry. Everything is a unit of work that flows through it, so state, the ledger, and
  streaming are controlled in exactly one place. (This is the "single door" the whole
  design is built to enforce.)
- **D2 — Gate by inherited `internal` interface + DIM bridge** (Option B), not a token,
  not a base class. The public-ish `IActor` inherits an `internal` `Flow.IRunnable`; a
  default-interface-method bridges `Execute`→`Body`. The execution method is reachable
  only by code that can name `Flow.IRunnable` — i.e. `Flow` itself.
- **D3 — Actors are re-fused, single- or multi-verb.** An agent is a single-verb actor
  because it carries per-thread durable state (the session); a tool like Git is a
  multi-verb actor because its verbs share only stable collaborators (`projectDir`,
  guardrails). The rule is mechanical: *own durable per-invocation state → single-verb
  type; share stable collaborators → multi-verb.*
- **D4 — `OperationArgs` carries the name; the actor interface is behavior-only.** This
  collapses the no-arg/arg arity split into a single `IActor<TArgs, TResult>` shape, and
  makes the args record self-describing (`name + typed fields` = a turn-tool schema).
- **D5 — Collaborators via constructor, args via `Run`; `FlowContext` dissolves.** Run-wide
  values (`projectDir`) are baked into actors at composition by the factory; nothing is
  threaded to ops through a shared context object. `FlowContext` shrinks to `Flow`'s
  private ledger.
- **D6 — Name is a label, not an identity.** Verb ops bake a constant name in the args
  type; agents thread a per-call **intent** (`"implement"`, `"fix"`). Invocation identity
  is the ledger's positional record, not the name.

## Key insights (the load-bearing discoveries)

- **C# nested access is one-way.** A nested type reaches its enclosing type's privates;
  the enclosing type gets *nothing* special from its nested type (only `internal`/`public`).
  This is what lets a private/internal gate interface be invisible to actors yet usable by
  `Flow` — and what rules out several "obvious" arrangements that don't actually compile.
- **The single law of deferred-execution patterns:** *mutation has exactly one door, and
  the description never opens it itself.* (Command/Invoker, Redux dispatch, Elm `Cmd`,
  ZIO/Cats `IO`, Temporal activities.) Centralized ledger + streaming are not features we
  add — they are the consequence of there being one interpreter to wrap.
- **A minting method is pure indirection only when the call site could construct the op
  equally cleanly without it.** Git's old `Commit → CommitImpl` redirect was; an agent's
  `Run(prompt)` that binds private session is not. This is the test for when a factory
  earns its keep — and why most disappear here.
- **Multi-verb-on-one-object** can't be *inferred* (a type implementing `IActor<,>` twice
  fails type inference, `CS0411`) but works with explicit type args; it's impossible
  entirely with a base class (single inheritance). Hence: actors are interfaces, and
  multi-verb call sites are explicit.
- **The accessibility rule "a base interface must be ≥ as accessible as its derived
  interface"** forbids hiding the gate on a more-private parent of a more-public `IActor`.
  This is why the inherited-interface gate's ceiling is **assembly-granular** — and why a
  truly Flow-only gate needs either a base class (loses multi-verb) or a token (adds
  ceremony + a runtime hole).
- **Putting the name on `OperationArgs` removes the "no-arg" case** (every op has at least
  a name), which is what collapses the arity split into one shape.

## Structure protection — how the gate actually holds

1. **`Execute` is unspeakable to authors.** It lives on `internal Flow.IRunnable`,
   implemented explicitly via a DIM. An actor-author sees only `Body`; a flow-author sees
   only `Run`. Neither can name `Execute` to call it.
2. **Implementing `IActor` *is* wiring the gate.** You cannot author a runnable that
   escapes `Run` — the inheritance does it for you. There is no "forgot to register" path.
3. **`Run` is `private protected`.** Only `Flow` subclasses in the same assembly (or a
   named `[InternalsVisibleTo]` friend) can execute anything at all.
4. **The constraint `where TArgs : OperationArgs`** makes an unnamed operation
   unrepresentable — you cannot compile an op without a label.
5. **Multi-verb calls are explicit** (`Run<CommitArgs, CommitResult>(git, …)`) and fully
   type-checked — a wrong result/args pairing does not compile.

## Why this is better for AI

- **One path, one shape.** Every unit of work is `Run(actor, args)`. There is no "which
  overload," no `Unit`, no separate minting layer. Fewer decisions an AI can get wrong.
- **Minimal author surface.** A new actor is `: IActor<TArgs, TResult>` + `Body` + an
  args record. That template is short and hard to botch; the compiler fills in and hides
  the rest.
- **Enforcement is structural, not prose.** The wrong thing (running an op yourself) is
  not reachable by following the code; the right thing (`Run`) is the only public path.
  This is "structure over prose" in its honest form — the rule lives in the type graph,
  not in a `RunScope` parameter the author must copy and ignore.
- **Forced explicitness is a feature.** Explicit type args on multi-verb calls and the
  named-args constraint are *checked* structure — exactly the verbosity that helps an AI
  and a compiler agree.
- **Observability is free.** Because `Run` is the one door, the ledger and the `Changed`
  stream see every operation and its args without any per-actor effort.

## The hole (stated plainly)

The gate is **assembly-granular, not Flow-only.** Any type *in the same assembly* (or in a
named `[InternalsVisibleTo]` friend) can cast an actor to `Flow.IRunnable<,>` and call
`Execute` directly — bypassing `Run`, and therefore the ledger and the stream.

Why we accept it:

- **It is not reachable by drift.** `Execute` is hidden behind an explicit `internal`
  interface impl; `Run` is the obvious, documented, only-public path. An AI (or a human)
  following the grain of the code never casts to an internal interface to skip the runner.
- **The threat model is AI drift, not an adversary.** A determined same-assembly caller
  *can* bypass; so can reflection against any of the alternatives. The protection is
  discipline-grade, which is the right grade for this threat.
- **`[InternalsVisibleTo]` is all-or-nothing.** A named friend assembly sees *every*
  internal, not just the gate. Fine for first-party module splits and trusted plugins
  (the same mechanism tests already use); a broad grant for semi-trusted code.

If the threat ever changes (a hard wall against same-assembly code, or open third-party
authoring), the gate mechanism — and only the gate mechanism — would change to the token
or base-class alternative below; the actor/args/`Run` model stays the same.

## Consequences

- **`FlowContext` dissolves** into `Flow`'s private ledger; `ProjectDir` moves to
  actor/config, baked at composition. (Departs from ADR 0003 §3.)
- **Actors re-fuse with operations.** The "actor mints an operation" layer of ADR 0003 is
  removed; the actor *is* the runnable. "Operation" survives only as the ledger-record noun
  (`OperationRecord` / `OperationDto`).
- **Assembly layout is constrained.** `Flow`, `IActor`, `OperationArgs`, the actors, and
  the flows must live in one assembly, or be linked by `[InternalsVisibleTo]`. This likely
  keeps `Flow`/`IActor`/`OperationArgs` in `RemoteAgents` (or in `Core` with IVT to
  `RemoteAgents`) rather than a freely-referenced public contract.
- **Reusable actors are held as flow fields**; agents carry a mutable session, so an actor
  **must not run two operations concurrently** — a stated invariant, fine for the
  work→fix→review→revise model.
- **Args are turn-tooling-ready.** `(Name, typed fields)` is a tool definition; this
  sharpens ADR 0005 §7 at no extra cost.

## Tradeoffs

- **Assembly-granular gate** (this proposal) vs **Flow-only** (token / base class): we take
  the lighter, drift-appropriate gate and avoid both the token's ceremony and its runtime
  hole.
- **Named extensibility** (`[InternalsVisibleTo]`) vs **open third-party authoring**: we
  take named; open authoring against a published framework would require the token.
- **Name as instance data on every args** (even when type-constant for verb ops) — cheap
  for records, and it buys the per-call label flexibility agents need.
- **Multi-verb call sites are explicit** — accepted as checked structure rather than hidden
  inference.

## Alternatives explored (looked at, not discarded)

We deliberately built and compile-tested each of these; they remain valid fallbacks if a
requirement changes.

- **Token gate (`RunScope`, minted only by `Flow` via inverted nesting).** Gives a
  **Flow-only** gate *and* **open third-party** extensibility (consumers in any assembly can
  author flows/actors). We looked because it's the strongest gate compatible with
  multi-verb actors. Costs: a `RunScope` parameter threaded through every `Act` signature
  (ceremony the author carries but never uses) and a `null!` runtime hole that compiles.
  **Revisit if** open third-party authoring becomes a requirement.
- **Base class + private interface gate.** The **hardest** gate — `Execute` is totally
  unspeakable outside `Flow`, with no runtime hole — and it supports open extensibility. We
  looked because it's the cleanest enforcement. Cost: single inheritance forbids a cohesive
  multi-verb actor, so Git would split into grouped nested sub-actors. **Revisit if**
  multi-verb cohesion stops mattering, or a no-hole gate is required.
- **Verb-mints-a-private-op (args at construction).** Reads fluently (`agent.Turn(prompt)`)
  and keeps a single generic. We looked because of the nicer agent call site. Cost: the args
  are sealed in the op and not visible to the ledger, and it keeps a per-call minting
  method. The args-at-`Run` form was preferred for ledger-inspectable args and uniformity.
- **Fused `Operation<TArgs,TResult>` with a public `Execute`.** The simplest possible model.
  We looked, and rejected it as the baseline only because a public `Execute` provides **no
  gate at all** — anyone can run an op outside a flow.
```

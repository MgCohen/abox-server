---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-4, #flows]
---

# Layer 4 — Flows

## Target structure

A flow is **a single-shot, addressable, decoratable unit of orchestration.**
It starts, runs, ends. It is not composed of other flows. (See
[`99-rejected.md`](99-rejected.md) — composition is an explicit
non-goal.)

The contract is one interface:

```csharp
public interface IFlow
{
    string Name { get; }
    string? Summary { get; }
    Task<FlowResult> RunAsync(FlowContext ctx, FlowArgs args, CancellationToken ct);
}

public sealed record FlowResult(FlowExitReason Reason, string? Detail);

public enum FlowExitReason
{
    Shipped,
    NoChanges,
    ValidationFailed,
    VerdictUnclear,
    RevisionBrokeValidation,
    AbortedDirtyTree,
    BadArgs,
    Failed,
}
```

Each flow registers with `FlowRegistry` through the composition root.
The CLI dispatcher resolves by name. The Host's
`InProcessFlowExecutor` resolves by name. There's one place that
knows what flows exist.

**No flow body sets `Environment.ExitCode`.** That's the dispatcher's
job. The dispatcher reads `FlowResult.Reason`, maps to an exit code,
sets it once.

**No flow body opens a `try/catch (Exception)` block** around its
whole body. The runner wraps the flow in a uniform error envelope:
exceptions become `FlowResult(Failed, ex.Message)`, the session is
marked failed, the sink gets a final `FlowEvent` (or
`AgentEvent.Failed`), and the dispatcher exits.

**Decorators implement `IFlow` and wrap another `IFlow`** — but only
for **cross-cutting** concerns: timing, structured logging, audit-log,
retry-once. They observe and instrument the run; they do not encode
domain rules. Composition is **linear** (decorator → flow), not
recursive (flow → flow).

**Domain preconditions are not decorators and not flows.** A clean-tree
check, an arg validation, a `--push` gate — these belong in the flow
that needs them, as the first lines of its `RunAsync` body:

```csharp
public async Task<FlowResult> RunAsync(FlowContext ctx, FlowArgs args, CancellationToken ct)
{
    if (await ctx.Git.HasUncommittedAsync(ctx.ProjectDir, ct))
        return new(FlowExitReason.AbortedDirtyTree, "working tree dirty");

    // ... flow body
}
```

If two flows share a precondition, the *check* extracts to a helper
(`GitChecks.EnsureCleanAsync(...)`) — not the flow wrapping. A
precondition is a `bool` (or an early `FlowResult`); wrapping it in
`IFlow` would re-introduce flow-as-step composition, which R1 in
[`99-rejected.md`](99-rejected.md) rules out. (See also R10, narrowed:
decorators are observation-only, not gates.)

**`Reviews` and `Loops` stay as helpers**, but stop owning display
strings or agent construction:

- `Reviews.AskReviewerForVerdictAsync(IAgentFactory factory, IReviewProtocol protocol, ...)` —
  the protocol owns the prompt template and verdict parsing.
- `Loops.ValidateAndFixAsync(IValidator, Agent, ...)` — no `progressNote`
  or `fixDescriptor` display strings. Caller narrates if it wants to.

## Current structure

- **No `IFlow` interface.** Flows are top-level programs:
  `#:project ... using ... await using ctx ... try { ... } catch { ... }`.
- **Eight flow files** under [`cli/flows/`](../../remote-agents-dotnet/cli/flows/),
  each a separate `dotnet run` entry point with the same scaffolding.
- **Flow discovery is duplicated** in
  [`cli/agents-dotnet.cs:73-81`](../../remote-agents-dotnet/cli/agents-dotnet.cs)
  and [`Program.cs:63-91`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Program.cs)
  — both glob `cli/flows/*.cs`, both filter `!StartsWith("smoke-")`,
  both sort, both extract a description from the first comment block.
- **`FlowBootstrap.StartAsync` returns `null` on bad args**
  ([`FlowBootstrap.cs:48`](../../remote-agents-dotnet/src/RemoteAgents/Flows/FlowBootstrap.cs))
  with side-effect `Environment.ExitCode = 2`. Every flow then opens
  with `if (ctx is null) return`.
- **`Environment.ExitCode` is set in many places per flow**, with
  overloaded values:
  - `=2` for bad args (FlowBootstrap)
  - `=2` for validation failed (full-review)
  - `=2` for verdict unclear (full-review)
  - `=2` for dirty tree (FlowContext.EnsureCleanTreeAsync)
  - `=1` for caught exception (every flow's catch)
  - `=0` for success
- **`Loops.ValidateAndFixAsync`** takes `progressNote` and
  `fixDescriptor` display-string parameters
  ([`Loops.cs:21-30`](../../remote-agents-dotnet/src/RemoteAgents/Flows/Loops.cs)).
  Caller's flow knows the callee's UI strings.
- **`Reviews.AskCodexForVerdictAsync` constructs a CodexAgent**
  ([`Reviews.cs:54-59`](../../remote-agents-dotnet/src/RemoteAgents/Flows/Reviews.cs))
  with hardcoded options. The review prompt and the verdict syntax
  are baked in.
- **`FlowContext.EnsureCleanTreeAsync`** lives on `FlowContext` but
  is a pre-flight check ([`FlowBootstrap.cs:25-32`](../../remote-agents-dotnet/src/RemoteAgents/Flows/FlowBootstrap.cs))
  — really a decorator candidate.

## Gap

1. **No `IFlow` contract.** Flows are scripts. Cannot be looked up,
   decorated, tested in isolation, or invoked in-process by the Host.
2. **No `FlowRegistry`.** Two enumerations of the flows folder exist,
   each duplicating the filter + ordering rules.
3. **`Environment.ExitCode` is used as a control-flow channel** out
   of flow bodies and helpers, with no documented mapping. The
   dispatcher (CLI and Host) checks ≠ 0 and doesn't read the number.
4. **No `FlowResult` value.** A flow's outcome is `Environment.ExitCode`
   + `Session.End("string")`. Two channels carrying the same
   information.
5. **`Reviews` mixes orchestration, agent construction, prompt
   templating, and verdict parsing.** Four concerns in one helper.
6. **`Loops` mixes orchestration and display narration.** Caller passes
   `progressNote = " (Unity batch-mode, this can take minutes)"` —
   the caller's flow is telling the callee how to phrase its sink
   messages.
7. **Cross-cutting concerns are inline conditionals**, not decorators.
   `EnsureCleanTreeAsync` is a method on `FlowContext`; `--push` is
   parsed by `FlowBootstrap` and read by flows via `ctx.ShouldPush`;
   there's no `DryRunDecorator`, no `TimedFlowDecorator`, no
   `RequireCleanTreeDecorator`.
8. **Flows duplicate ~15 lines of try/catch + Session.End + ExitCode**
   each. Eight flows = ~120 lines of identical scaffolding.

## Migration steps

1. **Introduce `IFlow`, `FlowResult`, `FlowExitReason`, `FlowArgs`** in
   the contracts assembly (and `IFlow` in the library — it has an
   `RunAsync` method so it's not pure data, but the value types it
   exchanges are).
2. **Introduce `FlowRegistry`** + `services.AddFlow<T>()`. The CLI
   dispatcher and Host both resolve from it. Delete the two
   duplicate enumeration routines.
3. **Convert one flow first as a pilot** (`claude-only.cs` —
   smallest). Move the body into `ClaudeOnlyFlow : IFlow`. Keep the
   `cli/flows/claude-only.cs` script as a 3-line shim that resolves
   the registry and calls `RunAsync`. Verify the smoke still passes.
4. **Introduce `FlowRunner` (in library)** — owns the try/catch
   envelope. Takes an `IFlow`, runs it, returns `FlowResult`, marks
   the session, returns. The CLI dispatcher calls
   `flowRunner.RunAsync(flow, args)` and maps `FlowResult` to an exit
   code. (Distinct from the Host's `Host/Runs/FlowRunner.cs` — that
   one renames in [Layer 6](06-host.md) once the in-process executor
   lands.)
5. **Convert remaining flows.** Each becomes a class implementing
   `IFlow`. The `cli/flows/*.cs` shims stay so `dotnet run
   cli/flows/X.cs` still works for muscle-memory.
6. **Delete `Environment.ExitCode` from every flow body.** The CLI
   dispatcher's main owns the exit-code mapping. Flow bodies return
   `FlowResult`.
7. **Move `FlowContext.EnsureCleanTreeAsync` into a static helper**
   (`GitChecks.EnsureCleanAsync(projectDir, ct) → bool`). Flows that
   require a clean tree call it as the first line of `RunAsync` and
   return `FlowResult(AbortedDirtyTree, ...)` on `false`. No
   `RequireCleanTreeFlow : IFlow` class, no `Decorated<...>` wiring
   for it — preconditions are domain code in the flow body, not flow
   composition. (Cross-cutting decorators like `TimedFlow` /
   `LoggedFlow` are still valid and register via
   `services.AddFlow<X>().DecoratedWith<TimedFlow>()`.)
8. **Refactor `Loops.ValidateAndFixAsync`**: drop the `progressNote`
   and `fixDescriptor` parameters. The validator owns its own
   self-description (a `IValidator.Describe()` getter for the
   message); the caller narrates outside the helper. Net: ~25 lines,
   no display strings in the loop body.
9. **Refactor `Reviews.AskCodexForVerdictAsync`** into:
   - `IReviewProtocol` (prompt builder + verdict parser), e.g.
     `ApproveReviseProtocol`.
   - `IReviewer` (the actual orchestration, takes `IAgentFactory` +
     `IReviewProtocol`).
   - The flow calls `await reviewer.ReviewAsync(...)`. Helpers no
     longer hand-roll the prompt or hand-roll an agent.
10. **`FlowBootstrap.StartAsync` returns `FlowContext`** (not nullable).
    Bad args throw `FlowArgsException`. The runner catches it and
    returns `FlowResult(BadArgs, ex.Message)`. No more
    `if (ctx is null) return`.

## Acceptance criteria

Layer 4 is done when:

- Every flow file under `cli/flows/` (excluding the dispatcher) is
  either deleted or reduced to a < 5-line shim.
- `Environment.ExitCode` is set in exactly one place: the CLI
  dispatcher's main.
- `Loops.ValidateAndFixAsync` has no `string`-typed display parameter.
- `Reviews` does not contain `new CodexAgent(...)` or
  `new ClaudeAgent(...)`.
- `FlowRegistry` is the only place that enumerates available flows.
- At least one cross-cutting decorator (e.g. `TimedFlow` or
  `LoggedFlow`) exists and is registered through DI. No decorator
  encodes a domain precondition (clean tree, push, validation) — those
  live inside the flow body that needs them.
- Existing smoke tests pass — every flow's user-observable output
  unchanged.

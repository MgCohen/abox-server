The skeptics are in. Let me tally the survivors and write the final report.

**Confirmed (survived refutation):** A, B, C, D, G, L, M  
**Refuted:** E (IGate load-bearing), F (internal seam enforced), H (Pending state valid), I (namespace deliberate), J (wrapper is used), K (constraint identical), N (runtime dispatch correct), O (standard DI behavior), P (FlowConfig used independently), Q (catalog validates)

---

## Engine Audit — Final Report

### Executive Summary

The audit of 7 files in `src/RemoteAgents/Engine/` surfaced 7 confirmed findings (3 critical, 2 major, 2 minor) from 19 raw candidates — 12 were refuted after scrutinizing actual call sites and construction paths. The two most dangerous issues are structural bugs in `Flow.cs` that can permanently poison operation state and silently corrupt the `FlowContext` operation log. A third critical issue is that `FlowDefinition` holds an untyped `System.Type`, making illegal states representable at construction time even though the intended construction path (`FlowCatalog.Register<TFlow>`) is constrained.

---

### Findings Table

| # | Severity | Category | File:Line | Title |
|---|----------|----------|-----------|-------|
| 1 | **CRITICAL** | bug | `Flow.cs:56-57` | `StartOperation` and `Changed` outside `try` poisons `_inFlight` |
| 2 | **CRITICAL** | correctness | `Flow.cs:52-54` | `_inFlight` guard is instance-identity; misses `new Op()` callers |
| 3 | **CRITICAL** | correctness | `FlowDefinition.cs:3` | `FlowDefinition.FlowType` is untyped `System.Type` |
| 4 | **MAJOR** | bug | `Flow.cs:8,17` | Mutable `_ctx` field makes `ExecuteAsync` non-re-entrant |
| 5 | **MAJOR** | design | `FlowDefinition.cs:3` | `FlowDefinition` has no construction-time `FlowType` guard |
| 6 | minor | design | `Flow.cs:13,21` | `ctx` parameter of `RunAsync` is redundant with `_ctx` field |
| 7 | minor | quality | `FlowContext.cs:22` | Comment documents invariant the code should enforce |

---

### Critical Findings

#### 1 · `StartOperation` and `Changed` outside `try` permanently poisons `_inFlight`
**`Flow.cs:56-57`** | bug · critical

`_ctx.StartOperation` and `Changed?.Invoke()` execute **before** the `try` block that contains the `finally { _inFlight.TryRemove(op, out _) }`. If `StartOperation` throws (e.g., `OperationRecord.Start()` throws) or any `Changed` event handler throws, the `finally` is never reached. The `op` key stays in `_inFlight` for the lifetime of the `Flow` instance, and every subsequent call to `Run` with the same operation will throw `"already running on this actor"` even though nothing is running — permanently bricking that `Operation` instance.

```csharp
_ctx.StartOperation(args.Name);   // ← outside try
Changed?.Invoke();                 // ← outside try
try
{
    ...
}
finally
{
    _inFlight.TryRemove(op, out _);   // never reached if above throws
}
```

---

#### 2 · `_inFlight` guard uses object identity; misses `new Op()` per-call pattern
**`Flow.cs:52-54`** | correctness · critical

The concurrency guard uses the `op` reference as the `ConcurrentDictionary` key. When a subclass creates `new Op()` on each call to `RunAsync` (e.g., `await Run(new FetchOperation(), args, ct)`), each call produces a distinct object reference — `TryAdd` always succeeds and the guard never fires. Two concurrent `Run(new FetchOp(), ...)` calls both proceed, both call `_ctx.StartOperation`, and then both call `_ctx.CompleteOperation`/`FailOperation` which use `_operations[^1]` (last element). The second `StartOperation` displaces the `[^1]` slot, so whichever of the two operations finishes first completes the *other* operation's record — silently corrupting the operation log for both. The error message `"already running on this actor; sequence the calls"` implies type-level protection that the implementation does not provide.

```csharp
if (!_inFlight.TryAdd(op, 0))                    // key is object identity
    throw new InvalidOperationException(
        $"Operation '{args.Name}' is already running on this actor; sequence the calls.");
```

---

#### 3 · `FlowDefinition.FlowType` is an untyped `System.Type`
**`FlowDefinition.cs:3`** | correctness · critical

`FlowDefinition` is a `public sealed record` with a public primary constructor that accepts any `System.Type` without constraint. `new FlowDefinition(typeof(string), config)` compiles, constructs, and passes as a valid value. The illegal state is fully representable, violating "make illegal states unrepresentable." The only structural guard exists upstream in `FlowCatalog.Register<TFlow>() where TFlow : Flow`, but `FlowDefinition` itself is publicly constructible by anyone in the assembly. When an invalid `FlowType` reaches `IFlowFactory.Create`, the failure manifests as a runtime DI resolution exception or cast failure rather than an actionable error at the point of construction.

```csharp
public sealed record FlowDefinition(Type FlowType, FlowConfig Config);
//                                   ^^^^ accepts typeof(string), typeof(int), etc.
```

---

### Major Findings

#### 4 · Mutable `_ctx` field makes `ExecuteAsync` non-re-entrant
**`Flow.cs:8, 17`** | bug · major

`_ctx` is a plain instance field overwritten at the start of every `ExecuteAsync` call. If `ExecuteAsync` is called concurrently on the same `Flow` instance (or re-entered — nothing prevents it), the second call's `_ctx = ctx` overwrites the field while in-flight `Run` calls from the first execution still read `_ctx` by field reference (not a captured local). Every `_ctx.StartOperation`, `_ctx.CompleteOperation`, `_ctx.FailOperation`, and `_ctx.SetPhase` call from the first execution then silently mutates the second caller's `FlowContext`. The data corruption is invisible at the call site.

```csharp
private FlowContext _ctx = null!;   // mutable; no guard on concurrent assignment
...
public async Task ExecuteAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
{
    _ctx = ctx;   // overwrites any in-flight execution's context
    ...
}
```

---

#### 5 · `FlowDefinition` has no construction-time guard on `FlowType`
**`FlowDefinition.cs:3`** | design · major

Even though `FlowCatalog.Register<TFlow>` validates the `Name` and uses `where TFlow : Flow` to constrain type registration, `FlowDefinition` itself has no guard that throws an actionable error when `FlowType` is `null`, abstract, or not a `Flow` subclass. Any direct construction of `FlowDefinition` with an invalid `FlowType` silently produces an invalid object. Per the project rules: "throw actionable errors; make illegal states unrepresentable" — both apply here and neither is met at the record level.

```csharp
public sealed record FlowDefinition(Type FlowType, FlowConfig Config);
// no: if (!flowType.IsSubclassOf(typeof(Flow))) throw new ArgumentException(...)
```

---

### Minor Findings

#### 6 · `ctx` parameter of `RunAsync` is redundant with `_ctx` field
**`Flow.cs:13, 21`** | design · minor

`ExecuteAsync` stores `ctx` in the `_ctx` field and then forwards the same reference as a parameter to `RunAsync`. The `Run<TArgs,TResult>` helper uses `_ctx` (the field) exclusively — never the `RunAsync` parameter. Subclasses consequently receive `ctx` through both channels simultaneously. The asymmetry is subtle: a subclass author might reasonably expect that forwarding a different context to a helper would affect what `Run` tracks, but `Run` ignores any such forwarding and uses `_ctx` instead.

```csharp
protected abstract Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct);
...
_ctx = ctx;
...
await RunAsync(config, ctx, ct);   // ctx forwarded; Run() ignores it
```

---

#### 7 · Comment in `FlowContext` documents an invariant the code should enforce
**`FlowContext.cs:22`** | quality · minor

The comment `// Single-writer + sequential run: Complete/Fail close the operation just started.` exists because the `CompleteOperation`/`FailOperation`/`CancelOperation` methods use `_operations[^1]` without a guard. The project rules allow a comment only when the *why* is something the code genuinely cannot express — but this invariant *can* be expressed as a guard (`if (_operations.Count == 0) throw new InvalidOperationException("...")`). The comment is narrating a design decision rather than expressing something structurally impossible to code.

```csharp
// Single-writer + sequential run: Complete/Fail close the operation just started.
internal void CompleteOperation(string? summary) => _operations[^1].Complete(summary);
```

---

### What to Fix First

1. **Fix #1 (critical bug)** — Move `_ctx.StartOperation` and `Changed?.Invoke()` inside the `try` block in `Flow.Run`, or wrap the pre-try section in its own try/finally that removes `op` from `_inFlight` on any throw.

2. **Fix #2 (critical correctness)** — Decide on the intended concurrency contract. If operations must be serialized by type (not instance), key `_inFlight` on `op.GetType()`. If they must be serialized by name, key on `args.Name`. Document the contract in the error message and enforce it consistently.

3. **Fix #4 (major bug)** — Capture `ctx` as a local at the top of `Run<TArgs,TResult>` (or restructure `ExecuteAsync` + `Run` to pass context down the call chain rather than storing it in a mutable field).

4. **Fix #3 + #5 (critical + major, same root)** — Add a primary constructor guard to `FlowDefinition`: validate that `FlowType` is non-null, concrete, and `IsSubclassOf(typeof(Flow))`, throwing `ArgumentException` with a clear message. This collapses both issues into one fix.

5. **Fix #6 and #7** are cosmetic/structural cleanups; address after the above are stable.
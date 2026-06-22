---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-6, #host]
---

# Layer 6 — Host: transport, lifecycle, persistence

## Target structure

The Host **calls the library in-process** through `IFlow.RunAsync`.
It does not spawn subprocesses (for production runs), does not parse
session-id lines out of stdout, does not tail the library's transcript
file, does not parse Claude's per-session JSONL. The library emits
structured events; the Host's `ChannelSink` participates in the same
sink composite as the library's other sinks.

```csharp
public interface IFlowExecutor
{
    Task<FlowResult> RunAsync(LiveRun run, CancellationToken ct);
}

// Default — used by the Host.
public sealed class InProcessFlowExecutor : IFlowExecutor
{
    public InProcessFlowExecutor(IFlowRegistry flows, IEventSinkBuilder sinks, IRunArtifactStore artifacts) { ... }

    public async Task<FlowResult> RunAsync(LiveRun run, CancellationToken ct)
    {
        var flow = _flows.Resolve(run.Record.Flow);
        var ctx = await BuildContextAsync(run, sinks: _sinks.Build(run.Sink), ct);
        return await flow.RunAsync(ctx, run.Record.Args, ct);
    }
}

// CLI keeps a subprocess executor for `dotnet run cli/flows/foo.cs` ergonomics.
public sealed class SubprocessFlowExecutor : IFlowExecutor { ... }
```

The Host has **one event channel per run**. SignalR exposes one
stream method. The chat content (assistant text, thinking, tool use,
tool result) joins `AgentEvent` and rides the same channel.
`ClaudeJsonlTailer` deletes — the agent provider emits the structured
content directly from inside `DriveAsync` (it already has the JSONL
path).

`Run` splits into:

- `RunRecord` (in contracts) — persistable, wire-shareable.
- `LiveRun` (Host-internal) — wraps `RunRecord` plus `Sink`,
  `Cts`, `ExecutorTask`. No tailers. No second channel.

REST endpoints live in `RunsEndpoints.Map(app)` as their own module
(or `RouteGroupBuilder` extension), one typed method per handler.
`Program.cs` becomes the composition root only.

## Current structure

- [`FlowRunner.cs`](../../../remote-agents-dotnet/ui/ABox.Host/Runs/FlowRunner.cs)
  — 290 lines:
  - Spawns `dotnet run cli/agents-dotnet.cs run <flow> <project> <prompt>`.
  - Reads child stdout line by line.
  - Regex-matches each line against `SessionIdLine` to extract the
    session ID (lines 18-20, 191-194).
  - On match, computes the session dir by
    `Path.Combine(_orchestratorRoot, "sessions", run.SessionId)`.
  - Starts `TailTranscriptAsync` — opens
    `<sessionDir>/transcript.jsonl` with `FileShare.ReadWrite |
    FileShare.Delete` and polls.
  - Parses each line as `AgentEvent` and re-emits into the run's
    `ChannelSink`.
  - Also starts `ChatTailerTask` → `ClaudeJsonlTailer.RunAsync(...)`.
- [`ClaudeJsonlTailer.cs`](../../../remote-agents-dotnet/ui/ABox.Host/Runs/ClaudeJsonlTailer.cs)
  — 268 lines:
  - Polls `~/.claude/projects/<encoded-cwd>/` for new `*.jsonl` files.
  - Reads + reparses Claude's JSONL format: `type=summary`,
    `type=user`, `type=assistant`, `content` blocks (`text`,
    `thinking`, `tool_use`, `tool_result`).
  - Emits structured `ChatEvent`s into a separate `ChatChannel`.
- [`Run.cs`](../../../remote-agents-dotnet/ui/ABox.Host/Runs/Run.cs)
  — mixes live state and persistent state. Carries
  `ClaudeSessionId` (provider-specific).
- [`PersistedRun.cs`](../../../remote-agents-dotnet/ui/ABox.Host/Runs/PersistedRun.cs)
  — separate type with the persistent shape.
- [`Program.cs:170-183`](../../../remote-agents-dotnet/ui/ABox.Host/Program.cs)
  — three converter functions (`SummaryFromRun`, `SummaryFromCombined`,
  `SummaryFromPersisted`) for the same `RunSummary` shape.
- [`Program.cs:43-167`](../../../remote-agents-dotnet/ui/ABox.Host/Program.cs)
  — all REST endpoints as inline lambdas with embedded business
  logic (the `/runs/{id}/output` candidate-list).
- [`RunsHub.cs`](../../../remote-agents-dotnet/ui/ABox.Host/Hubs/RunsHub.cs)
  — two stream methods (`Stream` for `AgentEvent`, `StreamChat`
  for `ChatEvent`).

## Gap

1. **Subprocess + regex + file-tail as transport.** Six layers of
   indirection where in-process invocation would do. Spelled out in
   the original review; not repeating in full.
2. **`ClaudeJsonlTailer` is 268 lines of Claude-format parsing in
   the Host.** The library has this knowledge already
   (`ClaudeJsonl.cs`, `ProviderJsonlIngestSink.cs`). Three places
   know Claude's JSONL shape.
3. **Two streams, two channels, two tailers per run.** Direct
   consequence of #2.
4. **`Run` is the wrong shape:** carries runtime tasks, persistent
   data, and a provider-specific field (`ClaudeSessionId`).
5. **Three `RunSummary` projections.** Caller picks the right one
   per endpoint.
6. **REST handlers are inline lambdas with embedded logic.**
   `/runs/{id}/output` hardcodes flow-specific filenames.
7. **`FlowRunner.BuildProcessStartInfo` does its own env scrub**
   (third copy of the API-key blank).
8. **`FlowRunner` resolves `OrchestratorRoot` itself.** (Closed in
   [Layer 5](05-sessions.md), but listed here too for completeness.)

## Migration steps

This is the largest layer. Order matters; each step is a separately
landable delta.

### Step A — Introduce `IFlowExecutor` + in-process variant (no behavior change yet)

1. Add `IFlowExecutor` to contracts.
2. Add `InProcessFlowExecutor` to the library (or to `Host` for now —
   tradeoff: putting it in the library means it depends on `IFlow`
   already; OK after Layer 4).
3. Add `SubprocessFlowExecutor` carrying *exactly today's logic*
   from `FlowRunner.cs` — regex sniff, transcript tail, the works.
   No semantic change yet.
4. `Host/Runs/FlowRunner.cs` becomes a thin owner that picks the
   executor and manages `LiveRun` lifecycle around it. (Or delete
   it entirely if the executor + a new lightweight `RunSupervisor`
   cover its responsibilities.)
5. Register both executors via DI; pick `SubprocessFlowExecutor` for
   now. Build, smoke. Nothing user-visible changes.

### Step B — Library emits structured chat events from `DriveAsync`

6. **Add chat-content variants to `AgentEvent`** (per [Layer 3](03-events-and-sinks.md)):
   `AgentEvent.AssistantText`, `UserText`, `Thinking`, `ToolUse`,
   `ToolResult`, `SummaryNote`. (Same shapes as today's `ChatEvent.*`.)
7. **Move `ClaudeJsonlTailer`'s parsing logic into a library helper**
   (`Providers/Claude/ClaudeJsonlEventEmitter.cs`). Its API:
   `IAsyncEnumerable<AgentEvent> StreamAsync(Run record, CancellationToken)`.
8. **`ClaudeAgent.DriveAsync`** starts the emitter as a background
   task during the run, piping its events into the agent's sink.
   Tailing happens *inside* the agent, in-process, where it belongs.
9. Verify the `transcript.jsonl` now carries the structured chat
   events. (This may be a behavior change in transcript content —
   document it explicitly in the change. It's strictly *additive*:
   previously-emitted events still appear; new chat-content events
   are extra.)

### Step C — Switch the Host to in-process executor

10. **Flip the DI registration:** `InProcessFlowExecutor` becomes the
    default for the Host. `SubprocessFlowExecutor` stays available
    for tests and for the CLI dispatcher path.
11. **`LiveRun.Sink`** is the `ChannelSink`, passed directly into the
    composed sink composite the flow uses. No file tailing.
12. **Delete `FlowRunner.SessionIdLine` regex**, the
    `ReadStdoutAsync` session-id sniff, the
    `TailTranscriptAsync` file poller.
13. **Delete `ClaudeJsonlTailer.cs`** entirely. The library now
    emits the structured chat events directly into the same sink.
14. **Delete `Host/Sinks/ChatChannel.cs`, `Host/Hubs/ChatEvent.cs`.**
15. **Delete `RunsHub.StreamChat`.** One stream per run.
16. **Delete `Run.Chat`, `Run.ChatTailerTask`, `Run.ClaudeSessionId`.**
17. **`Run.ProjectDir`** stops being nullable — resolved upfront by
    the Host from `ProjectRegistry`.

### Step D — Split `Run` and collapse summary projections

18. **Introduce `LiveRun`** in `Host/Runs/`. It carries a
    `RunRecord record` field plus `Sink` + `Cts` + `ExecutorTask`.
19. **Delete `Run`, `PersistedRun`** as distinct types.
    `RunRegistry` indexes `LiveRun` by id and emits `RunRecord` on
    persistence. `RunStore` reads/writes `RunRecord`.
20. **Delete `SummaryFromRun`, `SummaryFromCombined`,
    `SummaryFromPersisted`.** Endpoints return `RunRecord` directly.

### Step E — Endpoint modules

21. **Move REST endpoints out of `Program.cs`** into
    `RunsEndpoints.Map(app)`, `ProjectsEndpoints.Map(app)`,
    `FlowsEndpoints.Map(app)`, `HealthEndpoint.Map(app)`.
22. **`/runs/{id}/output`** asks `Session.ReadArtifactAsync(...)`
    (per [Layer 5](05-sessions.md)). No candidate-list.

### Step F — Env scrub deduplication

23. **Delete `FlowRunner.BuildProcessStartInfo`** env scrub. The
    library's unified env scrub primitive (introduced in Layer 2)
    runs at the agent level instead. Subprocess executor — which now
    only matters for `dotnet run` smokes — gets it from the same
    primitive.

## Acceptance criteria

Layer 6 is done when:

- `Host/Runs/ClaudeJsonlTailer.cs` is deleted.
- `Host/Hubs/ChatEvent.cs` is deleted.
- `Host/Sinks/ChatChannel.cs` is deleted.
- `RunsHub.StreamChat` does not exist.
- `Run.ClaudeSessionId` does not exist.
- `Host/Runs/Run.cs` and `Host/Runs/PersistedRun.cs` are deleted;
  `LiveRun` is the only Host-internal run type, and `RunRecord`
  (in contracts) is the persisted/wire shape.
- `FlowRunner.SessionIdLine` (the regex) does not exist.
- `FlowRunner.TailTranscriptAsync` does not exist.
- `Program.cs` contains the composition root only — no REST
  handler lambdas with embedded business logic.
- `grep` for `"ANTHROPIC_API_KEY"` in `Host/` returns nothing
  (relies on the library's unified scrub).
- The Host can run any existing flow end-to-end against a real
  agent; UI gets one structured stream covering every event the
  old two streams carried.

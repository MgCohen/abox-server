---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-3, #events, #sinks]
---

# Layer 3 — Events & sinks

## Target structure

**Two event hierarchies, sibling under `OrchestrationEvent`:**

```
OrchestrationEvent
├── AgentEvent
│     ├── Started / StreamChunk / Completed / Failed
│     ├── DialogDismissed / ProviderSessionAttached
│     ├── NonInteractiveViolation
│     ├── AssistantText / UserText / Thinking
│     └── ToolUse / ToolResult / SummaryNote
└── FlowEvent
      ├── PhaseStart / PhaseOk / PhaseFail / PhaseInfo
      └── (other flow-narration variants as needed)
```

`AgentEvent.AgentName` always identifies the agent (the preset's
field name, e.g. `"Planner"`). `FlowEvent.Stage` identifies the flow
stage. **No field overloading.** `Phase` was overloading `AgentName`
to mean "agent or stage" — that goes away.

**No `Provider` string field on any event.** Provider identity is the
agent's runtime type (see [Layer 2](02-agents.md) and
[`99-rejected.md`](99-rejected.md) R12). The wire-side polymorphic
discriminator (`$kind` via `[JsonPolymorphic]`) is the only string
that crosses the boundary; it's managed by `System.Text.Json`, not
hand-rolled. If a consumer needs "which provider produced this run"
in summary form, it reads `RunRecord.AgentKind` (one enum, set once
at run start), not a per-event field.

The chat-content variants (`AssistantText`, `UserText`, `Thinking`,
`ToolUse`, `ToolResult`) join `AgentEvent`. The agent provider emits
them. The Host no longer parses Claude's JSONL itself —
`ProviderJsonlIngestSink`-style ingestion runs *inside the agent*,
emitting structured events into the same sink as everything else.

**Sinks are composed by registration:**

```csharp
services.AddABox(opts => {
    opts.AddSink<ConsoleSink>();
    opts.AddSink<JsonlSink>();
    opts.AddSink<ProviderJsonlArchiver>(); // renamed
});
// Host adds its own:
services.AddRemoteAgentSink<ChannelSink>();
```

`IEventSinkBuilder` builds the active sink composite at run start.
Flow code never instantiates a sink. The composition root chooses
which sinks are active per environment (CLI vs Host vs tests).

**`ProviderJsonlIngestSink` is renamed and relocated.** It is not a
sink — it's a post-run artifact archiver that listens for `Completed`
and copies a file. The new name reflects that. Its class moves out of
`Providers/Claude/` since it knows about both Claude and Codex.

## Current structure

- [`AgentEvent.cs`](../../remote-agents-dotnet/src/ABox/Core/Events/AgentEvent.cs)
  — single hierarchy. `Phase` is mixed in. `Phase.AgentName` is
  doc-commented as "reusing the slot for the entity producing the
  update."
- [`ChatEvent.cs` (Host)](../../remote-agents-dotnet/ui/ABox.Host/Hubs/ChatEvent.cs)
  — separate hierarchy with `AssistantText`, `UserText`, `Thinking`,
  `ToolUse`, `ToolResult`, `Meta`. Parsed by
  [`ClaudeJsonlTailer.cs`](../../remote-agents-dotnet/ui/ABox.Host/Runs/ClaudeJsonlTailer.cs)
  (268 lines, Host-side).
- [`EventSinkExtensions.cs`](../../remote-agents-dotnet/src/ABox/Core/Events/EventSinkExtensions.cs)
  — `PhaseStart/Ok/Fail/Info` extensions on `IEventSink`.
- [`FlowBootstrap.cs:79-83`](../../remote-agents-dotnet/src/ABox/Flows/FlowBootstrap.cs)
  — hardcoded triple: `new CompositeSink(new ConsoleSink(), jsonl,
  new ProviderJsonlIngestSink(...))`.
- [`ProviderJsonlIngestSink.cs`](../../remote-agents-dotnet/src/ABox/Providers/Claude/ProviderJsonlIngestSink.cs)
  — lives under `Providers/Claude/` but knows both Claude and Codex
  paths. Implements `IEventSink.EmitAsync` but only reacts to
  `Completed`; on every other event it returns `Task.CompletedTask`.
- [`ChannelSink.cs` (Host)](../../remote-agents-dotnet/ui/ABox.Host/Sinks/ChannelSink.cs)
  — `IEventSink` over a bounded `Channel<AgentEvent>`. Not registered
  through any builder — `FlowRunner` newing one per run.
- [`ChatChannel.cs`](../../remote-agents-dotnet/ui/ABox.Host/Sinks/ChatChannel.cs)
  — second channel for `ChatEvent`, written by `ClaudeJsonlTailer`.

## Gap

1. **`Phase` overloads `AgentName`** to mean "agent or stage." Doc
   comments admit it. Single typed hierarchy hides the split that
   should be explicit.
2. **`ChatEvent` exists as a separate hierarchy because the agent
   doesn't emit chat content** — the Host re-parses Claude's JSONL
   to recover it. Folding chat variants into `AgentEvent` (emitted by
   the agent) removes both `ChatEvent` and `ClaudeJsonlTailer`.
3. **Sink composition is hardcoded in `FlowBootstrap`.** Adding a
   sink means editing the library. Tests can't inject a capture sink
   without forking.
4. **`ProviderJsonlIngestSink` is misnamed and miscategorized.**
   It's not a sink in any meaningful sense (only Completed matters);
   it's a file copier. It lives under Claude's provider folder while
   knowing about both providers.
5. **`PhaseStart/Ok/Fail/Info` extensions live on `IEventSink`**
   ([`EventSinkExtensions.cs`](../../remote-agents-dotnet/src/ABox/Core/Events/EventSinkExtensions.cs))
   — flow-orchestration helpers attached to the base agent sink
   interface. Wrong owner.
6. **Two separate hub streams** (`/Stream` and `/StreamChat`) with
   two separate channels per `Run` are the consequence of the same
   problem #2 calls out. They go away when chat content joins the
   single stream.

## Migration steps

This layer is later in the sequence than its number suggests
(Phase 7 — see [`00-sequencing.md`](00-sequencing.md)) because the
target chat-event split depends on Layer 6's in-process executor.
But the **split into `AgentEvent` / `FlowEvent`** can land earlier.

1. **Introduce `OrchestrationEvent` abstract base** in the contracts
   assembly. Make `AgentEvent` derive from it. Add `FlowEvent` as
   sibling with the four `Phase*` variants (becoming `FlowEvent.PhaseStart`
   etc.).
2. **Migrate Phase events**: delete the `AgentEvent.Phase` variant.
   Update `Reviews.cs`, `Loops.cs`, `FlowBootstrap.cs`, every flow,
   `ConsoleSink`, `JsonlSink`, and the UI's switch in `RunView.razor`
   to handle the new `FlowEvent.PhaseX` variants. `Phase.AgentName`
   becomes `FlowEvent.PhaseX.Stage`.
3. **Move `PhaseStart/Ok/Fail/Info` extension methods** off
   `IEventSink` and onto a new `IFlowEventSink`. Or — if the same
   transport carries both — keep them as one interface but split the
   extension methods by event family so the names hint which side
   they belong to.
4. **Introduce `IEventSinkBuilder`** + `services.AddSink<T>()`.
   `FlowBootstrap` (or its successor in [Layer 4](04-flows.md)) asks
   the builder for the composed sink instead of newing one. Tests
   register a `CaptureSink` through DI; the Host registers its
   `ChannelSink` the same way.
5. **Rename `ProviderJsonlIngestSink` → `ProviderTranscriptArchiver`.**
   Move it from `Providers/Claude/` to a neutral
   `Core/Archives/`. Implement it as an `IAgentEventListener` (or
   keep as `IEventSink` that only handles `Completed`, but document
   that it's an archive trigger, not a transport).
6. **(Cross-link Layer 6.)** Once Layer 6 lands the in-process
   executor, fold chat-content variants
   (`AssistantText`/`UserText`/`Thinking`/`ToolUse`/`ToolResult`)
   into `AgentEvent`. The library's Claude `DriveAsync` emits them
   from inside the agent (it already has the JSONL path).
   `Host/Hubs/ChatEvent.cs`, `Host/Sinks/ChatChannel.cs`, and
   `Host/Runs/ClaudeJsonlTailer.cs` delete in the same change.
7. **`RunsHub.StreamChat` deletes.** One stream per run. UI client's
   second subscription deletes too.

## Acceptance criteria

Layer 3 is done when:

- The string `"reusing the slot"` (from the `AgentEvent.Phase`
  doc comment) does not appear in any source file.
- No source file emits a `Phase` event whose `AgentName` is not the
  name of an actual agent.
- Sink composition is performed by a builder pattern; `FlowBootstrap`
  contains no `new CompositeSink(...)`.
- `ProviderJsonlIngestSink` does not exist by that name; the
  replacement is not under `Providers/Claude/`.
- `Host/Runs/ClaudeJsonlTailer.cs`, `Host/Sinks/ChatChannel.cs`, and
  `Host/Hubs/ChatEvent.cs` are deleted.
- `RunsHub.StreamChat` does not exist.
- One hub stream per run carries every event the UI needs.
- No event record declares a `string Provider` field; provider
  identity is the polymorphic discriminator at the wire and
  `RunRecord.AgentKind` at the summary layer.

---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-1, #contracts]
---

# Layer 1 — Contracts (records)

## Target structure

One assembly, `RemoteAgents.Contracts`, holds every record that
crosses an assembly, process, or wire boundary. The assembly has
**only** `System.*` dependencies (no ASP.NET, no SignalR, no
provider SDKs). Both the Razor lib and the Host reference it. Both
the library and the Host emit the same `AgentEvent` shape; both
consume the same `RunRecord`. No mirror types.

Records in this layer are:

- Pure data (records with `init`-only or positional members).
- Polymorphic via `[JsonPolymorphic]` discriminators (kept stable
  forever — the wire shape is the contract).
- Free of behavior beyond trivial derived getters (`IsApprove` etc.
  are fine; I/O is not).
- Free of provider-specific fields. If a field is meaningful only for
  one provider, it lives in `ProviderMetadata` (open dict) or in a
  provider-specific record under a typed slot.

Enums replace stringly-typed status fields where the value set is
fixed and known.

## Current structure

- `RemoteAgents.csproj` (the library) owns:
  - [`AgentRunRequest.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Agents/AgentRunRequest.cs)
  - [`AgentResult.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Agents/AgentResult.cs)
  - [`AgentQuestion.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Agents/AgentQuestion.cs)
  - [`AgentStatus.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Agents/AgentStatus.cs)
  - [`InteractionMode.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Agents/InteractionMode.cs)
  - [`AgentEvent.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Events/AgentEvent.cs)
- `RemoteAgents.Host` owns:
  - [`Dtos.cs`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Dtos.cs)
    (`ProjectInfo`, `FlowInfo`, `StartRunRequest`, `RespondRequest`, `RunSummary`)
  - [`Hubs/ChatEvent.cs`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Hubs/ChatEvent.cs)
  - [`Runs/Run.cs`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Runs/Run.cs) — mixes runtime state and persistable state
  - [`Runs/PersistedRun.cs`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Runs/PersistedRun.cs) — the persistable projection
- `RemoteAgents.UI.Components` owns:
  - [`Models/WireShapes.cs`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Models/WireShapes.cs) — byte-identical mirror of `Dtos.cs`
  - [`Models/ChatEvent.cs`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Models/ChatEvent.cs) — byte-identical mirror of `Hubs/ChatEvent.cs`

## Gap

1. **`ChatEvent` is defined twice, byte-for-byte identical.** The
   comment in `Models/ChatEvent.cs` justifies it as "Razor lib can't
   reference ASP.NET Host." The fix is a third assembly that depends
   on neither, not a copy.
2. **`Dtos.cs` and `WireShapes.cs` are the same shapes** with
   different record names (`RunSummary` is identical; `StartRunRequest`
   too). Same justification, same answer.
3. **`Run` mixes runtime and persistent state.**
   `Sink`, `Chat`, `Cts`, `TailerTask`, `ChatTailerTask` are live;
   `Status`, `ExitCode`, `SessionId`, `SessionDir`, `FailureReason`,
   `Project`, `Flow`, `Prompt`, `Args` are durable. The result is
   *three projections of the same data* — `Run`, `PersistedRun`,
   `RunSummary` — converted by three sibling functions in
   [`Program.cs:170-183`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Program.cs).
4. **`Run.ClaudeSessionId`** ([`Run.cs:46`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Runs/Run.cs))
   is provider-specific on a provider-agnostic type. The moment Codex
   needs the same it grows `CodexSessionId`. Either an open
   `ProviderMetadata` dict or a typed `ProviderSessionRef` carries it.
5. **`AgentEvent.Phase.Status` is a `string`** with constants on the
   record ([`AgentEvent.cs:46-53`](../../remote-agents-dotnet/src/RemoteAgents/Core/Events/AgentEvent.cs)).
   The constants exist next to the field — the data shape is asking
   for an enum.
6. **`Session.End(string result, ...)`** ([`Session.cs:71`](../../remote-agents-dotnet/src/RemoteAgents/Core/Sessions/Session.cs))
   takes a free-form string. Values like `"shipped"`,
   `"validation-failed"`, `"verdict-unclear"`, `"revision-broke-validation"`,
   `"no-changes"`, `"aborted-dirty-tree"`, `"failed"` are scattered
   across flow files. No central enumeration. The Host doesn't read
   the value.
7. **`Reviews.CodexVerdict.Verdict`** ([`Reviews.cs:16`](../../remote-agents-dotnet/src/RemoteAgents/Flows/Reviews.cs))
   is a stringly-typed enum (`"approve"` / `"revise"` / `"unclear"`)
   with derived `IsApprove` / `IsRevise` / `IsUnclear` properties.
   Should be an enum.

## Migration steps

1. **Create `RemoteAgents.Contracts.csproj`.** TargetFramework matches
   the lowest common denominator (whatever the Razor WASM shell can
   reference). Zero deps. Add the new project to the solution.
2. **Move records with no internal deps first:**
   `AgentStatus`, `InteractionMode`, `AgentQuestion`, `AgentRunRequest`,
   `AgentResult`. `RemoteAgents.csproj` references contracts. Build.
3. **Move `AgentEvent` hierarchy.** All variants. Update every
   `using RemoteAgents.Events;` to also/instead pull the contracts
   namespace. Build, run smokes.
4. **Move `ChatEvent` hierarchy to contracts.** Delete
   `Models/ChatEvent.cs` from UI.Components; point UI at contracts.
   Delete `Hubs/ChatEvent.cs` from Host; point Host at contracts.
5. **Introduce `RunRecord` in contracts.** Fields = the persistable
   ones currently on `Run`. `RunSummary` becomes `RunRecord` directly
   (it's already isomorphic). Delete `Dtos.RunSummary`,
   `WireShapes.RunSummary`, and the three `SummaryFromX` converters.
6. **Move remaining wire DTOs to contracts:**
   `ProjectInfo`, `FlowInfo`, `StartRunRequest`, `RespondRequest`.
   Delete `Host/Dtos.cs` and `UI.Components/Models/WireShapes.cs`.
7. **Split `Run`:** the Host's `Run` becomes `LiveRun` carrying a
   mutable `RunRecord` plus the channels/tasks/CTS. All callers that
   read durable fields go through `live.Record.X`. (Layer 6 finishes
   this; this phase only introduces `RunRecord`.)
8. **Lift `Phase.Status` to enum.** Add `PhaseStatus { Start, Ok, Fail, Info }`.
   Update sinks' switch statements; add a `JsonStringEnumConverter`
   so the wire shape stays stable. Delete the `Start`/`Ok`/`Fail`/`Info`
   string constants on the Phase record.
9. **Lift `CodexVerdict.Verdict` to enum:** `Verdict { Approve, Revise, Unclear }`.
   Keep the derived `IsApprove`/`IsRevise`/`IsUnclear` getters for
   convenience.
10. **Lift `Session.End(result)` to enum:** `SessionResult { Shipped, NoChanges,
    ValidationFailed, VerdictUnclear, RevisionBrokeValidation, AbortedDirtyTree, Failed }`.
    Update every flow's call site.
11. **Replace `Run.ClaudeSessionId`** with `ProviderSessionRef(string Provider, string Id)`
    on `RunRecord`, populated by a new `AgentEvent.ProviderSessionAttached(...)` variant
    that providers emit. (Codex can emit the same record shape.)

## Acceptance criteria

Layer 1 is done when:

- `grep -r "WireShapes" remote-agents-dotnet/` returns nothing.
- Both `ChatEvent` files are gone; one definition lives in `RemoteAgents.Contracts`.
- `Run.ClaudeSessionId` does not exist.
- `RunSummary` does not exist as a separate type from `RunRecord`.
- No `Phase.Status` constants — the field is an enum.
- The contracts assembly's `csproj` has only `System.*` references.
- All existing flows produce byte-identical session-dir outputs.

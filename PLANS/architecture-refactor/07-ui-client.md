---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-7, #ui, #razor]
---

# Layer 7 — UI client (Razor)

## Target structure

The Razor library renders against the shared contracts assembly.
**No format-specific cleanup of upstream data, no knowledge of which
files the Host reads, no string heuristics over raw bytes.** Two
rendering surfaces, both fed by structured events:

1. **The "Output" panel** — renders the agent's distilled final
   answer. Sourced from `Session.ReadArtifactAsync(AssistantText)`
   via `/runs/{id}/output`. Renders as preformatted text (or
   markdown if we add a renderer later — out of scope).
2. **The "Conversation" panel** — renders structured `AgentEvent`s
   live, one Razor component per event variant:
   - `<UserTextBubble Text="...">`
   - `<AssistantTextBubble Text="...">`
   - `<ThinkingBlock Text="...">` (collapsed by default)
   - `<ToolUseCard Name="..." InputJson="...">`
   - `<ToolResultCard ForUseId="..." Content="..." IsError="...">`
   - `<FlowPhaseLine Stage="..." Status="..." Detail="...">`
   - `<RunLifecycleLine Variant="started|completed|failed" ...>`

3. **The "Raw stream" toggle (debug)** — a `<details>` element with
   the raw PTY bytes, **ANSI-stripped only**. No per-line heuristics,
   no spinner blocklist, no letter-ratio test, no dedup queue.

`HostApiClient` returns `Result<T, HostError>` (or throws a typed
`HostApiException`). The "every error becomes null" pattern is
deleted.

```csharp
public abstract record HostError;
public sealed record NotFound : HostError;
public sealed record NoContent : HostError;
public sealed record Network(string Reason) : HostError;
public sealed record Server(int StatusCode, string Body) : HostError;

public sealed record Result<T>(T? Value, HostError? Error)
{
    public bool IsOk => Error is null;
    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(HostError err) => new(default, err);
}
```

Or, simpler: typed exceptions; the page's `try/catch` decides what
to render. Either way, the distinction between "not yet" / "not
found" / "network blip" is preserved at the boundary.

The UI's mirror records (`Models/ChatEvent.cs`, `Models/WireShapes.cs`)
delete. The UI references `RemoteAgents.Contracts` directly.

## Current structure

- [`RunView.razor`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Pages/RunView.razor)
  — 278 lines. Contains:
  - Page layout (~60 lines).
  - SignalR connect/consume lifecycle (~30 lines).
  - `Sanitize` method (~50 lines) — TUI-spinner heuristics:
    glyph blocklist, ≥60% letters rule, sliding-window dedup,
    8-char minimum-length filter.
  - `_streamLen` property that aliases `_streamBuf.Length` (unused
    via the alias).
  - `_recentLines` field declared *after* the method that uses it.
  - Empty `catch { }` around `Api.GetOutputAsync`.
  - The Sanitize blob runs against the raw stream — making the
    "raw" debug view not raw.
- [`Models/ChatEvent.cs`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Models/ChatEvent.cs)
  — byte-identical mirror of the Host's `ChatEvent`.
- [`Models/WireShapes.cs`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Models/WireShapes.cs)
  — byte-identical mirror of Host `Dtos`.
- [`Api/HostApiClient.cs`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Api/HostApiClient.cs)
  — all methods return `T?` or silently map errors to null.
- [`Api/RunStreamClient.cs`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Api/RunStreamClient.cs)
  — SignalR client. Only subscribes to one stream today (the chat
  stream isn't wired in yet, but the Host already has it).

## Gap

1. **`RunView.Sanitize` is library-domain knowledge in a Razor page.**
   Per [the code-quality review](../../) (root README of this folder),
   this is the largest single piece of incidental complexity in the
   UI layer. Detailed there; not repeating.
2. **Two duplicate-record files** that the contracts assembly
   eliminates ([Layer 1](01-contracts.md)).
3. **`HostApiClient` collapses every error to null.** Page can't
   distinguish "not ready yet" from "404" from "500" from "network
   down."
4. **Empty `catch { }` around output fetch** at
   [`RunView.razor:149`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Pages/RunView.razor)
   — masks bugs.
5. **No structured rendering** for events. Every event becomes a
   line in a flat `<div class="log">` list. The "conversation"
   experience is missing because the wire shape never carried
   structured chat content in the stream (Host re-parses Claude's
   JSONL into a separate `ChatEvent` channel — [Layer 6](06-host.md)
   collapses this).
6. **`_streamLen` property is unused indirection.**
7. **`_recentLines` field declared after `Sanitize`.**

## Migration steps

This layer is mostly mechanical once Layers 1, 3, 6 land.

1. **(Depends on Layer 1.)** Delete `Models/ChatEvent.cs` and
   `Models/WireShapes.cs`. Point the Razor lib at
   `RemoteAgents.Contracts`.
2. **Delete `RunView.Sanitize`** and `_recentLines` and the
   `_streamLen` alias. Replace stream-chunk handling with:
   ```csharp
   case AgentEvent.StreamChunk c:
       var clean = AnsiHelpers.StripAnsi(c.Chunk);
       if (clean.Length == 0) break;
       _streamBuf.Append(clean);
       if (_streamBuf.Length > 100_000)
           _streamBuf.Remove(0, _streamBuf.Length - 100_000);
       _streamText = _streamBuf.ToString();
       break;
   ```
   Move `AnsiHelpers` into a shared assembly the Razor lib can
   reference (or into the contracts assembly if dependency-thin
   enough — it currently uses `System.Text.RegularExpressions` only,
   which is fine).
3. **Lift `HostApiClient` errors out of null-collapse.** Either
   `Result<T>` or typed exceptions — pick one, apply consistently.
   `GetOutputAsync` distinguishes 204-NoContent from 404 from
   network failure.
4. **Replace the empty catch** at
   [`RunView.razor:149`](../../remote-agents-dotnet/ui/RemoteAgents.UI.Components/Pages/RunView.razor)
   with explicit handling of the new error variants.
5. **(Depends on Layer 6.)** Once `AgentEvent` carries structured
   chat content (assistant text, thinking, tool use, tool result),
   build the per-variant Razor components listed above. Replace the
   flat `<div class="log">` with a `<ConversationPanel>` that
   routes each event to its component.
6. **(Depends on Layer 6.)** Delete the second stream subscription.
   One `RunStreamClient.StreamAsync(runId)` covers everything.

## Acceptance criteria

Layer 7 is done when:

- `RunView.Sanitize` does not exist.
- `_recentLines`, `_streamLen` properties do not exist.
- `Models/ChatEvent.cs` and `Models/WireShapes.cs` are deleted.
- No `catch { }` block exists in any Razor page or component.
- `HostApiClient` distinguishes at least three error categories
  (not-found, no-content, network/server).
- The "raw stream" debug view shows ANSI-stripped bytes with no
  further filtering.
- The "Conversation" panel renders structured events as
  semantic components (one component per event variant).
- The UI assembly references `RemoteAgents.Contracts` and not
  `RemoteAgents.Host`.

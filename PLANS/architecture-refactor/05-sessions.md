---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-5, #sessions, #paths]
---

# Layer 5 — Sessions & orchestrator paths

## Target structure

**One owner of "where the orchestrator lives":** `OrchestratorPaths`,
a primitive that resolves once at startup, hands every consumer the
same paths.

```csharp
public sealed record OrchestratorPaths(
    string Root,             // remote-agents-dotnet/
    string SessionsDir,      // remote-agents-dotnet/sessions/
    string FlowsDir,         // remote-agents-dotnet/cli/flows/
    string CliDispatcher);   // remote-agents-dotnet/cli/agents-dotnet.cs

public static class OrchestratorPathsFinder
{
    public static OrchestratorPaths Find(string? overrideRoot = null);
}
```

`overrideRoot` covers the config path the Host already supports
(`RemoteAgents:OrchestratorRoot`). With no override, walks for
`RemoteAgents.slnx`. The CLI dispatcher, the Host, and `Session.Start`
all call `OrchestratorPathsFinder.Find` exactly once and cache.

**One owner of the session-dir layout:** `Session`. It already owns
`prompt.txt`, `transcript.jsonl`, `meta.json`. It also owns the
artifact files that flows write (`claude-text.txt`,
`codex-review.txt`, `claude-raw.txt`, `codex-review.jsonl`, future
additions).

```csharp
public enum SessionArtifact
{
    Transcript,        // transcript.jsonl
    Prompt,            // prompt.txt
    Meta,              // meta.json
    AssistantText,     // claude-text.txt | codex-text.txt
    RawAssistantOutput,// claude-raw.txt  | codex-raw.txt
    ReviewVerdict,     // codex-review.jsonl
    ReviewText,        // codex-review.txt
}

public sealed class Session
{
    public string Path(SessionArtifact artifact);
    public Task WriteArtifactAsync(SessionArtifact artifact, string content, CancellationToken ct = default);
    public Task<string?> ReadArtifactAsync(SessionArtifact artifact, CancellationToken ct = default);
    public bool HasArtifact(SessionArtifact artifact);
    public IEnumerable<SessionArtifact> AvailableArtifacts();
}
```

No `Path.Combine(sessionDir, "claude-text.txt")` in any flow or in
the Host. Every read/write goes through `Session`.

**`PtySession` decision** (raised by Layer 2): rename to
`ClaudePtySession`, move to `Providers/Claude/`. The Claude-specific
knobs (`SubmitAsync(text, settleMs)`, `WaitIdleAsync(minWaitMs)`)
stay; the file's location now matches its actual ownership.
`AnsiHelpers` stays in `Core/Pty/` since it's genuinely generic.

## Current structure

- **Three `ResolveOrchestratorRoot` routines:**
  - [`cli/agents-dotnet.cs:19-28`](../../remote-agents-dotnet/cli/agents-dotnet.cs)
  - [`Host/Runs/FlowRunner.cs:275-289`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Runs/FlowRunner.cs)
  - [`Sessions/Session.cs:98-107`](../../remote-agents-dotnet/src/RemoteAgents/Core/Sessions/Session.cs)
    (different signature, slightly different walk)
- **File basenames hardcoded in many places:**
  - `"claude-text.txt"` — written by [claude-only.cs:33](../../remote-agents-dotnet/cli/flows/claude-only.cs),
    [claude-validate.cs:65](../../remote-agents-dotnet/cli/flows/claude-validate.cs);
    read by [Program.cs:137](../../remote-agents-dotnet/ui/RemoteAgents.Host/Program.cs).
  - `"codex-review.txt"` — written by
    [full-review.cs:116](../../remote-agents-dotnet/cli/flows/full-review.cs),
    [unity-review.cs:129](../../remote-agents-dotnet/cli/flows/unity-review.cs);
    read by [Program.cs:138](../../remote-agents-dotnet/ui/RemoteAgents.Host/Program.cs).
  - `"claude-raw.txt"` — written by four flows; not read anywhere
    yet (forensic dump).
  - `"codex-review.jsonl"` — written by
    [Reviews.cs:117](../../remote-agents-dotnet/src/RemoteAgents/Flows/Reviews.cs).
  - `"transcript.jsonl"`, `"prompt.txt"`, `"meta.json"` — owned by
    `Session`, but `transcript.jsonl` is also referenced by
    [`Host/Runs/FlowRunner.cs:222`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Runs/FlowRunner.cs)
    by string.
- **`Session.DefaultSessionsRoot`** walks the FS at every
  `Session.Start` call.
- **`PtySession`** under `Core/Pty/` — see Layer 2's gap #10.

## Gap

1. **Three FS walks for the same root**, each subtly different
   (one looks for `RemoteAgents.slnx`, one looks for
   `remote-agents-dotnet/`, one tries both with different fallbacks).
2. **Seven file basenames as string literals** spread across flows,
   helpers, and the Host. Adding a new artifact means updating the
   writer flow, the reader (Host), and remembering to clean it up.
3. **`Session` owns three of seven artifacts** — the other four
   are written directly by flows. Inconsistent ownership: the type
   that should own session layout owns less than half of it.
4. **The Host's `/runs/{id}/output` endpoint hardcodes the candidate
   file list** ([`Program.cs:135-139`](../../remote-agents-dotnet/ui/RemoteAgents.Host/Program.cs))
   — flow-specific filenames baked into a transport endpoint, with
   `Path.Combine` reconstruction.
5. **`PtySession` accumulates Claude-specific knobs** while named as
   a generic primitive.

## Migration steps

1. **Introduce `OrchestratorPaths` record** + `OrchestratorPathsFinder.Find()`
   in `Primitives/`. Find logic absorbs the union of the three
   existing routines (prefer `RemoteAgents.slnx`, fall back to
   walking for `remote-agents-dotnet/`, honor the config-override
   path).
2. **Delete `ResolveOrchestratorRoot` from `cli/agents-dotnet.cs` and
   `Host/Runs/FlowRunner.cs`.** Both call `OrchestratorPathsFinder.Find`
   instead. Cache the result on the Host as a singleton service.
3. **Delete `Session.DefaultSessionsRoot`'s walk.** `Session.Start`
   takes the paths object (or just `sessionsRoot`) as a parameter.
   Default behavior preserved at the call sites.
4. **Introduce `SessionArtifact` enum.** Map each entry to its
   basename in one private dictionary inside `Session`. (Provider
   prefix — `claude-text.txt` vs `codex-text.txt` — picked by a
   small map; if a future flow wants per-provider parallel artifacts,
   the enum gains explicit variants like `AssistantText_Claude`
   vs unified `AssistantText` with a provider parameter — TBD when
   we hit it.)
5. **Add `Session.Path(SessionArtifact)`,
   `WriteArtifactAsync(...)`, `ReadArtifactAsync(...)`,
   `HasArtifact(...)`, `AvailableArtifacts()`** methods. Implement
   on top of the basename map.
6. **Convert flow callsites** that write artifacts to go through
   `Session`:
   - `await ctx.Session.WriteArtifactAsync(SessionArtifact.AssistantText, work.Text)`
   - `await ctx.Session.WriteArtifactAsync(SessionArtifact.ReviewText, review.Text)`
   - etc.
7. **Convert Host `/runs/{id}/output`** to query `Session` for
   `SessionArtifact.AssistantText` first, then `SessionArtifact.ReviewText`.
   No `Path.Combine` for basenames in `Program.cs`.
8. **Convert `Reviews.WriteReviewArtifactAsync`** to use
   `Session.WriteArtifactAsync(SessionArtifact.ReviewVerdict, ...)`.
9. **Convert `Host/Runs/FlowRunner.TailTranscriptAsync`** to
   `Path.Combine(run.SessionDir, ...)` becomes
   `session.Path(SessionArtifact.Transcript)`. (This goes away
   entirely in Layer 6's in-process executor, but the conversion is
   still worth doing in the interim since FlowRunner survives as
   the subprocess executor for the CLI dispatcher path.)
10. **Rename `PtySession` → `ClaudePtySession`** and move to
    `Providers/Claude/`. Keep `AnsiHelpers` in `Core/Pty/`.
    Update the one user (`ClaudeAgent`) accordingly. (If at some
    point we add a Gemini agent that uses ConPTY, factor a real
    generic base out of `ClaudePtySession` then — not now.)

## Acceptance criteria

Layer 5 is done when:

- `grep -r "ResolveOrchestratorRoot" remote-agents-dotnet/` returns
  exactly one definition.
- The literal strings `"claude-text.txt"`, `"codex-review.txt"`,
  `"claude-raw.txt"`, `"codex-review.jsonl"`, `"transcript.jsonl"`
  do not appear outside `Session.cs` and `OrchestratorPaths.cs` and
  their tests.
- `Session` owns every artifact in the session dir.
- `PtySession` no longer exists by that name in `Core/Pty/`; the
  Claude-specific session driver lives under `Providers/Claude/`.
- Existing smoke tests produce byte-identical session-dir outputs.

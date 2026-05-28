# Architecture

Internals view of `remote-agents-dotnet/`. For day-to-day commands, read [`usage.md`](usage.md) first. For the design rationale and the decisions that shaped this code, read [`../../PLANS/csharp-orchestrator-prd.md`](../../PLANS/csharp-orchestrator-prd.md) and [`../../PLANS/csharp-orchestrator-build.md`](../../PLANS/csharp-orchestrator-build.md).

---

## 1. What this thing is

A local C#/.NET 10 orchestrator that drives `claude` and `codex exec` against Unity (or any) projects under **subscription billing** — Claude Max, ChatGPT Plus/Pro — not the per-token API.

The interesting part is how it gets there. The Claude CLI checks `isatty(stdin) && isatty(stdout)` to decide between subscription billing and the Agent SDK Credit pool. A plain `Process` silently fails that check and bills against the API. We spawn `cmd.exe` inside a ConPTY (via [Porta.Pty 1.0.7](https://www.nuget.org/packages/Porta.Pty/1.0.7)) and then write the actual `claude --session-id <uuid> ...\r` launch line to the spawned shell's stdin. Inside that PTY, both `isatty` checks pass.

`codex exec` does *not* need the trick — Codex has been officially supported on ChatGPT subscriptions since April 2026. Plain `Process` with redirected stdio is enough.

Everything else (sessions, events, sinks, validators, flows, the CLI shim) exists in service of those two execution shapes.

---

## 2. The four layers

Bottom-up. Each layer only depends on the layers below it.

```
┌─────────────────────────────────────────────────────────────────┐
│  4. Per-project tooling                                          │
│     flows/*.cs (file-based programs)                             │
│     agents/*.cs (named factories)                                │
│     validation/*.cs (IValidator impls)                           │
└─────────────────────────────────────────────────────────────────┘
        ▲ depends on
┌─────────────────────────────────────────────────────────────────┐
│  3. Events & sessions                                            │
│     AgentEvent (5 cases) · IEventSink · JsonlSink / ConsoleSink /│
│     CompositeSink / NoOpSink / ProviderJsonlIngestSink           │
│     Session · SessionMeta                                        │
└─────────────────────────────────────────────────────────────────┘
        ▲ depends on
┌─────────────────────────────────────────────────────────────────┐
│  2. Agents                                                       │
│     abstract Agent (sealed RunAsync lifecycle)                   │
│     ClaudeAgent (Porta.Pty)   CodexAgent (Process + --json)      │
│     virtual hooks: DetectStartupDialog, IsResponseComplete       │
└─────────────────────────────────────────────────────────────────┘
        ▲ depends on
┌─────────────────────────────────────────────────────────────────┐
│  1. Primitives                                                   │
│     GitOps · RunCommand · FsDiff · ProjectRegistry               │
│     SubscriptionGuard · AnsiHelpers · PtyExtensions              │
└─────────────────────────────────────────────────────────────────┘
```

### Layer 1 — Primitives ([`RemoteAgents/Primitives/`](../RemoteAgents/Primitives/), [`RemoteAgents/Pty/`](../RemoteAgents/Pty/))

Dumb verbs. No state, no inheritance, all static. Every async method takes `CancellationToken` (default `default`).

- `GitOps` — `Diff/DiffStat/Add/Commit/Push/CurrentBranch/IsDirty`. Refuses `git add` with no file list and `git push --force` to `main`/`master`.
- `RunCommand` — `Process` wrapper through `cmd.exe /c`. Captures stdout/stderr, supports stdin input, env overrides (null value → unset), timeout.
- `FsDiff` — directory snapshot (size + mtime ms) with a skip list (`.git`, `node_modules`, `sessions`, Unity `Library/Temp/Logs/UserSettings`, `obj/bin`, etc.). `Diff(before, after)` returns added/changed/removed.
- `ProjectRegistry` — walks up from CWD looking for `<repo>/projects.json`, resolves short names to absolute paths.
- `SubscriptionGuard` — refuses to start if `ANTHROPIC_API_KEY` / `CLAUDE_API_KEY` / `OPENAI_API_KEY` is set; probes `claude --version` and `codex --version` to fail fast on missing binaries.
- `AnsiHelpers.StripAnsi` — regex strip for parsing TUI buffers.
- `PtyExtensions.ExitCodeOrNull` — wraps Porta.Pty's throw-happy `ExitCode` getter.

### Layer 2 — Agents ([`RemoteAgents/Agents/`](../RemoteAgents/Agents/))

`abstract class Agent` owns the lifecycle. Subclasses own the mechanics.

```csharp
public abstract class Agent
{
    public required string Name { get; init; }
    public IEventSink Sink { get; init; } = NoOpSink.Instance;

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        await Sink.EmitAsync(new AgentEvent.Started(...), ct);
        try
        {
            var result = await ExecuteAsync(req, ct);
            await Sink.EmitAsync(new AgentEvent.Completed(...), ct);
            return result;
        }
        catch (Exception ex)
        {
            // Use CT.None so Failed lands even when the caller's CT canceled.
            await Sink.EmitAsync(new AgentEvent.Failed(...), CancellationToken.None);
            throw;
        }
    }

    protected abstract Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct);
}
```

Invariants:

- **`RunAsync` is not virtual.** The sealed lifecycle is the contract. Subclasses extend via `ExecuteAsync` and any subclass-specific virtual hooks.
- **`Started` always fires before any work.** If `ExecuteAsync` throws before doing anything, you get Started → Failed.
- **`Failed` always fires on exception**, including when the caller's `CancellationToken` was the cause — the emit uses `CancellationToken.None` so the event survives the cancellation.
- **Successful runs go Started → (StreamChunk | DialogDismissed)\* → Completed.**

Two concrete subclasses ship in the library, both **non-sealed** with two `virtual` protected hooks:

- `ClaudeAgent` — `cmd.exe` via Porta.Pty → ConPTY. Hooks: `DetectStartupDialog(buf)` (trust dialog / bypass warning) and `IsResponseComplete(buf, lastChunkAt)` (default: 6s idle threshold).
- `CodexAgent` — `cmd.exe /c codex exec` via plain `Process`. Streams `--json` stdout, reads final agent message from `-o <tmpfile>`. Tolerates schema drift via a defensive session-id scanner (root `thread_id` / `session_id` / `sessionId`, nested `.thread.id` / `.session.id`, `payload.*`).

Both blank API-key env vars in the spawned child env as defense in depth.

### Layer 3 — Events & sessions ([`RemoteAgents/Events/`](../RemoteAgents/Events/), [`RemoteAgents/Sessions/`](../RemoteAgents/Sessions/))

```csharp
public abstract record AgentEvent(DateTimeOffset At, string AgentName)
{
    public sealed record Started(...)         : AgentEvent(...);
    public sealed record StreamChunk(...)     : AgentEvent(...);
    public sealed record DialogDismissed(...) : AgentEvent(...);
    public sealed record Completed(...)       : AgentEvent(...);
    public sealed record Failed(...)          : AgentEvent(...);
}
```

This is the entire live-event vocabulary. **Deliberately small.** What's NOT here:

| Concern | Lives in |
|---|---|
| Tool calls | The ingested Claude/Codex provider JSONL (`claude-turn-N.jsonl`) |
| Token usage | Same |
| Rate-limit signals | Same |
| Provider-internal turn boundaries | Same |

That's the deal: the live transcript is for what *the orchestrator* decided (what was run, did it complete, did it fail). Everything the provider already records lives in the provider's own JSONL, which `ProviderJsonlIngestSink` copies into the session dir after each run.

`IEventSink` is the entire interface — one method, `EmitAsync(evt, ct)`. Compose freely:

```csharp
var sink = new CompositeSink(
    new ConsoleSink(),                                  // human-readable progress
    new JsonlSink(session.TranscriptFile),              // deterministic transcript
    new ProviderJsonlIngestSink(session.Dir, projDir)); // post-run provider telemetry
```

`CompositeSink` is **sequential** in registration order. Deterministic transcripts depend on that. `JsonlSink` also takes a `SemaphoreSlim` so two agents emitting concurrently produce line-atomic output.

`Session.Start(StartSessionRequest)` creates `sessions/<isoTs>-<slug>/` with three files and tracks meta until `End(result, failureReason?)`. The meta.json schema is versioned (`Orchestrator: "csharp"`, `SchemaVersion: "1"`) so a future replay viewer self-identifies.

### Layer 4 — Per-project tooling (`flows/`, `agents/`, `validation/`)

**Convention, not framework.**

- `flows/<name>.cs` — `.NET 10` file-based programs. First lines are `#:project ../RemoteAgents/RemoteAgents.csproj` (plus any others). Hand-written control flow; the library imposes none.
- `agents/<Name>.cs` — static factories returning configured `ClaudeAgent` / `CodexAgent`. System prompts loaded from `agents/prompts/<name>.md` via embedded resource (`Prompts.Load("name")`).
- `validation/<Project>Validator.cs` — implementations of `IValidator`. Bundled into `validation/Validators.csproj` which flows reference via `#:project`.
- `bin/agents-dotnet.cs` — CLI shim. `list / projects / run <flow> [...args]`. Spawns `dotnet run flows/<flow>.cs -- <args>` with stdio inherited.

This layer is the user's. The library doesn't reach into it; it's the other way around.

---

## 3. Repository layout

```
remote-unity-agents/
├── projects.json                      # <-- short-name → abs path lookup
├── PLANS/                              # PRD, build plan, design handover
├── remote-agents/research/             # historical design notes
└── remote-agents-dotnet/
    ├── RemoteAgents.slnx               # solution: 4 projects
    ├── RemoteAgents/                   # the library (net10.0)
    │   ├── Agents/
    │   ├── Events/
    │   ├── Sessions/
    │   ├── Primitives/
    │   ├── Validation/                 # IValidator + ValidationResult
    │   └── Pty/
    ├── RemoteAgents.Tests/             # xUnit (52 tests)
    ├── validation/                     # per-project validators
    │   ├── Validators.csproj
    │   ├── OrchestratorValidator.cs    # Roslyn syntax-only parse
    │   └── UnityBatchValidator.cs      # Unity.exe -batchmode -quit
    ├── agents/                         # named agent factories
    │   ├── NamedAgents.csproj
    │   ├── Planner.cs      (Claude opus)
    │   ├── Documenter.cs   (Claude haiku)
    │   ├── Researcher.cs   (Codex gpt-5.5)
    │   ├── Prompts.cs                  # embedded-resource loader
    │   └── prompts/*.md                # system prompts
    ├── flows/                          # .NET 10 file-based programs
    │   ├── claude-only.cs
    │   ├── claude-validate.cs
    │   ├── full-review.cs
    │   ├── unity-review.cs
    │   └── smoke-*.cs                  # CLI hides smoke-* from `list`
    ├── bin/
    │   └── agents-dotnet.cs            # CLI shim
    ├── sessions/                       # gitignored, per-run dirs
    └── docs/
        ├── architecture.md             # ← this file
        └── usage.md
```

`.slnx` (Microsoft's slimmer solution format) bundles four projects: `RemoteAgents`, `RemoteAgents.Tests`, `Validators`, `NamedAgents`. Flow files reference whichever of those csprojs they need via `#:project`.

---

## 4. Anatomy of a run

What `dotnet run flows/full-review.cs gear-engine "..."` does, step by step:

```
1. SubscriptionGuard.CheckAsync()
   ├─ refuse if ANTHROPIC_API_KEY | CLAUDE_API_KEY | OPENAI_API_KEY set
   └─ refuse if `claude --version` / `codex --version` nonzero

2. ProjectRegistry.Resolve("gear-engine") → "C:/Unity/Gear-Engine"

3. Session.Start(...)
   └─ writes sessions/<ts>-<slug>/{prompt.txt, transcript.jsonl, meta.json}

4. GitOps.IsDirtyAsync() → bail if dirty

5. CompositeSink wires ConsoleSink + JsonlSink + ProviderJsonlIngestSink

6. ClaudeAgent.RunAsync(prompt, sessionId=null, projectDir)
   ├─ Sink.EmitAsync(Started)
   ├─ ExecuteAsync:
   │   ├─ Porta.Pty spawns cmd.exe in projectDir, env-scrub ANTHROPIC_*/CLAUDE_*
   │   ├─ Reader task → emits StreamChunk per buffer
   │   ├─ Write `claude --session-id <uuid> --permission-mode acceptEdits\r`
   │   ├─ Dwell, then DetectStartupDialog(buf) → maybe emit DialogDismissed
   │   ├─ Write prompt + \r
   │   ├─ Poll until IsResponseComplete(buf, lastChunkAt) → 6s idle threshold
   │   └─ Write /exit\r, exit\r, WaitForExit
   └─ Sink.EmitAsync(Completed(SessionId, ExitCode, OutputChars))

7. ProviderJsonlIngestSink on Completed:
   └─ copy ~/.claude/projects/<encoded-cwd>/<uuid>.jsonl → session/claude-turn-1.jsonl

8. OrchestratorValidator.ValidateAsync(projectDir)
   ├─ walk **/*.cs, CSharpSyntaxTree.ParseText with FileBasedProgram feature
   └─ return ValidationResult { Ok, Summary, Errors }

9. If !Ok: ClaudeAgent.RunAsync(fixPrompt, sessionId=<previous>, projectDir)
   └─ resumes the same Claude session; loop up to MAX_FIX_ATTEMPTS

10. GitOps.DiffAsync(projectDir) → diffText

11. CodexAgent.RunAsync(reviewPrompt, sessionId=null, projectDir)
    ├─ Process spawns cmd.exe /c codex exec --cd <dir> -o <tmp> --json -
    ├─ stdin <<< prompt
    ├─ stream stdout lines: emit StreamChunk + scan for thread_id/session_id
    └─ read final agent message from -o tmpfile

12. Parse verdict (APPROVE/REVISE), write codex-review.jsonl

13. If REVISE: one Claude revision pass + re-validate

14. FsDiff.Diff(before, after) → file list

15. GitOps.CommitAsync(files, message + Codex verdict trailer, coAuthor)
    └─ [optional with --push] GitOps.PushAsync(branch)

16. Session.End("shipped")
    └─ meta.json updated with EndedAt + DurationMs + Result
```

Every async call takes the CancellationToken from the top-level flow. A `Ctrl-C` on the CLI is one `CTS.Cancel()` away from tearing the whole tree down cleanly.

---

## 5. The PTY trick, in detail

`ClaudeAgent.ExecuteAsync` (paraphrased):

```csharp
var ptyOpts = new PtyOptions
{
    App = @"C:\Windows\System32\cmd.exe",
    Cwd = req.ProjectDir,
    Environment = envWithApiKeysBlanked,
    Cols = 120, Rows = 40,
};
using var pty = await PtyProvider.SpawnAsync(ptyOpts, ct);

// Reader fans StreamChunks into the sink.
var readerTask = Task.Run(async () =>
{
    var buf = new byte[4096];
    while (!ct.IsCancellationRequested)
    {
        var n = await pty.ReaderStream.ReadAsync(buf, 0, buf.Length, ct);
        if (n == 0) break;
        var chunk = Encoding.UTF8.GetString(buf, 0, n);
        await Sink.EmitAsync(new StreamChunk(...));
    }
});

// Launch claude as a command typed into the running cmd.exe.
await Write(pty, $"claude {string.Join(' ', BuildClaudeArgs(...))}\r");

await Task.Delay(Options.InitialDwellMs, ct);
if (DetectStartupDialog(BufferSnapshot()) is "trust")
    await Write(pty, "\r");

await Write(pty, req.Prompt + "\r");

while (!IsResponseComplete(BufferSnapshot(), lastChunkAt))
    await Task.Delay(500, ct);

await Write(pty, "/exit\r");
await Task.Delay(Options.ExitDwellMs, ct);
await Write(pty, "exit\r");
pty.WaitForExit(15_000);
```

Two virtual hooks in v1, both protected. The contract: `DetectStartupDialog` returns one of `"trust" | "bypass-warning" | null`; `IsResponseComplete` returns `bool`. Other private mechanics (the dwell budget, the `\r` writes, the `/exit` choreography) are private until a real subclass need shows up.

The Claude session UUID is generated by us, passed via `--session-id <uuid>` on a fresh run or `--resume <uuid>` on a continuation. Because we own the UUID, we always know the path to the matching `~/.claude/projects/<encoded-cwd>/<uuid>.jsonl` for T0 ingestion.

---

## 6. JSON: source-gen only

.NET 10 file-based programs disable reflection-based `System.Text.Json` by default. The library declares three source-gen contexts:

- `SessionJsonContext` — `meta.json` (pretty)
- `EventJsonContext` — `transcript.jsonl` entries (compact, line-atomic)
- `ProjectsJsonContext` — `projects.json` (`Dictionary<string, string>`)

If you add a new serializable shape inside the library, add it to the appropriate context. Don't reach for reflection-based serialization.

For one-off JSON inside a flow file, hand-build the line (see how `flows/unity-review.cs` writes `codex-review.jsonl` — escape `\`, `"`, `\n`, `\r`, `\t` and interpolate).

---

## 7. Sessions on disk

```
sessions/2026-05-28T21-56-03-584Z-unity-review/
├── prompt.txt                         the verbatim user prompt
├── meta.json                          schema v1 (id, orchestrator, schemaVersion, timing, result)
├── transcript.jsonl                   the orchestrator's live events
├── claude-turn-1.jsonl                Claude's session JSONL (tool calls, token usage)
├── codex-turn-1.jsonl                 Codex's rollout JSONL
├── claude-raw.txt                     PTY raw byte dump (debug)
├── codex-review.jsonl                 {"verdict":"approve|revise|unclear", "sessionId", "text"}
└── codex-review.txt                   Codex's reply, untransformed
```

Want to know *what Claude actually did*? Read `claude-turn-N.jsonl`. Want to know *what the orchestrator did with it*? Read `transcript.jsonl`. The clean separation is by design.

---

## 8. CancellationToken everywhere

Every async method in this codebase takes a `CancellationToken` parameter. Plumbed end-to-end. A UI Stop button is one `CancellationTokenSource.Cancel()` away — no retrofit will be needed when/if a UI lands.

The one exception is `IEventSink.EmitAsync` calls in `RunAsync`'s `catch` block emitting `Failed` — those pass `CancellationToken.None` so the failure event lands even when the caller's CT was the cause of the failure.

---

## 9. Test strategy

The xUnit suite (`RemoteAgents.Tests/`, 52 tests) covers:

- Primitives, end-to-end (`GitOpsTests`, `FsDiffTests`, `ProjectRegistryTests`, `SubscriptionGuardTests`, `RunCommandTests`)
- Agent lifecycle invariants (`AgentLifecycleTests` against fake agents)
- ClaudeAgent / CodexAgent argv builders, sessionId scanner
- Sessions + sinks (`SessionAndSinksTests`, `ProviderJsonlIngestSinkTests`)
- Validators (`OrchestratorValidatorTests` against synthetic trees and the real repo; `UnityBatchValidatorTests` for path discovery)

What is **not** under xUnit:

- `ClaudeAgent.ExecuteAsync` (the PTY mechanics) — validated only by live smokes in `flows/smoke-*.cs`. Driving Porta.Pty in a unit test would mean spawning a real `cmd.exe`, which we already do in the smoke flows.
- `CodexAgent.ExecuteAsync` — same.
- The flows themselves — validated by running them end-to-end against real subscription-billed Claude/Codex.

This is a deliberate trade. Unit tests guard the deterministic pieces; smokes guard the integrations.

---

## 10. UI seam (for later)

The orchestrator runs in-process. There is no daemon, no IPC, no HTTP. A UI attaches by registering a `ChannelSink` — an `IEventSink` over `System.Threading.Channels<AgentEvent>` — and reading from the channel.

For **MAUI Blazor Hybrid**: UI project takes `ProjectReference` on `RemoteAgents.csproj`. Pass `AgentEvent` instances directly to Blazor components.

For **Tauri 2 + React**: write a thin `RemoteAgents.Host` ASP.NET project that wraps a flow runner in HTTP/JSON-RPC + WebSocket for live events.

Either path: **don't change the library's public types** to accommodate the UI. If a future change is needed (e.g. making `AgentEvent` more serialization-friendly), it's a library decision, not a UI patch.

---

## 11. Known limits

| Limit | Why |
|---|---|
| Windows-only v1 | `cmd.exe /c` callsites, Porta.Pty Windows-only path, hardcoded `C:\Program Files\Unity\Hub\Editor\` for `UnityBatchValidator`. Linux port = ~1–2d when Hetzner-VM time lands. |
| No `IAsyncEnumerable<AgentEvent>` return | By design (Q5). Events go through sinks only. |
| No live tool-call / token-usage events | By design (Q6). Those live in the ingested provider JSONLs. |
| Unity batch-mode dirties the tree every run | Provider-side reality (TMP_SDF auto-regen). Flows handle it by reverting non-`.cs` changes pre-commit (see `unity-review.cs`). |
| Claude `Completed.ExitCode == -1` in some runs | The PTY reader cancels before claude's `/exit` reply is captured. The flow's actual work still lands; the reported exit code is wrong. Known rough edge. |

---

## 12. Where to look next

- [`usage.md`](usage.md) — getting started, the four example flows, writing your own.
- [`../RemoteAgents/Agents/Agent.cs`](../RemoteAgents/Agents/Agent.cs) — the sealed lifecycle in 60 lines.
- [`../RemoteAgents/Agents/ClaudeAgent.cs`](../RemoteAgents/Agents/ClaudeAgent.cs) — the PTY mechanics.
- [`../RemoteAgents/Events/AgentEvent.cs`](../RemoteAgents/Events/AgentEvent.cs) — the five event cases.
- [`../flows/full-review.cs`](../flows/full-review.cs) — the most complete example flow; copy as a starting point.
- [`../../PLANS/csharp-orchestrator-prd.md`](../../PLANS/csharp-orchestrator-prd.md) — PRD (standalone-buildable from cold).
- [`../../PLANS/csharp-orchestrator-build.md`](../../PLANS/csharp-orchestrator-build.md) — build plan + 19 confirmed decisions.
- [`../../remote-agents/research/`](../../remote-agents/research/) — design notes from the JS-prototype era.

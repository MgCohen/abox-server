# Architecture

Local C#/.NET 10 orchestrator that drives `claude` and `codex exec` against your Unity projects under your **subscription billing** (Claude Max, ChatGPT Plus/Pro), not the per-token API. Started life as a JS prototype under `../../remote-agents/` (removed once C# parity shipped). Implemented as four layers in idiomatic C#.

This document is the internals view. For day-to-day commands, see [`usage.md`](usage.md). For decisions and tradeoffs, see [`../../PLANS/csharp-orchestrator-prd.md`](../../PLANS/csharp-orchestrator-prd.md) and [`../../PLANS/csharp-orchestrator-build.md`](../../PLANS/csharp-orchestrator-build.md).

---

## 1. The four layers

1. **Primitives** ([`RemoteAgents/Primitives/`](../RemoteAgents/Primitives/))
   Static helpers / records. Dumb verbs the flow author calls directly: `GitOps`, `RunCommand`, `FsDiff`, `ProjectRegistry`, `SubscriptionGuard`. Every async method takes a `CancellationToken`.

2. **Agents** ([`RemoteAgents/Agents/`](../RemoteAgents/Agents/))
   `abstract class Agent` owns a `sealed RunAsync` lifecycle (`Started` → `ExecuteAsync` → `Completed` on success, `Failed` + rethrow on exception). Concrete subclasses ship in the library: `ClaudeAgent` (Porta.Pty → ConPTY → `cmd.exe /c claude`) and `CodexAgent` (plain `Process` + `codex exec`). Both are **non-sealed** with two virtual hooks — `DetectStartupDialog(buf)` and `IsResponseComplete(buf, lastChunkAt)` — that downstream code overrides per project if Claude/Codex changes wording.

3. **Events & sinks** ([`RemoteAgents/Events/`](../RemoteAgents/Events/))
   `abstract record AgentEvent` with five sealed cases: `Started`, `StreamChunk`, `DialogDismissed`, `Completed`, `Failed`. `IEventSink.EmitAsync(evt, ct)` is the only logging surface. Built-in sinks compose: `JsonlSink` (transcript file), `ConsoleSink` (stdout), `CompositeSink` (fan-out), `ProviderJsonlIngestSink` (post-run copy of Claude/Codex's own session JSONLs into the session dir).

4. **Per-project tooling** (`agents/`, `validation/`, `flows/`)
   Convention, not framework. Static factories in [`../agents/`](../agents/), `IValidator` impls in [`../validation/`](../validation/), `.NET 10` file-based programs in [`../flows/`](../flows/). The library doesn't reach down into these — it's the other way around.

The library is in-process. There is no daemon, no IPC, no HTTP. A UI attaches by registering a `ChannelSink` (over `System.Threading.Channels<AgentEvent>`); the seam is documented in [§6 of the build plan](../../PLANS/csharp-orchestrator-build.md#6-ui-attachment-seam-for-later).

---

## 2. File map

```
remote-agents-dotnet/
├── RemoteAgents.slnx              # solution: 4 projects
├── RemoteAgents/                  # the library (net10.0)
│   ├── Agents/
│   │   ├── Agent.cs               # abstract base, sealed RunAsync
│   │   ├── ClaudeAgent.cs         # non-sealed, 2 virtual hooks
│   │   ├── ClaudeAgentOptions.cs
│   │   ├── CodexAgent.cs
│   │   ├── CodexAgentOptions.cs
│   │   ├── AgentRunRequest.cs
│   │   └── AgentResult.cs
│   ├── Events/
│   │   ├── AgentEvent.cs          # abstract record + 5 sealed cases
│   │   ├── IEventSink.cs
│   │   ├── JsonlSink.cs
│   │   ├── ConsoleSink.cs
│   │   ├── CompositeSink.cs
│   │   ├── NoOpSink.cs            # default sink
│   │   └── ProviderJsonlIngestSink.cs
│   ├── Sessions/
│   │   ├── Session.cs
│   │   ├── SessionMeta.cs
│   │   └── SessionJsonContext.cs  # source-gen for meta + events
│   ├── Primitives/
│   │   ├── GitOps.cs
│   │   ├── RunCommand.cs
│   │   ├── FsDiff.cs
│   │   ├── ProjectRegistry.cs     # reads ../projects.json (repo root)
│   │   └── SubscriptionGuard.cs
│   ├── Validation/
│   │   ├── IValidator.cs
│   │   └── ValidationResult.cs
│   └── Pty/
│       ├── AnsiHelpers.cs
│       └── PtyExtensions.cs       # ExitCodeOrNull()
├── RemoteAgents.Tests/            # xUnit (49 tests as of step 13)
├── validation/                    # per-project validators
│   ├── Validators.csproj          # tiny shim so flows can #:project this
│   └── OrchestratorValidator.cs   # Roslyn syntax-only parse
├── agents/                        # named agent factories
│   ├── NamedAgents.csproj
│   ├── Planner.cs                 # Claude opus + prompts/planner.md
│   ├── Documenter.cs              # Claude haiku + prompts/documenter.md
│   ├── Researcher.cs              # Codex gpt-5.5 + prompts/researcher.md
│   ├── Prompts.cs                 # embedded-resource loader
│   └── prompts/
│       ├── planner.md
│       ├── documenter.md
│       └── researcher.md
├── flows/                         # .NET 10 file-based programs
│   ├── claude-only.cs
│   ├── claude-validate.cs
│   ├── full-review.cs
│   └── smoke-*.cs                 # CLI hides these
├── bin/
│   └── agents-dotnet.cs           # CLI shim (file-based program)
├── sessions/                      # gitignored
└── docs/
    ├── architecture.md            # ← this file
    └── usage.md
```

---

## 3. The PTY trick (Claude)

Claude Code checks `isatty(stdin) && isatty(stdout)` to decide between subscription billing and the Agent SDK Credit pool. Driving the CLI from a plain `Process` (or `child_process.spawn`) fails that check, and the subscription path silently turns into API billing.

`ClaudeAgent` spawns `cmd.exe` via [Porta.Pty](https://github.com/microsoft/terminal/tree/main/src/cascadia/TerminalConnection) (a managed P/Invoke wrapper over Win32 ConPTY), then writes the actual `claude --session-id <uuid> ...\r` launch line to the spawned shell's stdin. Inside that PTY, both `isatty` checks pass, and Claude bills against the active subscription.

The smoke that proved this works lives at `C:\Unity\dotnet-pty-smoke\` (outside the repo).

`CodexAgent` does **not** need the trick: `codex exec` has been officially supported on ChatGPT subscriptions since April 2026. A plain `Process` with `RedirectStandardInput/Output` is sufficient.

---

## 4. The Agent lifecycle

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
            await Sink.EmitAsync(new AgentEvent.Failed(...), CancellationToken.None);
            throw;
        }
    }

    protected abstract Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct);
}
```

Key invariants:

- **Started always fires** before any work. Failures during `ExecuteAsync` still produce `Started` followed by `Failed`.
- **Failed always fires on exception**, including when the caller's `CancellationToken` is canceled — the sink emit uses `CancellationToken.None` so the failure event survives the cancellation that caused it.
- **`RunAsync` is not virtual.** The whole point of the seal is that subclasses cannot weaken the contract. The extension surface is `ExecuteAsync` plus any subclass-specific virtual hooks.

---

## 5. The five event cases

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

This is the entire live event vocabulary. **Deliberately small.** What's *not* here:

- No `ToolCallStarted` / `ToolCallEnded`. Those live in the provider session JSONLs that `ProviderJsonlIngestSink` copies post-run into `session.Dir/claude-turn-N.jsonl`.
- No `TokenUsage`. Same — captured by the provider, available in the post-run JSONL.
- No `RateLimit`. Same.

The split is deliberate: the live transcript is for the orchestrator's decisions (what was run, did it complete, did it fail). The provider JSONLs are for everything the provider already records (tool calls, usage, rate limits). One folder ends up with both.

---

## 6. Sessions

`Session.Start(...)` creates `sessions/<isoTs>-<slug>/` with three files:

- `prompt.txt` — the verbatim user prompt
- `transcript.jsonl` — one JSON object per event (kind discriminator + record fields)
- `meta.json` — `SessionMeta` with `id`, `orchestrator: "csharp"`, `schemaVersion: "1"`, timing, result

After the flow runs, additional files appear:

- `claude-turn-N.jsonl` / `codex-turn-N.jsonl` — provider session JSONLs (T0 ingest)
- `claude-raw.txt` / `claude-text.txt` — forensic dumps if the flow saved them
- `codex-review.jsonl` / `codex-review.txt` — Codex review verdict (full-review flow)

The new schema is a clean break from the JS orchestrator's `meta.json` (no compat shim per [Q16](../../PLANS/csharp-orchestrator-build.md#1-confirmed-decisions)). A future replay viewer distinguishes orchestrators by which `sessions/` folder a run lives in.

---

## 7. Sinks

`IEventSink.EmitAsync(evt, ct)` is the entire interface. Compose freely:

```csharp
var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile),
    new ProviderJsonlIngestSink(session.Dir, projectDir));
```

`CompositeSink` emits to children in registration order, sequentially. That keeps transcript ordering deterministic even with multiple agents emitting concurrently — there's a `SemaphoreSlim` inside `JsonlSink` to serialize append calls.

`ProviderJsonlIngestSink` listens for `AgentEvent.Completed`, then probes:

- `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`
- `~/.codex/sessions/YYYY/MM/DD/rollout-*-<sessionId>.jsonl`

…and copies the matching file into the session dir. Encoded cwd is the project path with `\`, `/`, `:` → `-`.

---

## 8. JSON: source-gen only

.NET 10 file-based programs disable reflection-based `System.Text.Json` by default. The library declares two `JsonSerializerContext`s:

- `SessionJsonContext` — pretty JSON for `meta.json`
- `EventJsonContext` — compact JSON for `transcript.jsonl`

Plus `ProjectsJsonContext` in `Primitives/` for `projects.json`. If you add a new serializable shape, add it to the appropriate context — don't reach for reflection-based serialization.

---

## 9. Cancellation

Every async method in this codebase takes a `CancellationToken` parameter (default `default`). This is plumbed end-to-end so a UI Stop button is one `CancellationTokenSource.Cancel()` away. No retrofit will be needed if/when a UI lands.

The one exception is `IEventSink.EmitAsync` calls inside `RunAsync`'s `catch` block for `Failed` — those pass `CancellationToken.None` so the failure event is delivered even when the cancellation is what caused the failure.

---

## 10. Subscription guard

`SubscriptionGuard.CheckAsync()` runs at flow start and refuses to proceed if:

- `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY`, or `OPENAI_API_KEY` is set in the env
- `claude --version` or `codex --version` returns non-zero (the binaries aren't on PATH)

The env-var check protects against the silent-API-billing failure mode. The `--version` probes fail fast when a flow would have crashed deep in `ExecuteAsync` for an avoidable reason.

`ClaudeAgent` and `CodexAgent` also blank the API-key env vars in the spawned child env, as defense in depth.

---

## 11. Where to look next

- [`usage.md`](usage.md) — getting started, the three example flows, common edits.
- [`../../PLANS/csharp-orchestrator-prd.md`](../../PLANS/csharp-orchestrator-prd.md) — PRD for the rewrite (standalone-buildable).
- [`../../PLANS/csharp-orchestrator-build.md`](../../PLANS/csharp-orchestrator-build.md) — build plan + decisions (Q1–Q19).
- [`../../PLANS/csharp-orchestrator-rewrite.md`](../../PLANS/csharp-orchestrator-rewrite.md) — design handover with the reference-library audit.
- [`../../remote-agents/research/`](../../remote-agents/research/) — historical design notes from the JS-prototype era (PTY pattern, billing-policy changes, alternatives considered, logging plan, etc.).

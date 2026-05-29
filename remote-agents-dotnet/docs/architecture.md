# Architecture

Internals view of `remote-agents-dotnet/`. For day-to-day commands, read [`usage.md`](usage.md) first. For the design rationale and the decisions that shaped this code, read [`../../PLANS/csharp-orchestrator-prd.md`](../../PLANS/csharp-orchestrator-prd.md) and [`../../PLANS/csharp-orchestrator-build.md`](../../PLANS/csharp-orchestrator-build.md).

---

## 1. What this thing is

A local C#/.NET 10 orchestrator that drives `claude` and `codex exec` against Unity (or any) projects under **subscription billing** — Claude Max, ChatGPT Plus/Pro — not the per-token API.

The interesting part is how it gets there. The Claude CLI checks `isatty(stdin) && isatty(stdout)` to decide between subscription billing and the Agent SDK Credit pool. A plain `Process` silently fails that check and bills against the API. We spawn `cmd.exe` inside a ConPTY (via [Porta.Pty 1.0.7](https://www.nuget.org/packages/Porta.Pty/1.0.7)) and then write the actual `claude --session-id <uuid> ...\r` launch line to the spawned shell's stdin. Inside that PTY, both `isatty` checks pass.

`codex exec` does *not* need the trick — Codex has been officially supported on ChatGPT subscriptions since April 2026. Plain `Process` with redirected stdio is enough.

Everything else (sessions, events, sinks, validators, flows, the CLI shim) exists in service of those two execution shapes.

---

## 2. The three layers

Bottom-up. Each layer only depends on the layers below it. The folder structure under `src/` mirrors this split exactly: `Core/`, `Providers/`, and the layer-3 consumers (`Flows/` + `NamedAgents/`).

```
┌─────────────────────────────────────────────────────────────────┐
│  3. Composed                                                     │
│     src/RemoteAgents/Flows/*.cs           (FlowBootstrap, Loops,│
│                                            Reviews, IsolationScope)│
│     src/NamedAgents/*.cs                  (Planner / Documenter /│
│                                            Researcher personas)  │
│     cli/flows/*.cs                        (top-level entry-point │
│                                            scripts users run)    │
└─────────────────────────────────────────────────────────────────┘
        ▲ depends on
┌─────────────────────────────────────────────────────────────────┐
│  2. Providers — adapters to external tools                       │
│     Claude/        ClaudeAgent (Porta.Pty) + ClaudeJsonl +       │
│                    ProviderJsonlIngestSink                       │
│     Codex/         CodexAgent (Process + --json)                 │
│     Unity/         UnityBatchValidator                           │
│     Orchestrator/  OrchestratorValidator                         │
└─────────────────────────────────────────────────────────────────┘
        ▲ depends on
┌─────────────────────────────────────────────────────────────────┐
│  1. Core — abstractions + reusable primitives, no vendor coupling│
│     Agents/      abstract Agent (sealed RunAsync lifecycle),     │
│                  AgentResult, AgentRunRequest                    │
│     Events/      AgentEvent (6 cases) · IEventSink · ConsoleSink/│
│                  JsonlSink / CompositeSink / NoOpSink            │
│     Pty/         PtySession, AnsiHelpers, PtyExtensions          │
│     Primitives/  GitOps · RunCommand · FsDiff · ProjectRegistry  │
│                  SubscriptionGuard · RepoRoot · Shell            │
│     Sessions/    Session · SessionMeta · SessionJsonContext      │
│     Validation/  IValidator · ValidationResult                   │
└─────────────────────────────────────────────────────────────────┘
```

**Dependency rule, enforced by eye:** Core knows nothing of Providers; Providers know nothing of Flows/NamedAgents. If you ever want to make this mechanical, split each provider into its own csproj — the `Providers/` name makes that natural.

### Layer 1 — Core ([`src/RemoteAgents/Core/`](../src/RemoteAgents/Core/))

Abstractions + reusable primitives. No vendor knowledge. The folder layout:

- **`Primitives/`** — dumb verbs, no state, all static, every async method takes `CancellationToken` (default `default`):
  - `GitOps` — `Diff/DiffStat/Add/Commit/Push/CurrentBranch/IsDirty`. Refuses `git add` with no file list and `git push --force` to `main`/`master`.
  - `RunCommand` — `Process` wrapper through `cmd.exe /c`. Captures stdout/stderr, supports stdin input, env overrides (null value → unset), timeout.
  - `FsDiff` — directory snapshot (size + mtime ms) with a skip list (`.git`, `node_modules`, `sessions`, Unity `Library/Temp/Logs/UserSettings`, `obj/bin`, etc.). `Diff(before, after)` returns added/changed/removed.
  - `ProjectRegistry` — walks up from CWD looking for `<repo>/projects.json`, resolves short names to absolute paths.
  - `SubscriptionGuard` — refuses to start if `ANTHROPIC_API_KEY` / `CLAUDE_API_KEY` / `OPENAI_API_KEY` is set; probes `claude --version` and `codex --version` to fail fast on missing binaries.
  - `RepoRoot` / `Shell` — walk-up helpers and a Windows-quoting helper.
- **`Pty/`** — `PtySession` (RAII over Porta.Pty), `AnsiHelpers.StripAnsi` (regex strip for parsing TUI buffers), `PtyExtensions.ExitCodeOrNull` (wraps Porta.Pty's throw-happy `ExitCode` getter).
- **`Agents/`** — `abstract class Agent` (sealed `RunAsync` lifecycle), plus `AgentResult` and `AgentRunRequest`. The abstraction; concrete agents live in `Providers/`.
- **`Events/`** — `AgentEvent` (6-case discriminated record), `IEventSink`, and the vendor-agnostic sinks (`ConsoleSink`, `JsonlSink`, `NoOpSink`, `CompositeSink`, plus `EventSinkExtensions` for `PhaseStart/Ok/Fail/Info` sugar).
- **`Sessions/`** — `Session.Start(...)` creates `sessions/<isoTs>-<slug>/` and tracks `SessionMeta` until `End(result, failureReason?)`. JSON shape lives in `SessionJsonContext` (source-gen).
- **`Validation/`** — `IValidator` and `ValidationResult`. The contract; concrete validators live in `Providers/`.

The `Agent` abstraction:

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

The `AgentEvent` vocabulary is **deliberately small** — six cases, five from the agent's lifecycle and one (`Phase`) for the flow's own step markers:

```csharp
public abstract record AgentEvent(DateTimeOffset At, string AgentName)
{
    public sealed record Started(...)         : AgentEvent(...);   // agent lifecycle
    public sealed record StreamChunk(...)     : AgentEvent(...);
    public sealed record DialogDismissed(...) : AgentEvent(...);
    public sealed record Completed(...)       : AgentEvent(...);
    public sealed record Failed(...)          : AgentEvent(...);
    public sealed record Phase(...)           : AgentEvent(...);   // flow-level phase markers (start/ok/fail/info)
}
```

What's NOT here lives in the ingested provider JSONL (`claude-turn-N.jsonl` / `codex-turn-N.jsonl`):

| Concern | Lives in |
|---|---|
| Tool calls | Provider JSONL |
| Token usage | Provider JSONL |
| Rate-limit signals | Provider JSONL |
| Provider-internal turn boundaries | Provider JSONL |

The live transcript is for what *the orchestrator* decided. Everything the provider already records, the provider keeps.

### Layer 2 — Providers ([`src/RemoteAgents/Providers/`](../src/RemoteAgents/Providers/))

Adapters to external tools. One folder per vendor. Each implements one or more Core contracts (`Agent`, `IValidator`, `IEventSink`).

- **`Providers/Claude/`** — `ClaudeAgent` subclasses `Agent`, spawns `cmd.exe` via Porta.Pty → ConPTY, types `claude --session-id <uuid> ...` into the spawned shell. Two `protected virtual` hooks for projects that need to extend it:
  - `DetectStartupDialog(buf)` — returns `"trust"`, `"bypass-warning"`, or `null`.
  - `IsResponseComplete(buf, lastChunkAt)` — default 6s idle threshold.
  Also here: `ClaudeAgentOptions` (record), `ClaudeJsonl` (parses `~/.claude/projects/<encoded>/<sessionId>.jsonl` for assistant text), and `ProviderJsonlIngestSink` (copies that JSONL into the session dir after each completed run).
- **`Providers/Codex/`** — `CodexAgent` subclasses `Agent`, spawns `cmd.exe /c codex exec` via plain `Process`, streams `--json` stdout, reads final agent message from `-o <tmpfile>`. Tolerates schema drift via a defensive session-id scanner (root `thread_id` / `session_id` / `sessionId`, nested `.thread.id` / `.session.id`, `payload.*`).
- **`Providers/Unity/`** — `UnityBatchValidator` implements `IValidator` by running `Unity.exe -batchmode -nographics -quit -projectPath <dir>` and parsing the editor log.
- **`Providers/Orchestrator/`** — `OrchestratorValidator` implements `IValidator` by walking `**/*.cs` and parsing with `CSharpSyntaxTree.ParseText` (FileBasedProgram feature). Used by `claude-validate.cs`.

Both agent providers blank API-key env vars in the spawned child env as defense in depth.

### Layer 3 — Composed

**Convention, not framework.** Three flavors:

- **Flow helpers** — [`src/RemoteAgents/Flows/`](../src/RemoteAgents/Flows/) — small reusable units of work each flow script composes itself: `FlowBootstrap` (arg parsing + session/sink wiring), `Loops.ValidateAndFixAsync` (the validate→fix while-loop), `Reviews.AskCodexForVerdictAsync` (Codex review + verdict parse + artifact), `IsolationScope` (snapshot+restore for noisy validators). There is no pipeline class — each entry-point script writes its own sequence.
- **Personas** — [`src/NamedAgents/`](../src/NamedAgents/) — static factories returning configured `ClaudeAgent` / `CodexAgent` with a role-specific prompt. System prompts live as `prompts/<name>.md` files; `Prompts.Load("name")` reads from disk on every call (walks up from CWD / `AppContext.BaseDirectory` to find `remote-agents-dotnet/src/NamedAgents/prompts/`), so editing a `.md` picks up on the next agent run with no rebuild. Separate csproj is purely an organizational choice — no layering implication.
- **Entry-point scripts** — [`cli/flows/<name>.cs`](../cli/flows/) — `.NET 10` file-based programs. First lines are `#:project ../../src/RemoteAgents/RemoteAgents.csproj` (plus `#:project ../../src/NamedAgents/NamedAgents.csproj` if you use personas). Hand-written control flow; the library imposes none.
- **CLI shim** — [`cli/agents-dotnet.cs`](../cli/agents-dotnet.cs) — `list / projects / run <flow> [...args]`. Spawns `dotnet run cli/flows/<flow>.cs -- <args>` with stdio inherited.

This layer is the user's. The library doesn't reach into it; it's the other way around.

---

## 3. Repository layout

```
remote-unity-agents/
├── projects.json                      # <-- short-name → abs path lookup
├── PLANS/                              # PRD, build plan, design handover
├── research/                           # historical design notes (JS-prototype era)
└── remote-agents-dotnet/
    ├── Directory.Build.props           # UseArtifactsOutput=true — all bin/obj → artifacts/
    ├── RemoteAgents.slnx               # solution: 3 projects
    ├── src/
    │   ├── RemoteAgents/               # the library (net10.0)
    │   │   ├── RemoteAgents.csproj
    │   │   ├── Core/                   # layer 1: abstractions + primitives, no vendor coupling
    │   │   │   ├── Agents/             #   Agent, AgentResult, AgentRunRequest
    │   │   │   ├── Events/             #   IEventSink + ConsoleSink/JsonlSink/NoOpSink/CompositeSink/EventSinkExtensions + AgentEvent
    │   │   │   ├── Pty/                #   PtySession, AnsiHelpers, PtyExtensions
    │   │   │   ├── Primitives/         #   GitOps, RunCommand, FsDiff, ProjectRegistry, SubscriptionGuard, RepoRoot, Shell
    │   │   │   ├── Sessions/           #   Session, SessionMeta, SessionJsonContext
    │   │   │   └── Validation/         #   IValidator, ValidationResult
    │   │   ├── Providers/              # layer 2: adapters to external tools
    │   │   │   ├── Claude/             #   ClaudeAgent + Options + ClaudeJsonl + ProviderJsonlIngestSink
    │   │   │   ├── Codex/              #   CodexAgent + Options
    │   │   │   ├── Unity/              #   UnityBatchValidator
    │   │   │   └── Orchestrator/       #   OrchestratorValidator
    │   │   └── Flows/                  # layer 3: small reusable flow helpers (no pipeline class)
    │   │       ├── FlowBootstrap.cs    #   FlowContext / shared session+sink wiring
    │   │       ├── Loops.cs            #   ValidateAndFixAsync — the validate→fix while-loop
    │   │       ├── Reviews.cs          #   AskCodexForVerdictAsync + commit-message builder
    │   │       └── IsolationScope.cs   #   snapshot+restore for noisy validators (Unity batch-mode)
    │   └── NamedAgents/                # layer 3: persona agents (separate csproj for organization)
    │       ├── NamedAgents.csproj
    │       ├── Planner.cs        (Claude opus)
    │       ├── Documenter.cs     (Claude haiku)
    │       ├── Researcher.cs     (Codex gpt-5.5)
    │       ├── Prompts.cs              # disk loader — walks up via RepoRoot, reads fresh each Load()
    │       └── prompts/*.md            # system prompts (read at runtime, edit live)
    ├── tests/
    │   └── RemoteAgents.Tests/         # xUnit
    ├── cli/                            # entry-point scripts
    │   ├── agents-dotnet.cs            #   CLI shim
    │   └── flows/                      #   .NET 10 file-based programs
    │       ├── claude-only.cs
    │       ├── claude-validate.cs
    │       ├── full-review.cs
    │       ├── unity-review.cs
    │       └── smoke-*.cs              #   CLI hides smoke-* from `list`
    ├── artifacts/                      # all build output (gitignored)
    ├── sessions/                       # per-run session dirs (gitignored)
    └── docs/
        ├── architecture.md             # ← this file
        └── usage.md
```

`.slnx` (Microsoft's slimmer solution format) bundles three projects: `RemoteAgents`, `NamedAgents`, `RemoteAgents.Tests`. Entry-point scripts reference whichever csprojs they need via `#:project` headers (always with the `../../src/...` prefix, since they live two levels deep under `cli/flows/`).

Build output is centralized via `Directory.Build.props` (`<UseArtifactsOutput>true</UseArtifactsOutput>`) — instead of a `bin/obj` pair under each csproj, everything lands in `artifacts/{bin,obj}/<ProjectName>/<Config>/<TFM>/`. One folder to gitignore, one folder to nuke when MSBuild gets weird.

---

## 4. Anatomy of a run

What `dotnet run cli/flows/full-review.cs gear-engine "..."` does, step by step. The flow file itself is the script — every numbered step below is a line or short block in [`full-review.cs`](../cli/flows/full-review.cs), no hidden pipeline.

```
 1. FlowBootstrap.StartAsync(args, "full-review")
    ├─ parse <project> "<prompt>" [--push]
    ├─ SubscriptionGuard.CheckAsync()
    │   ├─ refuse if ANTHROPIC_API_KEY | CLAUDE_API_KEY | OPENAI_API_KEY set
    │   └─ refuse if `claude --version` / `codex --version` nonzero
    ├─ ProjectRegistry.Resolve("gear-engine") → "C:/Unity/Gear-Engine"
    ├─ Session.Start(...) → sessions/<ts>-<slug>/{prompt.txt, transcript.jsonl, meta.json}
    └─ Sink = Composite(ConsoleSink, JsonlSink, ProviderJsonlIngestSink)

 2. ctx.EnsureCleanTreeAsync()  → bail if dirty

 3. claude.RunAsync(userPrompt, sessionId=null, projectDir)
    ├─ Sink.EmitAsync(Started)
    ├─ ExecuteAsync:
    │   ├─ Porta.Pty spawns cmd.exe in projectDir, env-scrub ANTHROPIC_*/CLAUDE_*
    │   ├─ Reader task → emits StreamChunk per buffer
    │   ├─ Write `claude --session-id <uuid> --permission-mode acceptEdits\r`
    │   ├─ Dwell, then DetectStartupDialog(buf) → maybe emit DialogDismissed
    │   ├─ Write prompt + \r
    │   ├─ session.WaitIdleAsync(IdleThresholdMs, MaxWaitMs) → settle on quiet PTY
    │   └─ Write /exit\r, exit\r, WaitForExit
    └─ Sink.EmitAsync(Completed(SessionId, ExitCode, OutputChars))
    ├─ ProviderJsonlIngestSink on Completed:
    │   └─ copy ~/.claude/projects/<encoded-cwd>/<uuid>.jsonl → session/claude-turn-1.jsonl

 4. Loops.ValidateAndFixAsync(claude, validator, work, projectDir, sink, maxAttempts: 3)
    ├─ for attempt 1..N:
    │   ├─ validator.ValidateAsync(projectDir) → ValidationResult { Ok, Summary, Errors }
    │   └─ if !Ok: claude.RunAsync(fixPrompt, sessionId=<previous>, projectDir)
    └─ returns ValidateAndFixResult { Ok, LastResult, LastValidation }
    [unity-review wraps this in: await using var iso = await IsolationScope.BeginAsync(...)
     which snapshots changed files at Begin and reverts everything else at Dispose]

 5. GitOps.DiffAsync(projectDir) → empty? End("no-changes"); else continue.

 6. Reviews.AskCodexForVerdictAsync(projectDir, sessionDir, userPrompt, ...)
    ├─ build review prompt (project kind + validation label + diff)
    ├─ codex.RunAsync(reviewPrompt, sessionId=null, projectDir) — read-only sandbox
    │   ├─ Process spawns cmd.exe /c codex exec --cd <dir> -o <tmp> --json -
    │   ├─ stdin <<< prompt
    │   ├─ stream stdout lines: emit StreamChunk + scan for thread_id/session_id
    │   └─ read final agent message from -o tmpfile
    ├─ parse verdict (APPROVE | REVISE | unclear)
    ├─ write session/codex-review.jsonl ({verdict, sessionId, text})
    └─ return CodexVerdict { Verdict, Text, SessionId }

 7. if Verdict == "unclear": End("verdict-unclear"); refuse to commit.

 8. if Verdict == "revise":
    ├─ claude.RunAsync(feedbackPrompt, sessionId=<previous>, projectDir)
    └─ validator.ValidateAsync(projectDir) → must still be Ok

 9. GitOps.ChangedFilesAsync(projectDir) → empty? End("no-changes")
10. GitOps.CommitAsync(files, Reviews.BuildCommitMessage(userPrompt, reviewText), coAuthor)
11. if --push: GitOps.PushAsync(branch)
12. Session.End("shipped")  → meta.json gets EndedAt + DurationMs + Result
```

Every async call takes the CancellationToken from the top-level flow. A `Ctrl-C` on the CLI is one `CTS.Cancel()` away from tearing the whole tree down cleanly.

---

## 5. The PTY trick, in detail

`ClaudeAgent.ExecuteAsync` (paraphrased — `PtySession` owns the reader/buffer/drain plumbing, `ClaudeAgent` just writes the script):

```csharp
var pty = await SpawnPtyAsync(BuildPtyOptions(req.ProjectDir), ct);   // virtual hook for tests
await using var session = new PtySession(
    pty,
    onChunk: (chunk, ct) => Sink.EmitAsync(new StreamChunk(...), ct),
    ct);

// Type the claude launch line into the running cmd.exe, then settle.
await session.DwellAsync(500, ct);
await session.WriteLineAsync($"claude {BuildClaudeArgs(...).Join(' ')}", ct);
await session.DwellAsync(Options.InitialDwellMs, ct);

// Startup dialog dismissal — trust folder / bypass warning.
if (DetectStartupDialog(session.Buffer) is "trust") await session.WriteAsync("\r", ct);

// Submit the user prompt, then wait for the TUI to go quiet for IdleThresholdMs
// (capped at MaxWaitMs). This is what used to be IsResponseComplete; it's now
// PtySession's responsibility, not a virtual hook on ClaudeAgent.
await session.WriteAsync(req.Prompt, ct);
await session.WriteAsync("\r", ct);
await session.WaitIdleAsync(Options.IdleThresholdMs, Options.MaxWaitMs, ct);

await session.WriteLineAsync("/exit", ct);
await session.DwellAsync(Options.ExitDwellMs, ct);
await session.WriteLineAsync("exit", ct);
var exitCode = await session.ShutdownAsync(Options.WaitForExitMs, Options.ReaderDrainMs);
```

Two `protected virtual` hooks in v1:

- `DetectStartupDialog(buf) → "trust" | "bypass-warning" | null` — override per-project when Claude reworks the TUI dialog.
- `SpawnPtyAsync(PtyOptions, ct) → IPtyConnection` — defaults to `PtyProvider.SpawnAsync`; tests swap in a `FakePtyConnection` so the drive loop runs against scripted bytes (see `tests/RemoteAgents.Tests/Agents/FakePty.cs`).

Idle/wait/dwell/exit budgets all live on `ClaudeAgentOptions` and are passed verbatim — no subclass needed to tune them. Other mechanics (`BuildClaudeArgs`, the dwell choreography, `ExtractAssistantText`, the JSONL read) stay private to `ClaudeAgent` / `PtySession`.

The Claude session UUID is generated by us, passed via `--session-id <uuid>` on a fresh run or `--resume <uuid>` on a continuation. Because we own the UUID, we always know the path to the matching `~/.claude/projects/<encoded-cwd>/<uuid>.jsonl` for T0 ingestion.

---

## 6. JSON: source-gen only

.NET 10 file-based programs disable reflection-based `System.Text.Json` by default. The library declares one source-gen context per serializable shape, all `internal`, each colocated with the code that owns it:

- `SessionJsonContext` (in `Core/Sessions/SessionJsonContext.cs`) — `meta.json` (pretty, for `SessionMeta`)
- `EventJsonContext` (same file) — `transcript.jsonl` entries (compact, line-atomic, polymorphic over `AgentEvent`)
- `ProjectsJsonContext` (in `Core/Primitives/ProjectRegistry.cs`) — `projects.json` (`Dictionary<string, string>`)
- `GhJsonContext` (in `Core/Primitives/GhOps.cs`) — `gh pr` JSON output (`GhPrInfo`)
- `FlowsJsonContext` (in `Flows/Reviews.cs`) — `codex-review.jsonl` (`CodexReviewArtifact`)

The pattern is one `[JsonSerializable]` partial context per consumer, kept `internal`. If you add a new serializable shape inside the library, define a context next to it — don't grow a single library-wide context.

For one-off JSON inside a flow file, add a `[JsonSerializable]` partial context in the same `.cs` file (see `Flows/Reviews.cs` for the shape) or hand-build the line.

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

The xUnit suite (`tests/RemoteAgents.Tests/`) covers:

- Primitives, end-to-end (`GitOpsTests`, `FsDiffTests`, `ProjectRegistryTests`, `SubscriptionGuardTests`, `RunCommandTests`)
- Agent lifecycle invariants (`AgentLifecycleTests` against fake agents)
- ClaudeAgent / CodexAgent argv builders, sessionId scanner
- Sessions + sinks (`SessionAndSinksTests`, `ProviderJsonlIngestSinkTests`)
- Validators (`OrchestratorValidatorTests` against synthetic trees and the real repo; `UnityBatchValidatorTests` for path discovery)

What is **not** under xUnit:

- `ClaudeAgent.ExecuteAsync` (the PTY mechanics) — validated only by live smokes in `cli/flows/smoke-*.cs`. Driving Porta.Pty in a unit test would mean spawning a real `cmd.exe`, which we already do in the smoke flows.
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
| Unity batch-mode dirties the tree every run | Provider-side reality (TMP_SDF auto-regen). Flows handle it by wrapping the validate/fix loop in `IsolationScope.BeginAsync(projectDir)` — snapshot Claude-touched files at `Begin`, revert everything else at `Dispose` — see `unity-review.cs`. |
| Claude `Completed.ExitCode == -1` | Real abnormal teardown — `pty.WaitForExit(Options.WaitForExitMs)` timed out, Kill path fired. Inspect `claude-turn-N.jsonl` and `claude-raw.txt` to see where it stalled. (Earlier behavior where clean exits also reported `-1` was a reader-drain bug; fixed.) |
| Claude assistant text source | Primary: `~/.claude/projects/<encoded>/<sessionId>.jsonl`, parsed by `ClaudeJsonl.TryReadLastAssistantText`. Fallback: ANSI-stripped PTY buffer. The JSONL path survives TUI wrap/scroll/drain hazards; the buffer path doesn't. |

---

## 12. Where to look next

- [`usage.md`](usage.md) — getting started, the four example flows, writing your own.
- [`../src/RemoteAgents/Core/Agents/Agent.cs`](../src/RemoteAgents/Core/Agents/Agent.cs) — the sealed lifecycle in 60 lines.
- [`../src/RemoteAgents/Providers/Claude/ClaudeAgent.cs`](../src/RemoteAgents/Providers/Claude/ClaudeAgent.cs) — the PTY mechanics.
- [`../src/RemoteAgents/Core/Events/AgentEvent.cs`](../src/RemoteAgents/Core/Events/AgentEvent.cs) — the five event cases.
- [`../cli/flows/full-review.cs`](../cli/flows/full-review.cs) — the most complete example flow; copy as a starting point. [`unity-review.cs`](../cli/flows/unity-review.cs) is the same script with `IsolationScope` wrapped around the validate loop.
- [`../src/RemoteAgents/Flows/`](../src/RemoteAgents/Flows/) — `FlowBootstrap`, `Loops`, `Reviews`, `IsolationScope` — the helpers every review-style flow composes.
- [`../../PLANS/csharp-orchestrator-prd.md`](../../PLANS/csharp-orchestrator-prd.md) — PRD (standalone-buildable from cold).
- [`../../PLANS/csharp-orchestrator-build.md`](../../PLANS/csharp-orchestrator-build.md) — build plan + 19 confirmed decisions.
- [`../../research/`](../../research/) — design notes from the JS-prototype era.

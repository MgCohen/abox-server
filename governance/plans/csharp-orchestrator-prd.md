# C# Orchestrator — Product Requirements

> **Purpose**: defines what the C# orchestrator must do and why, in enough
> detail that a cold reader can build it without first reading the design
> handover or the build plan. Companion documents
> ([`csharp-orchestrator-rewrite.md`](csharp-orchestrator-rewrite.md) for
> architecture rationale,
> [`csharp-orchestrator-build.md`](csharp-orchestrator-build.md) for step
> ordering) exist but are not preconditions for understanding this one.
>
> **Status**: requirements pinned 2026-05-28 against the 19 decisions
> captured in the build plan.

---

## 1. One-paragraph summary

The C# orchestrator is a `net10.0` library + CLI that drives the `claude`
and `codex` agent CLIs against project repositories on **subscription
billing** (Claude Max plan, ChatGPT Plus/Pro) — not per-token API. It
replaces the existing JS orchestrator at `remote-agents/orchestrator/` once
parity is reached, and adds two structural things the JS lib lacks: a
**named agent layer** (Planner, Documenter, Researcher…) so role-specific
configuration is reused across flows, and **base classes designed for
extension** — for per-project customization (subclass providers, add
validators, register new agents) and for a future UI app (events flow
through `IEventSink`; cancellation wired in everywhere). The deliverable
is a library + flow runner; the deliverable is **not** a UI or a server.

---

## 2. Background: how the system works

A cold reader needs this section to understand why the architecture is the
way it is.

### 2.1 The subscription-billing trick

Both `claude` and `codex` can run against either subscription billing or
API billing.

- **Claude** decides at startup by checking `isatty(stdin) && isatty(stdout)`.
  If both are TTYs, it talks to `claude.ai` against the Max plan quota
  (free within the plan). If either is a pipe, it talks to the Anthropic
  API (billed per token, against the Agent SDK Credit pool on Max).
- **Codex** has no such heuristic — `codex exec` was officially announced
  as supported on ChatGPT subscriptions in April 2026. Plain process
  spawning is fine for subscription billing as long as `OPENAI_API_KEY`
  isn't set in the env (which would force API mode).

The implication for the orchestrator:

- `ClaudeAgent` MUST drive `claude` through a **pseudo-terminal (PTY)** so
  the spawned process sees TTYs on both sides. On Windows that means
  **ConPTY** (the Windows pseudo-console API, shipped in Windows 10 1809+).
  In .NET, the ConPTY API is wrapped by **`Porta.Pty 1.0.7`**, a managed
  P/Invoke library — that's the pinned dependency.
- `CodexAgent` uses plain `System.Diagnostics.Process` (no PTY needed).

Empirical evidence that this works in .NET: the smoke test at
`C:\Unity\dotnet-pty-smoke\` (outside this repo) drives Claude's TUI
end-to-end via Porta.Pty + ConPTY, the PONG prompt round-trips, the
session writes a `~/.claude/projects/.../<uuid>.jsonl` with
`apiProvider:"firstParty"` and `subscriptionType:"max"` (i.e. subscription
billing path), and `claude auth status` post-test confirms no API
contamination.

### 2.2 Verifying subscription billing post-run

For any compliance test, the **subscription path is "intact" if**:

- Claude session JSONL at `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`
  contains `"apiProvider":"firstParty"` and `"subscriptionType":"max"`.
- `claude auth status --json` (or its current equivalent) returns
  `"source":"subscription"` / plan info.
- Codex `token_count` events show `rate_limits.primary.plan_type` of the
  ChatGPT plan, not API.

The library doesn't enforce these checks at runtime, but the test suite
in `ABox.Tests/` includes one integration test that verifies them
against a real run.

### 2.3 Session continuity

Both CLIs support resuming a prior conversation by ID:

- **Claude**: `--session-id <uuid>` on first call (orchestrator generates
  the UUID), `--resume <uuid>` on subsequent calls. We never scrape the
  session ID out of the TUI — we set it.
- **Codex**: `codex exec resume <thread-id>` reuses a prior thread. The
  thread ID arrives in the `--json` stream's first `session_meta` event;
  orchestrator captures it and passes it back on the next call.

### 2.4 Provider-side observability (free)

Both CLIs already write per-session JSONL transcripts to disk without any
orchestrator effort:

- **Claude**: `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`
  containing every user prompt, tool call (with full args), tool result
  (with full content), edit diff (as structured patches), assistant text,
  per-turn token counts, model + service tier, git branch, permission
  mode. `<encoded-cwd>` = the absolute path with `\`, `/`, and `:`
  replaced by `-`.
- **Codex**: `~/.codex/sessions/YYYY/MM/DD/rollout-<iso>-<sessionId>.jsonl`
  containing the same plus full `base_instructions` (system prompt),
  sandbox/approval mode, rate-limit percentage (`rate_limits.primary.used_percent`),
  and time-to-first-token. Indexed in `~/.codex/session_index.jsonl`.

Both encrypt the chain-of-thought / reasoning text server-side. Everything
else (tool calls, args, results, final text, tokens) is plaintext.

The orchestrator's `ProviderJsonlIngestSink` copies these into each
session dir post-run so users get the full audit trail without any work.

### 2.5 Claude TUI quirks the orchestrator handles

`ClaudeAgent` has to deal with three things in Claude's TUI that the
provider implementation makes invisible to flow authors:

1. **Startup dialogs** — "Trust this folder?" (press Enter to accept) and
   the "Bypass Permissions mode" warning (which defaults to *No, exit* —
   you have to send `2\r` to bypass). The current substrings to match in
   the stripped buffer are `'trust this folder'` and
   `'Is this a project you'`.
2. **No "I am finished" signal** — the TUI doesn't emit a clean end
   marker. The provider watches for an **idle threshold** of no new
   bytes from the PTY (default 6s) after submitting the prompt, then
   sends `/exit\r`.
3. **Trust-dialog wording shifts between Claude versions.** The smoke
   test at `C:\Unity\dotnet-pty-smoke\` found that v2.1.x changed the
   wording from what an earlier draft expected. `DetectStartupDialog` is
   `virtual` so a future subclass can override without touching the
   library.

---

## 3. Users and use cases

**Primary user**: a single solo developer (the repo owner) running flows
from his Windows laptop today, against multiple Unity projects under
`C:\Unity\*`. Later, the same flows run from a Hetzner Linux VM (Phase B
of [unity-agent-infrastructure.md](unity-agent-infrastructure.md)).

**Secondary user (future)**: same developer interacting through a UI app
(MAUI Blazor Hybrid: Windows + Android + web).

**Use cases**, in rough order of frequency:

| # | Use case | Today | With C# orchestrator |
|---|---|---|---|
| 1 | Run a flow against a Unity project | `agents run full-review card-framework "..."` (JS) | `agents-dotnet run full-review card-framework "..."` |
| 2 | Write a new flow that composes existing agents | hand-write `flows/<name>.mjs` | hand-write `flows/<name>.cs` with `#:project` directive |
| 3 | Register a new named agent | doesn't exist — re-specify config at each callsite | `agents/<Name>.cs` static factory; reuse anywhere |
| 4 | Add a per-project validator | `validation/<project>.mjs` exporting `validate(...)` | `validation/<Project>Validator.cs` implementing `IValidator` |
| 5 | Debug a run | grep `sessions/<id>/transcript.jsonl` + provider JSONLs | same, with cleaner schema |
| 6 | Customize a provider for one project | not supported | subclass `ClaudeAgent`, override `DetectStartupDialog` |
| 7 | (future) Drive a flow from a UI button + live progress | not supported | `ChannelSink` attached; events stream to UI |
| 8 | (future) Cancel a stuck run from a UI button | not supported | `CancellationTokenSource.Cancel()` propagates to PTY child |

**The three flows that must exist for v1** (parity with JS):

| Flow | Behavior |
|---|---|
| `claude-only` | Snapshot project files → run Claude with the user's prompt → snapshot after → write diff + transcript. No commit. Used for "just have Claude do a thing." |
| `claude-validate` | `claude-only` + a fix loop: after Claude's run, invoke a per-project `IValidator`. If it fails, re-prompt Claude with the validation errors (resuming the same session). Repeat up to 3 attempts. |
| `full-review` | `claude-validate` + a Codex review step: after validation passes, run Codex with the diff + the user's prompt and ask for `approve` or `revise` verdict. If `revise`, feed feedback back into Claude (same session). On approval, commit the diff and optionally push. |

---

## 4. Goals

| # | Goal | Measured by |
|---|---|---|
| G1 | **Parity** with JS orchestrator on the three flows | All three produce equivalent session-dir contents and land commits indistinguishable in intent from JS-run commits |
| G2 | **Named agent layer** so role config is reused | At least three named agents (Planner / Documenter / Researcher) defined as static factories; each used by ≥1 flow without re-specifying model/system-prompt |
| G3 | **Extensible base classes** for per-project tooling | Subclassing `ClaudeAgent` to override a `virtual` hook works without touching the library; new validators slot in via `IValidator`; new flows compose primitives without new types |
| G4 | **Subscription billing preserved end-to-end** | Per §2.2, every run leaves the subscription path intact |
| G5 | **Sealed lifecycle + exhaustive events** | `Agent.RunAsync` is `sealed`; subclasses cannot bypass `Started`/`Completed`/`Failed` emission; switch expressions on `AgentEvent` warn at compile-time on missing cases |
| G6 | **UI-ready (without building UI)** | UI can attach by registering one extra `IEventSink`; cancellation reaches the PTY child via `CancellationToken` in every async signature; no library API change needed to add UI |
| G7 | **Observable runs by default** | Every flow produces `sessions/<id>/` with structured transcript + ingested Claude/Codex provider JSONLs without flow-author effort |
| G8 | **No regressions during coexistence** | JS and C# orchestrators run against the same repo on the same day without corrupting each other's state |
| G9 | **Card Framework shakedown success** | One end-to-end `full-review` lands a real commit on a Card Framework branch with Codex verdict = `approve` |

---

## 5. Non-goals (explicit)

| # | Non-goal | Why |
|---|---|---|
| NG1 | Build a UI in this scope | UI is downstream; library must be UI-ready, not UI-bundled |
| NG2 | Tool-definition framework (Anthropic-style structured tools) | `RunCommand` is enough |
| NG3 | Memory primitive | CLI-native `CLAUDE.md` / `AGENTS.md` cover per-run context |
| NG4 | Agent-calls-agent composition | Composition is the flow's job |
| NG5 | Provider fallback to API (LiteLLM HTTP path) | Future option; not v1 |
| NG6 | Multi-agent orchestration runtime (Mastra/Agno style server) | Stays library + CLI |
| NG7 | Cross-platform v1 (Linux/macOS) | Windows-only v1; Linux port at Hetzner-VM time |
| NG8 | Backwards-compat with JS session folders | C# `meta.json` is a clean break |
| NG9 | Live tool-call / token-usage events | Those facts live exclusively in ingested JSONLs post-run |
| NG10 | Visual session-replay viewer | Build after enough sessions accumulate to inform the design |

---

## 6. Functional requirements (with data shapes)

### 6.1 Core records

These are the public types a builder needs to define. Field types are
inferable from the JS lib but pinned here for clarity.

```csharp
public sealed record AgentRunRequest(
    string Prompt,
    string? SessionId,        // null = fresh; non-null = resume
    string ProjectDir);       // absolute path to project root

public sealed record AgentResult(
    string Text,              // best-effort agent reply
    string SessionId,         // ID caller can pass back to resume
    int ExitCode,             // 0 = clean
    string RawOutput);        // full captured PTY stream / stdout

public sealed record ClaudeAgentOptions(
    int InitialDwellMs = 2000,     // wait before considering output
    int IdleThresholdMs = 6000,    // idle-bytes → "response done"
    int ExitDwellMs = 1500,        // wait after sending /exit
    string PermissionMode = "acceptEdits"); // claude --permission-mode

public sealed record CodexAgentOptions(
    string Sandbox = "workspace-write",
    int JsonStreamTimeoutMs = 60_000);

public sealed record ValidationResult(
    bool Ok,
    string Summary,
    string Errors);
```

### 6.2 Agent abstract base

```csharp
public abstract class Agent
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public IReadOnlyDictionary<string, object>? Options { get; init; }

    public sealed Task<AgentResult> RunAsync(
        AgentRunRequest req,
        IEventSink sink,
        CancellationToken ct = default);

    protected abstract Task<AgentResult> ExecuteAsync(
        AgentRunRequest req,
        IEventSink sink,
        CancellationToken ct);
}
```

**Lifecycle contract** (`sealed` so it can't be bypassed):
1. Emit `AgentEvent.Started`
2. Call `ExecuteAsync`
3. On success: emit `AgentEvent.Completed`, return result
4. On exception: emit `AgentEvent.Failed`, **rethrow**

**Hooks v1** — `ClaudeAgent` and `CodexAgent` are non-sealed; both expose
exactly these `virtual protected` hooks:

```csharp
protected virtual string? DetectStartupDialog(string strippedBuffer);
// returns "trust" | "bypass" | null. Subclass to handle new wordings.

protected virtual bool IsResponseComplete(string buffer, DateTimeOffset lastChunkAt);
// default: (DateTimeOffset.UtcNow - lastChunkAt).TotalMilliseconds >= IdleThresholdMs.
// Subclass to use a different signal.
```

All other methods stay `private`. Lifting more to `virtual` later is
non-breaking; demoting them back is breaking — be conservative.

### 6.3 Events

`AgentEvent` is an `abstract record` with exactly five sealed cases in
v1:

```csharp
public abstract record AgentEvent(string AgentName)
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    public sealed record Started(string AgentName, string Prompt, string? SessionId)
        : AgentEvent(AgentName);

    public sealed record StreamChunk(string AgentName, string Text)
        : AgentEvent(AgentName);

    public sealed record DialogDismissed(string AgentName, string DialogKind)
        : AgentEvent(AgentName);

    public sealed record Completed(string AgentName, AgentResult Result, TimeSpan Duration)
        : AgentEvent(AgentName);

    public sealed record Failed(string AgentName, string ErrorMessage, TimeSpan Duration)
        : AgentEvent(AgentName);
}
```

A switch on `AgentEvent` is exhaustive against these five. Adding a new
sealed case is a breaking change. Tool calls, token usage, and rate
limits are **not** events — they live in the ingested provider JSONLs.

### 6.4 Sinks

```csharp
public interface IEventSink
{
    Task EmitAsync(AgentEvent evt, CancellationToken ct = default);
}
```

Built-in sinks (all live in `ABox/Events/`):

| Sink | Purpose |
|---|---|
| `JsonlSink(string path)` | Append-only writer to `transcript.jsonl` |
| `ConsoleSink` | Pretty-print to `Console.Out` |
| `CompositeSink(params IEventSink[])` | Fan out one event to many sinks |
| `ProviderJsonlIngestSink(Session session)` | Post-run hook: copy `~/.claude/projects/.../<id>.jsonl` and the Codex rollout JSONL into the session dir |

### 6.5 Sessions

Every flow run produces `sessions/<iso>-<flow-slug>/` (e.g.
`2026-05-28T14-35-09-620Z-full-review/`).

**Files in the session dir** at end of run:

| File | Required | Written by |
|---|---|---|
| `meta.json` | yes | `Session.End()` |
| `prompt.txt` | yes | `Session.Start()` |
| `transcript.jsonl` | yes | `JsonlSink` over the run |
| `claude-turn-N.jsonl` | iff ≥1 Claude call | `ProviderJsonlIngestSink` |
| `codex-turn-N.jsonl` | iff ≥1 Codex call | `ProviderJsonlIngestSink` |
| `claude-raw.txt` | optional | flow may dump `AgentResult.RawOutput` for debugging |
| `codex-review.txt` | optional | flow may dump the extracted Codex verdict |

**`meta.json` schema** (clean break from JS):

```json
{
  "id": "2026-05-28T14-35-09-620Z-full-review",
  "flowName": "full-review",
  "projectName": "card-framework",
  "projectDir": "C:\\Unity\\Card-Framework",
  "userPrompt": "Add XML docs to public API on CardSpawner",
  "startedAt": "2026-05-28T14:35:09.620Z",
  "endedAt":   "2026-05-28T14:38:42.918Z",
  "durationMs": 213298,
  "result": "shipped",
  "claudeSessionId": "0b1f90ea-fa90-4554-86e5-606bdd2518e7",
  "codexSessionId":  "019e6f03-2657-79c1-bf66-2f2779446a30",
  "filesChanged": 1,
  "pushed": false
}
```

**`result` values** (free string, but these are the conventional ones):
`"shipped"`, `"aborted-dirty-tree"`, `"validation-failed"`,
`"review-rejected"`, `"no-changes"`, `"failed"`.

**`transcript.jsonl` line shape** — one JSON object per line. The library
emits one line per `AgentEvent`:

```jsonl
{"t":"2026-05-28T14:35:09.621Z","kind":"AgentEvent.Started","agentName":"planner","prompt":"Add XML docs...","sessionId":null}
{"t":"2026-05-28T14:36:42.108Z","kind":"AgentEvent.Completed","agentName":"planner","duration":92487,"result":{"sessionId":"0b1f90ea-...","exitCode":0}}
```

Flow authors may write additional lines for flow-level events (e.g.
`{"t":"...","kind":"flow.validate","attempt":1,"ok":true}`). These have
no library schema — they're free-form, `kind`-prefixed.

### 6.6 Primitives

**`GitOps`** (static class, `ABox/Primitives/GitOps.cs`):

```csharp
public static class GitOps
{
    public record DiffResult(string Patch, IReadOnlyList<string> Files);

    public static Task<bool> IsDirtyAsync(string projectDir, CancellationToken ct = default);
    public static Task<string> CurrentBranchAsync(string projectDir, CancellationToken ct = default);
    public static Task<DiffResult> DiffAsync(string projectDir, CancellationToken ct = default);
    public static Task AddAsync(string projectDir, IEnumerable<string> files, CancellationToken ct = default);
    // ↑ throws if files contains "-A" or "--all".
    public static Task CommitAsync(string projectDir, string message, CancellationToken ct = default);
    public static Task PushAsync(string projectDir, string branch, bool force = false, CancellationToken ct = default);
    // ↑ throws if force == true and branch is "main" or "master".
}
```

**`RunCommand`**:

```csharp
public static class RunCommand
{
    public record Result(int ExitCode, string Stdout, string Stderr, long DurationMs);

    public static Task<Result> ExecuteAsync(
        string fileName,
        IEnumerable<string> args,
        string? cwd = null,
        IReadOnlyDictionary<string, string?>? envOverrides = null,
        CancellationToken ct = default);
    // stdout/stderr captured to strings, not streamed. Use a separate API if
    // streaming is ever needed (not in v1).
}
```

**`FsDiff`**:

```csharp
public static class FsDiff
{
    public static readonly IReadOnlyList<string> SkipDirs =
        [".git", "node_modules", "bin", "obj",
         "Library", "Temp", "Logs",       // Unity
         "sessions"];

    public record FileSnapshot(string RelativePath, long Size, DateTimeOffset Mtime);
    public record DiffEntry(string RelativePath, ChangeKind Kind);
    public enum ChangeKind { Added, Removed, Modified }

    public static IReadOnlyList<FileSnapshot> Snapshot(string projectDir);
    public static IReadOnlyList<DiffEntry> Diff(
        IReadOnlyList<FileSnapshot> before,
        IReadOnlyList<FileSnapshot> after);
    // Compares by (Size, Mtime). Not content-aware.
}
```

**`ProjectRegistry`**:

```csharp
public static class ProjectRegistry
{
    public static string Resolve(string shortName);
    // Reads <repo>/projects.json. Returns absolute path. Throws on miss.
}
```

`<repo>/projects.json` schema:

```json
{
  "card-framework": "C:\\Unity\\Card-Framework",
  "scaffold": "C:\\Unity\\Scaffold",
  "gear-engine": "C:\\Unity\\Gear-Engine",
  "abox.server": "C:\\Unity\\abox.server"
}
```

**`SubscriptionGuard`**:

```csharp
public static class SubscriptionGuard
{
    public static void ThrowIfApiKeysSet();
    // Throws if ANTHROPIC_API_KEY, CLAUDE_API_KEY, or OPENAI_API_KEY is set.

    public static Task EnsureBinariesOnPathAsync(CancellationToken ct = default);
    // Runs `claude --version` and `codex --version`. Throws with a clear
    // message if either is missing.
}
```

Flows call both at startup before doing anything else.

### 6.7 Validators

```csharp
public interface IValidator
{
    Task<ValidationResult> ValidateAsync(
        string projectDir,
        AgentResult? lastAgentResult,
        CancellationToken ct = default);
}
```

Validators are per-project. Library ships `OrchestratorValidator`
(Roslyn syntax-only parse over `*.cs`). Real projects implement their
own (`CardFrameworkValidator` runs Unity batch-mode compile, etc.).

### 6.8 Flows and CLI

- Flows are `.NET 10` file-based programs (`.cs` files with `#:project
  ../ABox/ABox.csproj` directive). Run via `dotnet run
  flows/<name>.cs -- ...args`.
- `agents-dotnet` is itself a file-based program at
  `bin/agents-dotnet.cs`. Subcommands: `list` (lists `flows/*.cs`), `run
  <flow> [...args]` (spawns `dotnet run flows/<flow>.cs -- ...args` with
  inherited stdio).
- Flow args are parsed inline as `args[]`. `System.CommandLine` is not
  used in v1 (deferred unless complexity demands it).

### 6.9 Cancellation

- Every public async method takes `CancellationToken ct = default`.
- `Agent.RunAsync` cancellation MUST terminate the spawned child within
  5 seconds (kill PTY child or `Process.Kill(entireProcessTree: true)`).
- `IEventSink.EmitAsync` accepts `ct` but built-in sinks are not allowed
  to swallow `OperationCanceledException` — they propagate.

### 6.10 Provider-specific behavior

**`ClaudeAgent.ExecuteAsync` algorithm**:

1. Verify env: unset `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY` in child env.
2. Build argv:
   - `--session-id <new-uuid>` if `req.SessionId == null`, else
     `--resume <req.SessionId>`.
   - `--permission-mode <Options["permissionMode"] ?? "acceptEdits">`.
   - `--append-system-prompt <SystemPrompt>` if `SystemPrompt != null`.
   - `--model <Model>`.
   - The user prompt as the trailing positional arg.
3. Spawn via Porta.Pty: `cmd.exe /c claude <args...>`, set cwd = `req.ProjectDir`.
4. Read PTY stream into a buffer; on each chunk:
   - Strip ANSI escapes.
   - If `DetectStartupDialog(stripped)` returns non-null and not yet
     dismissed: send `\r` (trust) or `2\r` (bypass), emit
     `DialogDismissed`.
   - Track `lastChunkAt`.
5. After `InitialDwellMs` has passed AND `IsResponseComplete(...)`
   returns true: send `/exit\r`, wait `ExitDwellMs`, terminate.
6. Wrap `Porta.Pty.IPtyConnection.ExitCode` access in `ExitCodeOrNull`
   (the underlying API throws if `HasExited` is false).
7. Best-effort extract reply text from the buffer; return `AgentResult`.

**`CodexAgent.ExecuteAsync` algorithm**:

1. Verify env: unset `OPENAI_API_KEY` in child env.
2. If `SystemPrompt != null`: prepend `"{SystemPrompt}\n\n---\n\n"` to
   `req.Prompt` (Codex has no `--system-prompt` flag).
3. Write `req.Prompt` to a tmp file? **No** — pipe via stdin.
4. Build argv: `cmd.exe /c codex exec [resume <req.SessionId>] --cd
   <req.ProjectDir> -o <tmpfile-for-output> --sandbox <Options["sandbox"]
   ?? "workspace-write"> --dangerously-bypass-approvals-and-sandbox
   --json --model <Model> -`.
5. Spawn via `System.Diagnostics.Process` with redirected stdin/stdout.
6. Pipe `req.Prompt` to stdin; close stdin.
7. Read stdout line by line as JSONL; on each line, look for
   `thread_id` / `session_id` / `sessionId` and capture it as the new
   session ID.
8. On exit: read `<tmpfile-for-output>` as the final agent message.
9. Return `AgentResult { Text = file contents, SessionId, ExitCode,
   RawOutput = full stdout }`.

The `--dangerously-bypass-approvals-and-sandbox` flag is set because we
use `--sandbox` to control file access ourselves — the bypass tells Codex
not to prompt for approval since we've already constrained the sandbox.
If Codex semantics change, revisit this.

### 6.11 Named agents (the user-authored layer)

`agents/Planner.cs` (example template — all named agents follow this
shape):

```csharp
public static class Planner
{
    public static Agent Create() => new ClaudeAgent
    {
        Name = "planner",
        Model = "claude-opus-4-7",
        SystemPrompt = File.ReadAllText("agents/prompts/planner.md"),
        Timing = new ClaudeAgentOptions { IdleThresholdMs = 8000 },
        Options = new Dictionary<string, object>
        {
            ["permissionMode"] = "acceptEdits",
        },
    };
}
```

Sidecar prompts (`agents/prompts/<name>.md`) are loaded eagerly at
factory call. No hot-reload.

Three named agents ship in v1 for the example flows: `Planner` (Claude
Opus 4.7), `Documenter` (Claude Haiku 4.5), `Researcher` (Codex 5.3).

---

## 7. Non-functional requirements

| # | Requirement | Target | Verified by |
|---|---|---|---|
| NFR1 | `ClaudeAgent.RunAsync` median end-to-end latency for a short task | ≤ 25s on dev laptop (`Reply with PONG` style); the JS baseline at commit `14b5cc8` ran ~18–22s — we accept a small overhead during the rewrite | Manual timed run, recorded in `ABox.Tests` |
| NFR2 | Flow startup overhead (`dotnet run flows/foo.cs` → first agent call) | < 3s once SDK is warm | Manual timing |
| NFR3 | Subscription path verified per run | Section 2.2 checks pass | Integration test in `ABox.Tests` |
| NFR4 | Memory footprint of library + running flow | < 200 MB resident (excluding child CLIs) | `dotnet-counters` snapshot during a flow |
| NFR5 | Test coverage | Every primitive + the `Agent` lifecycle has a focused xUnit test asserting the contract | `dotnet test` green |
| NFR6 | Build cleanliness | `dotnet build` warning-free; `<Nullable>enable</Nullable>` on every project | CI / local build |
| NFR7 | Cancellation latency | `cts.Cancel()` → child process exits within 5s | xUnit integration test with a stuck `cmd.exe /c pause` |

---

## 8. User scenarios (concrete walks)

### 8.1 Doc commit on Card Framework

```
> agents-dotnet run full-review card-framework "Add XML docs to public API on CardSpawner"
```

Flow:
1. `SubscriptionGuard.ThrowIfApiKeysSet()` + `EnsureBinariesOnPathAsync()`.
2. `ProjectRegistry.Resolve("card-framework")` → `C:\Unity\Card-Framework`.
3. `Session.Start("full-review", "card-framework", projectDir, userPrompt)`.
4. Check `GitOps.IsDirtyAsync` → abort if dirty.
5. `FsDiff.Snapshot` → before-snapshot.
6. `var planner = Planner.Create();`
7. `var result = await planner.RunAsync(new(userPrompt, null, projectDir), sink, ct);`
8. `for attempt = 1..3:` validate; if `!ok`, re-run planner with the fix
   prompt and `result.SessionId`.
9. `var diff = await GitOps.DiffAsync(projectDir, ct);`
10. `var reviewer = Reviewer.Create();`
11. `var review = await reviewer.RunAsync(new(BuildReviewPrompt(userPrompt, diff.Patch), null, projectDir), sink, ct);`
12. Parse `review.Text` for `approve` / `revise`. On `revise`, feed back
    into planner and re-validate.
13. `GitOps.AddAsync(projectDir, diff.Files, ct);`
    `GitOps.CommitAsync(projectDir, BuildCommitMessage(userPrompt, review.Text), ct);`
14. If `--push`: `GitOps.PushAsync(projectDir, branch, ct)`.
15. `Session.End("shipped")`.

Session dir at end (`sessions/2026-05-28T14-35-09-620Z-full-review/`):

```
meta.json          ← result: "shipped"
prompt.txt         ← "Add XML docs..."
transcript.jsonl   ← Started/Completed for planner (×1 or ×2 if fix-loop ran), Started/Completed for reviewer
claude-turn-1.jsonl ← every tool call, edit, token count for the planner's first turn
claude-turn-2.jsonl ← (if fix-loop ran) second planner turn
codex-review.jsonl ← reasoning + verdict + rate-limit %
```

### 8.2 Register a new agent for refactoring

`agents/Refactorer.cs`:

```csharp
public static class Refactorer
{
    public static Agent Create() => new ClaudeAgent
    {
        Name = "refactorer",
        Model = "claude-opus-4-7",
        SystemPrompt = File.ReadAllText("agents/prompts/refactorer.md"),
        Timing = new ClaudeAgentOptions { IdleThresholdMs = 12_000 },
        Options = new Dictionary<string, object> { ["permissionMode"] = "acceptEdits" },
    };
}
```

Used anywhere without re-specifying config. Done.

### 8.3 Card Framework needs a custom startup-dialog handler

`agents/CardFrameworkClaude.cs`:

```csharp
public sealed class CardFrameworkClaudeAgent : ClaudeAgent
{
    protected override string? DetectStartupDialog(string strippedBuffer)
    {
        if (strippedBuffer.Contains("Custom CF dialog text")) return "cf-dialog";
        return base.DetectStartupDialog(strippedBuffer);
    }
}
```

Lifecycle stays in `Agent.RunAsync`; subclass only changes the one thing
it needs to. Compile-time guaranteed to still emit `Started` /
`Completed` / `Failed` correctly.

### 8.4 (future) UI run with Stop button

UI registers a `ChannelSink` (writes events to a `Channel<AgentEvent>`)
and creates a `CancellationTokenSource`. UI component does:

```csharp
var sink = new CompositeSink(new JsonlSink(transcriptPath), channelSink);
var task = agent.RunAsync(req, sink, cts.Token);

await foreach (var evt in channel.Reader.ReadAllAsync(cts.Token))
    UpdateUi(evt);

await task;
```

Stop button → `cts.Cancel()` → library terminates PTY child within 5s
(NFR7). No library API change required.

---

## 9. Acceptance criteria (the bar for "v1 done")

A flow author and the library maintainer can independently verify these.

### 9.1 Per-flow acceptance gate

For each of `claude-only`, `claude-validate`, `full-review`:

1. `dotnet run flows/<name>.cs <project> "<prompt>"` exits with code 0.
2. Session dir contains `meta.json`, `prompt.txt`, `transcript.jsonl`,
   and at least one `claude-turn-N.jsonl`.
3. For `full-review`: session dir also contains `codex-review.jsonl`,
   AND the final `agent_message` in that file parses as a verdict of
   `approve`.
4. `meta.json.result` is `"shipped"` (for `full-review`) or matches the
   flow's expected terminal value.
5. For `full-review` with `--push`: the throwaway branch on the remote
   advances by exactly one commit; the commit message contains the
   `Co-Authored-By` trailer.

### 9.2 Subscription billing acceptance

After at least one end-to-end run, every check in §2.2 passes:

- Claude session JSONL has `"apiProvider":"firstParty"` and
  `"subscriptionType":"max"`.
- `claude auth status` shows subscription path.
- Codex `token_count` shows ChatGPT plan in `rate_limits.primary.plan_type`.

### 9.3 Library acceptance

- `dotnet build` warning-free with `<Nullable>enable</Nullable>` on
  every project.
- `dotnet test` green with at least:
  - `AgentLifecycleTests` covering all 5 event emissions and rethrow
    on exception via a `FakeAgent`.
  - `GitOpsTests` covering `-A` refusal and `--force` to `main`
    refusal.
  - `JsonlSinkTests` covering append-only and ordering.
  - One integration test that runs `ClaudeAgent` against `Reply with
    PONG` and asserts `Text.Contains("PONG")`.
- Cancellation test: a flow that calls `agent.RunAsync` then cancels
  the CT 1s later sees the child process gone within 5s.

### 9.4 Card Framework shakedown

One real `full-review` run against `card-framework` produces:
- A commit landed on a throwaway branch.
- Codex verdict in `codex-review.jsonl` = `approve`.
- Session dir contains all required artifacts (§9.1).
- Subscription billing intact (§9.2).

When this passes, the JS orchestrator is retirable.

---

## 10. Constraints and assumptions

**Constraints:**
- Windows-only v1. Hetzner-time Linux port deferred.
- `.NET 10` SDK required on every host that runs flows.
- `claude` and `codex` CLIs must be on PATH (`SubscriptionGuard`
  verifies).
- `Porta.Pty 1.0.7` is the pinned PTY backend.

**Assumptions:**
- Claude `--session-id <uuid>` continues to be honored (smoke
  confirmed).
- TTY-on-both-sides subscription heuristic unchanged through Claude
  v2.x.
- Codex `--json` event schema remains parseable.
- The user is the sole consumer until UI lands; no multi-user concerns
  in v1.

---

## 11. Out-of-scope clarifications (revisit if they become real)

| Topic | Current decision | Trigger to revisit |
|---|---|---|
| Flow arg parsing style | `args[]` direct | A flow grows ≥4 distinct flags |
| Per-agent default permission mode | Set per-factory | If 3 agents end up sharing the same override |
| Sidecar prompt loading | Eager (factory time) | If hot-reload becomes useful |
| Codex `--dangerously-bypass-approvals-and-sandbox` | Set; comment explains why | If Codex changes flag semantics |
| LiteLLM HTTP fallback | Not built | If PTY trick ever breaks |
| Live tool-call events | Not emitted | If a UI feature genuinely needs them mid-run (unlikely) |

---

## 12. Pointers

| Topic | Location |
|---|---|
| Build plan (how + when) | [`csharp-orchestrator-build.md`](csharp-orchestrator-build.md) |
| Design handover (architecture decisions) | [`csharp-orchestrator-rewrite.md`](csharp-orchestrator-rewrite.md) |
| JS orchestrator architecture | `remote-agents/orchestrator/docs/architecture.md` |
| JS orchestrator usage | `remote-agents/orchestrator/docs/usage.md` |
| Logging plan (T0 ingest) | [`research/logging-and-telemetry.md`](../design/research/logging-and-telemetry.md) |
| C# validation result + smoke logs | [`research/csharp-rewrite-validation.md`](../design/research/csharp-rewrite-validation.md) + `C:\Unity\dotnet-pty-smoke\` |
| Larger infrastructure plan | [`unity-agent-infrastructure.md`](unity-agent-infrastructure.md) |
| JS claude provider (port reference for `ClaudeAgent.ExecuteAsync`) | `remote-agents/orchestrator/src/providers/claudeProvider.js` |
| JS codex provider (port reference for `CodexAgent.ExecuteAsync`) | `remote-agents/orchestrator/src/providers/codexProvider.js` |

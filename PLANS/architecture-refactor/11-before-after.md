---
type: design-sketch
status: draft
tags: [#architecture, #refactor, #before-after, #agents, #hooks, #primitives, #host]
date: 2026-05-30
branch: claude/orchestrator-refactor-audit-gLDB9
companion-to: 10-core-layer-audit.md
---

# Before / After — key types & services

> Illustrative target shapes for the findings in
> [`10-core-layer-audit.md`](10-core-layer-audit.md). These are **sketches to
> align on direction**, not final code — names and signatures will settle
> during implementation. Every change is behavior-preserving (R6): same
> session outputs, same flow results. `// …` elides unchanged code.
>
> Ordered to match the audit's suggested sequencing.

---

## 1. `AgentOptions` — fold the shared trio (Finding 7)

**Before** — two records repeat `Model` / `SystemPrompt` / `Hooks`; the base
can't see them, so providers must.

```csharp
public sealed record ClaudeAgentOptions(
    int LaunchSettleIdleMs = 1000, /* …PTY knobs… */
    string PermissionMode = "acceptEdits",
    string? Model = null,
    string? SystemPrompt = null,
    HookIntegrationOptions? Hooks = null);

public sealed record CodexAgentOptions(
    string Sandbox = "workspace-write",
    int JsonStreamTimeoutMs = 60_000,
    string? Model = "gpt-5.5",
    string? SystemPrompt = null,
    HookIntegrationOptions? Hooks = null);
```

**After** — one base owns the trio; providers add only what's theirs.

```csharp
public abstract record AgentOptions(
    string? Model = null,
    string? SystemPrompt = null,
    HookIntegrationOptions? Hooks = null);

public sealed record ClaudeAgentOptions(
    int LaunchSettleIdleMs = 1000, /* …PTY knobs… */
    string PermissionMode = "acceptEdits",
    string? Model = null,
    string? SystemPrompt = null,
    HookIntegrationOptions? Hooks = null)
    : AgentOptions(Model, SystemPrompt, Hooks);

public sealed record CodexAgentOptions(
    string Sandbox = "workspace-write",
    int JsonStreamTimeoutMs = 60_000,
    string? Model = "gpt-5.5",
    string? SystemPrompt = null,
    HookIntegrationOptions? Hooks = null)
    : AgentOptions(Model, SystemPrompt, Hooks);
```

`Agent` exposes `protected abstract AgentOptions BaseOptions => Options;`,
which is what unblocks §2 + §3.

---

## 2. `Agent` base — own compose + env-scrub + one hook seam (Findings 1, 3)

**Before** — base owns the hook *lifecycle* and resolution, but `Compose`,
env-scrub, and three separate hook seams (`HookConfig` / `HookParser` /
`InstallHookScopeAsync`) leak the hook concern across the base + both
providers.

```csharp
public abstract class Agent
{
    protected virtual HookIntegrationOptions? HookConfig => null;
    protected virtual IAgentHookParser? HookParser => null;
    protected virtual Task<IAsyncDisposable> InstallHookScopeAsync(
        AgentRunRequest req, string shimPath, CancellationToken ct) => throw …;

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        await Sink.EmitAsync(new AgentEvent.Started(…), ct);
        var cfg = HookConfig;
        IAsyncDisposable? hookScope = cfg is null ? null
            : await InstallHookScopeAsync(req, cfg.ShimPath, ct);
        DriveResult raw;
        try { raw = await DriveAsync(req, ct); }       // provider also composes prompt + scrubs env
        catch (Exception ex) { await Sink.EmitAsync(new AgentEvent.Failed(…)); throw; }
        finally { if (hookScope is not null) await hookScope.DisposeAsync(); }

        var outcome = cfg is null ? HookResolution.Completed
            : HookResolution.FromHooksJsonl(cfg.HooksJsonlPath, HookParser!, req.Mode);
        if (outcome.Question is null && raw.DetectedQuestion is not null)
            outcome = HookResolution.ForQuestion(raw.DetectedQuestion, req.Mode);
        if (outcome.Status == AgentStatus.Failed && outcome.Question is not null)
            await Sink.EmitAsync(new AgentEvent.NonInteractiveViolation(…));
        // …assemble AgentResult…
    }

    protected abstract Task<DriveResult> DriveAsync(AgentRunRequest req, CancellationToken ct);
}
```

**After** — base composes the prompt once, scrubs env once, and talks to a
single `IInteractionProbe` (see §3). Providers get a ready-made
`AgentDriveContext` and implement only the drive body.

```csharp
public abstract class Agent
{
    protected abstract AgentOptions BaseOptions { get; }
    protected abstract IInteractionProbe Probe { get; }   // one seam, not three

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        await Sink.EmitAsync(new AgentEvent.Started(…), ct);

        await using var probe = await Probe.BeginAsync(req, ct);     // install (or no-op)
        var ctx = new AgentDriveContext(
            req,
            SystemPrompt: UnattendedDirective.Compose(BaseOptions.SystemPrompt, req.Mode),
            HooksJsonlPath: probe.HooksJsonlPath,
            Sink: Sink);

        DriveResult raw;
        try { raw = await DriveAsync(ctx, ct); }
        catch (Exception ex) { await Sink.EmitAsync(new AgentEvent.Failed(…), CancellationToken.None); throw; }

        var outcome = probe.Resolve(raw, req.Mode);                 // collect + parse + merge, in one place
        if (outcome.Status == AgentStatus.Failed && outcome.Question is not null)
            await Sink.EmitAsync(new AgentEvent.NonInteractiveViolation(…), CancellationToken.None);
        // …assemble AgentResult…
    }

    protected abstract Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct);
}

public sealed record AgentDriveContext(
    AgentRunRequest Request, string? SystemPrompt, string? HooksJsonlPath, IEventSink Sink);
```

Env-scrub moves to one helper (§7-adjacent): `EnvScrub.Apply(env)` blanks
`ANTHROPIC_API_KEY` / `CLAUDE_API_KEY` / `OPENAI_API_KEY`, called by both
providers and the Host — the literals live in one file.

---

## 3. Hooks — collapse four pieces into one `IInteractionProbe` (Finding 3)

**Before** — the concern is spread over `HookIntegrationOptions`,
`IHookInstaller<T>`, `IAgentHookParser`, `HookResolution`, plus the base
wiring all three and merging a `DetectedQuestion` fallback. The agent knows
about `HooksJsonlPath`, `ShimPath`, parsers, and resolution.

```csharp
// scattered across Core/Agents/*, the base, and provider HookConfig/HookParser overrides
HookConfig            => Options.Hooks;
HookParser            => new ClaudeHookParser();
InstallHookScopeAsync => HookInstaller.InstallAsync(req, shimPath, ct);
// + HookResolution.FromHooksJsonl(...) in the base
// + REMOTEAGENTS_HOOKS_JSONL wired by hand into each provider's spawn code
```

**After** — one seam the base depends on; the typed `IHookInstaller<TAgent>`
(R12 — kept) lives *inside* the probe, not on the agent. The
`REMOTEAGENTS_HOOKS_JSONL` env wiring moves into the scope.

```csharp
public interface IInteractionProbe
{
    Task<IProbeScope> BeginAsync(AgentRunRequest req, CancellationToken ct);
}

public interface IProbeScope : IAsyncDisposable
{
    string? HooksJsonlPath { get; }                 // null when hooks are off
    void ApplyEnv(IDictionary<string, string> env); // sets REMOTEAGENTS_HOOKS_JSONL when on
    HookOutcome Resolve(DriveResult raw, InteractionMode mode); // collect → parse → merge
}

// Provider supplies installer + parser; the probe wires them. No-op when Options.Hooks is null.
public sealed class HookProbe<TAgent>(
    HookIntegrationOptions? cfg, IHookInstaller<TAgent> installer, IAgentHookParser parser)
    : IInteractionProbe where TAgent : Agent { /* … */ }
```

Reader win: "how does question detection work" is now one folder
(`Core/Agents/Hooks/`) and one interface, instead of 11 files.

---

## 4. `ClaudeAgent.DriveAsync` — script only (Findings 1, 2)

**Before** — `DriveAsync` composes the prompt, scrubs env, juggles three
CTSs, runs the JSONL-emitter sidecar, *and* drives the PTY script (~90 lines).

```csharp
protected override async Task<DriveResult> DriveAsync(AgentRunRequest req, CancellationToken ct)
{
    var effectiveOpts = Options with { SystemPrompt = UnattendedDirective.Compose(Options.SystemPrompt, req.Mode) };
    // …deadlineCts + linkedCts + emitterCts…
    var emitter = new ClaudeJsonlEmitter(…); var emitterTask = Task.Run(…);
    var pty = await SpawnPtyAsync(BuildPtyOptions(req.ProjectDir), dct);   // BuildPtyOptions also blanks API keys
    await using var session = new PtySession(pty, onChunk: …, dct);
    await session.WriteLineAsync(launchLine, dct);
    await session.WaitIdleAsync(…); await MaybeDismissDialogAsync(session, dct);
    await session.SubmitAsync(req.Prompt, 500, dct); await session.WaitIdleAsync(…);
    // …/exit, shutdown, emitter grace window, text extraction…
}
```

**After** — prompt already composed by the base; the deadline + emitter
sidecar move behind one `using`; the method reads as the script it claims to be.

```csharp
protected override async Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct)
{
    var sessionId = ctx.Request.SessionId ?? Guid.NewGuid().ToString();
    var launchLine = "claude " + string.Join(' ', BuildClaudeArgs(sessionId, …, ctx.SystemPrompt).Select(Shell.QuoteArg));

    await using var deadline = AgentDeadline.Start(Options.MaxOverallMs, ct);     // the 3-CTS dance, encapsulated
    await using var tail     = ClaudeTail.Start(ctx.Request.ProjectDir, sessionId, Name, ctx.Sink, deadline.Token); // emitter sidecar
    var pty     = await SpawnPtyAsync(BuildPtyOptions(ctx.Request.ProjectDir), deadline.Token);
    await using var session = new PtySession(pty, onChunk: …, deadline.Token);

    await session.WriteLineAsync(launchLine, deadline.Token);
    await session.WaitIdleAsync(Options.LaunchSettleIdleMs, 8_000, minWaitMs: Options.LaunchSettleMinWaitMs, ct: deadline.Token);
    await MaybeDismissDialogAsync(session, deadline.Token);
    await session.SubmitAsync(ctx.Request.Prompt, 500, deadline.Token);
    await session.WaitIdleAsync(Options.IdleThresholdMs, Options.MaxWaitMs, ct: deadline.Token);
    var exitCode = await session.ShutdownAsync(Options.WaitForExitMs, Options.ReaderDrainMs);

    await tail.DrainAsync();
    var text = ClaudeJsonl.TryReadLastAssistantText(ctx.Request.ProjectDir, sessionId, ctx.Request.Prompt)
               ?? ExtractAssistantText(session.Buffer, ctx.Request.Prompt);
    return new DriveResult(text, sessionId, exitCode, session.Buffer);
}
```

`BuildPtyOptions` calls `EnvScrub.Apply(env)` instead of inlining two
`env[...] = ""` lines.

---

## 5. `CodexAgent.DriveAsync` + `SubprocessSession` (Findings 2, 13, 22)

**Before** — ~125 lines: `ProcessStartInfo`, a `Channel` stdout pump,
session-id sniffing inside the `OutputDataReceived` callback, timeout/kill,
file read, cleanup, question detection — all inline. `ScanForSessionId` is a
public static parser bolted onto the agent.

```csharp
protected override async Task<DriveResult> DriveAsync(AgentRunRequest req, CancellationToken ct)
{
    // …tmpDir, psi, psi.Environment["OPENAI_API_KEY"]="", Channel pump,
    //   proc.OutputDataReceived += … ScanForSessionId(e.Data) …,
    //   WaitForExit + Kill, read last.txt, delete tmp, StopPayloadInspector…
}
public static string? ScanForSessionId(string line) { /* JSON shapes */ }
```

**After** — a `SubprocessSession` peer to `PtySession` owns the transport;
session-id parsing moves to `CodexSessionId`; the agent is a short script.

```csharp
protected override async Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct)
{
    using var outFile = TempFile.Create();
    var args = BuildCodexArgs(ctx.Request.SessionId, ctx.Request.ProjectDir, outFile.Path, Options);

    await using var session = SubprocessSession.Start(
        "codex", args, ctx.Request.ProjectDir, EnvScrub.Apply, ctx.Sink, Options.JsonStreamTimeoutMs, ct);

    string? sessionId = ctx.Request.SessionId;
    await foreach (var line in session.StdoutLines(ct))                 // clean line stream, not a callback
        if (sessionId is null && (sessionId = CodexSessionId.Scan(line)) is not null)
            await ctx.Sink.EmitAsync(new AgentEvent.ProviderSessionAttached(…));

    var exit = await session.WaitAsync();
    var text = await outFile.ReadAllTextAsync(ct);
    var detected = StopPayloadInspector.InspectText(text, "codex.text.sentinel", "codex.text.heuristic");
    return new DriveResult(text, sessionId ?? "", exit, session.RawStdout, detected);
}
```

```csharp
// Core/Pty (peer to PtySession) — also the shared core for RunCommand (Finding 22)
public sealed class SubprocessSession : IAsyncDisposable
{
    public static SubprocessSession Start(string exe, IEnumerable<string> args, string cwd,
        Action<IDictionary<string,string>> envMutator, IEventSink? sink, int timeoutMs, CancellationToken ct);
    public IAsyncEnumerable<string> StdoutLines(CancellationToken ct);
    public string RawStdout { get; }
    public Task<int> WaitAsync();   // linked-CTS timeout + Kill(entireProcessTree) live here, once
}
```

---

## 6. Hook parsers — `JsonHookParser` base + shared `JsonEl` accessors (Findings 8, 9)

**Before** — two parsers share a skeleton and three byte-identical private
helpers; the same `TryGetProperty`/`ValueKind` dance is re-coded in 6 files.

```csharp
public sealed class ClaudeHookParser : IAgentHookParser {
    public AgentQuestion? TryParse(JsonElement line) { /* validate, switch(source) */ }
    private static bool   TryGetString(JsonElement o, string n, out string v) { … }   // ← identical
    private static string GetString(JsonElement o, string n) { … }                    // ← identical
    private static JsonElement GetObjectOrEmpty(JsonElement o, string n) { … }         // ← identical, re-parses "{}"
}
public sealed class CodexHookParser : IAgentHookParser { /* same three helpers again */ }
```

**After** — accessors become extensions used everywhere; the parser keeps
only its `source switch`.

```csharp
public static class JsonEl   // one home, used by both parsers, StopPayloadInspector, ClaudeJsonl, CodexSessionId
{
    public static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    public static string GetStr(this JsonElement o, string name) => …;
    public static JsonElement GetObj(this JsonElement o, string name) => …;   // returns Empty, no per-call parse
    public static bool TryStr(this JsonElement o, string name, out string v) => …;
}

public abstract class JsonHookParser : IAgentHookParser
{
    public AgentQuestion? TryParse(JsonElement line)
    {
        if (line.ValueKind != JsonValueKind.Object || !line.TryStr("source", out var source)) return null;
        var payload = line.GetObj("payload");
        return payload.ValueKind == JsonValueKind.Object ? ParseSource(source, payload) : null;
    }
    protected abstract AgentQuestion? ParseSource(string source, JsonElement payload);
}

public sealed class ClaudeHookParser : JsonHookParser
{
    protected override AgentQuestion? ParseSource(string source, JsonElement payload) => source switch { … };
}
```

---

## 7. `RunCommandResult` — one error-text + exit-check idiom (Finding 15)

**Before** — repeated ~15× across `GitOps` / `GhOps`.

```csharp
var res = await RunCommand.RunAsync("git push …", new RunCommandOptions(Cwd: req.ProjectDir), ct);
if (res.ExitCode != 0)
    throw new InvalidOperationException(
        $"git push failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
return res;
```

**After** — on the result; call sites collapse to one line.

```csharp
public sealed record RunCommandResult(string Command, int ExitCode, string Stdout, string Stderr, bool TimedOut, long DurationMs)
{
    public string ErrorText => string.IsNullOrEmpty(Stderr) ? Stdout : Stderr;
    public RunCommandResult EnsureOk(string op) =>
        ExitCode == 0 ? this : throw new InvalidOperationException($"{op} failed: {ErrorText}");
}

// call site:
return (await RunCommand.RunAsync("git push …", new RunCommandOptions(Cwd: req.ProjectDir), ct)).EnsureOk("git push");
```

---

## 8. Shell-quoting — one quoter (Findings 10, 16)

**Before** — six implementations; `GitOps` uses an allowlist regex while the
rest use a denylist — they can disagree.

```csharp
// Shell.cs          → IndexOfAny(QuoteTriggers)        (denylist)
// GhOps.cs          → IndexOfAny(QuoteTriggers)        (copy)
// GitWorktree.cs    → QuotePath / QuoteIdent / Needs   (copy ×3)
// DotnetValidator.cs→ IndexOfAny(QuoteTriggers)        (copy)
// GitOps.cs         → SafePath allowlist regex         (DIFFERENT RULE)
```

**After** — one canonical quoter (allowlist — the safer default), five copies
deleted.

```csharp
public static class Shell
{
    private static readonly Regex Safe = new(@"^[A-Za-z0-9_./-]+$", RegexOptions.Compiled);
    public static string QuoteArg(string s) => Safe.IsMatch(s) ? s : "\"" + s.Replace("\"", "\\\"") + "\"";
}
// GitOps/GhOps/GitWorktree/DotnetValidator → Shell.QuoteArg(s)
```

---

## 9. `Reviews` — `IReviewer` + one verdict record (Findings 1, 17)

**Before** — `Reviews` `new`s a `CodexAgent` directly, and carries two records
with the same three fields.

```csharp
public sealed record CodexReviewArtifact(Verdict Verdict, string SessionId, string Text);
public sealed record CodexVerdict(Verdict Verdict, string Text, string SessionId) { public bool IsApprove => …; }

var codex = new CodexAgent { Name = "codex", Sink = sink, Options = codexOptions ?? new CodexAgentOptions(…) };
var review = await codex.RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir), ct);
```

**After** — reviewer resolved through a seam; one record, serialized directly.

```csharp
public interface IReviewer { Task<CodexVerdict> ReviewAsync(string projectDir, string sessionDir, string userPrompt,
    string projectKind, string validationLabel, IEventSink sink, CancellationToken ct); }

public sealed record CodexVerdict(Verdict Verdict, string SessionId, string Text)
{
    public bool IsApprove => Verdict == Verdict.Approve;
    public bool IsRevise  => Verdict == Verdict.Revise;
    public bool IsUnclear => Verdict == Verdict.Unclear;
}
// the verdict IS the artifact — WriteReviewArtifactAsync serializes CodexVerdict; CodexReviewArtifact deleted.
```

`ReviewFlow` takes an `IReviewer` (default `CodexReviewer`) instead of the
static `Reviews.AskCodexForVerdictAsync` → testable without a real codex.

---

## 10. `SessionArtifact` — de-provider the core (Finding 18)

**Before** — the core session enum and a Host endpoint hard-code provider
artifact names.

```csharp
public enum SessionArtifact { Transcript, ClaudeText, ClaudeRaw, CodexReview, CodexReviewJl }
// Session.cs maps → "claude-text.txt", "codex-review.txt", …
// Program.cs:175-179 serves /artifacts/claude-text & /codex-review by name
```

**After** — core owns only its own files; provider artifacts are
`(kind, basename)` values the provider supplies.

```csharp
public enum CoreArtifact { Transcript, Meta, Prompt }              // core, provider-agnostic
public sealed record ProviderArtifact(string Kind, string Basename); // e.g. ("claude.raw","claude-raw.txt")

// Session.WriteArtifactAsync(ProviderArtifact a, …) — basename still appears once, but not in the core enum.
// Host endpoint iterates the session dir / a manifest instead of naming providers.
```

---

## 11. `UnityChecks` — split the god-class (Finding 12)

**Before** — one 370-line static class: compile, EditMode, PlayMode,
analyzers, NUnit parsing, exe resolution, diagnostic extraction.

```csharp
public static class UnityChecks {
    public static Task<UnityCheckResult>  CompileAsync(…);
    public static Task<UnityTestResult>   EditModeTestsAsync(…);
    public static Task<UnityTestResult>   PlayModeTestsAsync(…);
    public static Task<UnityAnalyzerResult> AnalyzersAsync(…);
    public static NUnitParsed ParseNUnitResults(string path);   // already public static
    public static IReadOnlyList<string> ExtractDiagnostics(string buildOutput); // already public static
    private static string ResolveUnityExe(…); /* + tail/indent helpers */
}
```

**After** — focused, injectable pieces a validator composes.

```csharp
public interface IUnityRunner { Task<(int Exit, bool TimedOut)> RunAsync(string projectDir, UnityInvocation inv, CancellationToken ct); }
public sealed class NUnitResultParser { public NUnitParsed Parse(string path); }
public static class BuildDiagnostics  { public static IReadOnlyList<string> Extract(string buildOutput); }
public static class UnityExe          { public static string? FindForProject(string projectDir); }

public sealed class UnityFullValidator(IUnityRunner runner, NUnitResultParser nunit) : IValidator { /* composes them */ }
```

---

## 12. `Run` — lift transport state off the shared object (Finding 21)

**Before** — live + durable + subprocess-transport + UI fields on one class.

```csharp
public sealed class Run
{
    public CancellationTokenSource Cts { get; }
    public IEventSink Sink { get; }
    public RunStatus Status { get; set; }
    public string? SessionId { get; set; }
    public string? SessionDir { get; set; }
    public Task? TailerTask { get; set; }            // ← only SubprocessFlowExecutor uses this
    public AgentQuestion? PendingQuestion { get; set; }
}
```

**After** — transport-only state lives with the executor that owns it.

```csharp
public sealed class Run { /* live + durable + UI; no TailerTask */ }

// inside SubprocessFlowExecutor:
private sealed class SubprocessRunState { public Task? TailerTask; public string? SessionDir; }
private readonly ConcurrentDictionary<string, SubprocessRunState> _state = new();
```

---

## 13. Host JSON — share one source-gen context (Finding 20)

**Before** — the Host reads back the library's source-gen-written events with
reflection, per line (hot path), and persists runs with reflection too.

```csharp
// SubprocessFlowExecutor.cs:173 — per transcript line
evt = JsonSerializer.Deserialize<AgentEvent>(line, jsonOpts);     // reflection
// RunStore.cs:50,77
JsonSerializer.Deserialize<RunsFile>(json, JsonOpts);             // reflection
```

**After** — one context in the contracts assembly; both library and Host use it.

```csharp
// RemoteAgents.Contracts
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(RunsFile))]
[JsonSerializable(typeof(RunRecord))]
public partial class RemoteAgentsJsonContext : JsonSerializerContext { }

// Host — no reflection, no per-event allocation of a reflection plan
evt  = JsonSerializer.Deserialize(line, RemoteAgentsJsonContext.Default.AgentEvent);
file = JsonSerializer.Deserialize(json, RemoteAgentsJsonContext.Default.RunsFile);
```

---

## Net effect (line-count, indicative)

| Area | Before | After | Driver |
|---|---|---|---|
| `ClaudeAgent.DriveAsync` | ~90 lines | ~35 | §2, §4 |
| `CodexAgent.DriveAsync` | ~125 lines | ~25 + `SubprocessSession` | §5 |
| Hook concern | 11 files, 4 seams | `Hooks/` folder, 1 seam | §3 |
| Hook parsers | 2 × (skeleton + 3 helpers) | 1 base + 1 `JsonEl` | §6 |
| `GitOps`/`GhOps` throws | ~15 hand-written | `EnsureOk(op)` | §7 |
| Shell-quoting | 6 impls (2 rule-sets) | 1 | §8 |
| Host event read | reflection / line | source-gen | §13 |

All sketches preserve behavior; the wins are fewer owners, one seam per
concern, and symmetry between the two providers.

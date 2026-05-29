---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-2, #agents]
---

# Layer 2 — Agents (base + providers)

## Target structure

The `Agent` base class owns **every cross-provider concern**:

- Lifecycle events (`Started` / `Completed` / `Failed`) — already does.
- Hook lifecycle (install at start, uninstall in finally) via `IHookInstaller`.
- Mode composition (`UnattendedDirective.Compose(SystemPrompt, mode)`).
- Provider-env scrub (blank `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY`, `OPENAI_API_KEY`).
- Hook resolution (`HookResolution.FromHooksJsonl`) using the
  provider's parser.
- `NonInteractiveViolation` emission on failed-with-question.
- Final `AgentResult` shape assembly.

A provider implements **one method**: `DriveAsync(AgentDriveContext, ct) → AgentDriveResult`.
`AgentDriveContext` carries the effective options, the system prompt
(post-compose), the resolved hook scope's JSONL path, and the run
request. `AgentDriveResult` carries `Text`, `SessionId`, `ExitCode`,
`RawOutput`. The base composes the resolved `AgentResult` from drive
output + hook outcome + violation emit.

Providers don't `try { ... } finally { Hooks.Uninstall(...) }`. They
don't compose system prompts. They don't scrub env vars. They don't
call `HookResolution`. They don't emit `NonInteractiveViolation`. All
of that is the base.

**Provider identity = type identity.** `Agent` has no `Provider`
property and no `[Provider("...")]` attribute. `ClaudeAgent` *is* the
Claude provider; the type system encodes it. The string `"claude"`
appears in code in exactly two places: the namespace folder name and
the wire-side `JsonPolymorphic` discriminator on serialized events.
Nowhere else. (See [`99-rejected.md`](99-rejected.md) R12.)

**Named agents** ("Planner", "Documenter", "Researcher") are
*polymorphic preset values*, not classes and not registry strings:

```csharp
public abstract record AgentPreset
{
    public abstract Agent Build(IServiceProvider sp, IEventSink sink);
}

public sealed record ClaudePreset(string Model, PromptRef Prompt) : AgentPreset
{
    public override Agent Build(IServiceProvider sp, IEventSink sink) =>
        ActivatorUtilities.CreateInstance<ClaudeAgent>(sp, this, sink);
}

public sealed record CodexPreset(string Model, PromptRef Prompt) : AgentPreset
{
    public override Agent Build(IServiceProvider sp, IEventSink sink) =>
        ActivatorUtilities.CreateInstance<CodexAgent>(sp, this, sink);
}

public static class AgentPresets
{
    public static readonly ClaudePreset Planner    = new("opus",    Prompts.Planner);
    public static readonly ClaudePreset Documenter = new("haiku",   Prompts.Documenter);
    public static readonly CodexPreset  Researcher = new("gpt-5.5", Prompts.Researcher);
}
```

Call sites pass the value, not a string: `AgentPresets.Planner.Build(sp, sink)`.
No `services.AddAgentPreset("planner", ...)` lookup. The preset is its
own handle; the typed subtype encodes the provider; the prompt key is a
typed `PromptRef`. The name `"Planner"` exists exactly once — as the
static field name. (See [`99-rejected.md`](99-rejected.md) R14.)

**Hook installers** are typed against their agent:

```csharp
public interface IHookInstaller<TAgent> where TAgent : Agent
{
    Task<IHookScope> InstallAsync(HookContext ctx, string shimPath, CancellationToken ct);
}
public interface IHookScope : IAsyncDisposable
{
    string HooksJsonlPath { get; }
}
```

`ClaudeAgent`'s constructor takes `IHookInstaller<ClaudeAgent>`;
`CodexAgent`'s takes `IHookInstaller<CodexAgent>`. DI binds the
pairing. Mispairing is a compile error, not a runtime string-match
failure. The base agent calls `installer.InstallAsync(...)` and
disposes the scope; the concrete `ClaudeHookInstaller` writes
`<projectDir>/.claude/settings.json`, `CodexHookInstaller` writes
`~/.codex/hooks.json` — neither leaks through the call shape.

## Current structure

- [`Agent.cs`](../../remote-agents-dotnet/src/RemoteAgents/Core/Agents/Agent.cs)
  — 50 lines, emits Started/Completed/Failed around `ExecuteAsync`.
- [`ClaudeAgent.cs`](../../remote-agents-dotnet/src/RemoteAgents/Providers/Claude/ClaudeAgent.cs)
  — 227 lines. `ExecuteAsync` wraps `RunInternalAsync` in
  install/uninstall (lines 68-82). `RunInternalAsync` calls
  `UnattendedDirective.Compose` (line 89), `HookResolution.FromHooksJsonl`
  (line 151), emits `NonInteractiveViolation` (lines 154-159), scrubs
  env (lines 181-182). Provider-specific code: ~110 lines (PTY drive).
- [`CodexAgent.cs`](../../remote-agents-dotnet/src/RemoteAgents/Providers/Codex/CodexAgent.cs)
  — 249 lines. Same shape: install/uninstall (lines 53-68), compose
  (line 136), hook resolution (line 168), violation emit (lines
  187-192), env scrub (line 92). Provider-specific code: ~130 lines
  (Process drive + JSON session-id sniff + text-fallback sentinel).
- [`NamedAgents/Planner.cs`](../../remote-agents-dotnet/src/NamedAgents/Planner.cs),
  [`Documenter.cs`](../../remote-agents-dotnet/src/NamedAgents/Documenter.cs),
  [`Researcher.cs`](../../remote-agents-dotnet/src/NamedAgents/Researcher.cs)
  — static `Create(IEventSink?)` factories returning concrete
  `ClaudeAgent` / `CodexAgent`. Namespace `Flows.Agents`.
- [`ClaudeHookConfig.cs`](../../remote-agents-dotnet/src/RemoteAgents/Providers/Claude/ClaudeHookConfig.cs)
  — static `Install(projectDir, shim)` / `Uninstall(projectDir)`.
- [`CodexHookConfig.cs`](../../remote-agents-dotnet/src/RemoteAgents/Providers/Codex/CodexHookConfig.cs)
  — static `Install(configDir, shim)` / `Uninstall(configDir)` with
  `DefaultConfigDir() = ~/.codex`.

## Gap

1. **`Agent` base owns only lifecycle events.** Every other concern is
   reimplemented in every provider.
2. **Two providers each carry the same five-line install/finally block.**
   With one more provider (Gemini? Local model?) it'll be three.
3. **`UnattendedDirective.Compose(...)` is called by every provider**,
   never by the base.
4. **`HookResolution.FromHooksJsonl(...)` is called by every provider**,
   never by the base.
5. **`NonInteractiveViolation` is emitted by every provider**, with
   the same `if (Failed && question != null)` branch and the same
   shape.
6. **Env-key scrub is duplicated** in two places per provider (`Host/FlowRunner.cs:177-179`
   is a *third* place doing it for the spawned subprocess).
7. **`NamedAgents` are static factories returning concrete types.**
   Three concerns mashed into one method: identity (the name),
   provider choice, preset (model + system prompt). The namespace
   `Flows.Agents` doesn't match the library's `RemoteAgents.Agents`.
   They're presets pretending to be classes.
8. **`Reviews.AskCodexForVerdictAsync` is a fourth construction site**
   ([`Reviews.cs:54-59`](../../remote-agents-dotnet/src/RemoteAgents/Flows/Reviews.cs))
   — picks the agent's name (`"codex"`), default sandbox, timeout,
   sink. Nothing about constructing an agent is centralized.
9. **`ClaudeHookConfig.Install` and `CodexHookConfig.Install` take
   different arguments** (`projectDir` vs `configDir`). Different
   scope semantics leak through the call shape into agent code.
10. **`PtySession.SubmitAsync` and `WaitIdleAsync(minWaitMs)`** are
    Claude-shaped concessions on a "generic" PTY primitive. (Cross-ref
    [Layer 5](05-sessions.md) — that file also flags the PtySession
    naming/move question.)
11. **Provider identity leaks as runtime strings.** `ClaudeAgent.Name`
    is set to `"claude"`; `CodexAgent.Name` to `"codex"`; hook installer
    pairing in today's prototype thinking would have matched on those
    strings. The class hierarchy already encodes provider identity;
    the strings are parallel state waiting to drift.

## Migration steps

1. **Introduce `IHookInstaller<TAgent>` + `IHookScope`** with a unified
   `InstallAsync(HookContext, shimPath, ct) → IHookScope`.
   `HookContext` carries whatever the installer needs (`ProjectDir`
   for Claude, nothing for Codex which uses HOME). `IHookScope.DisposeAsync()`
   does the uninstall + restore-backup; `IHookScope.HooksJsonlPath`
   is what `HookResolution` reads. The `TAgent` parameter binds the
   installer to its provider at compile time.
2. **Implement `ClaudeHookInstaller : IHookInstaller<ClaudeAgent>` and
   `CodexHookInstaller : IHookInstaller<CodexAgent>`**. They wrap the
   existing `ClaudeHookConfig.Install/Uninstall` and
   `CodexHookConfig.Install/Uninstall` logic. The statics stay
   internal for now.
3. **Introduce `AgentDriveContext` and `AgentDriveResult`** records.
   Drive context = `{ Request, EffectiveOptions, SystemPrompt,
   HooksJsonlPath?, Sink }`. Drive result = `{ Text, SessionId,
   ExitCode, RawOutput }`.
4. **Make `Agent.ExecuteAsync` sealed**, add `protected abstract
   DriveAsync(AgentDriveContext, ct)`, add `protected abstract
   IHookInstaller HookInstaller { get; }`, add `protected abstract
   IAgentHookParser HookParser { get; }`. The sealed `ExecuteAsync`
   does:

   ```csharp
   using var hookScope = Options.Hooks is null ? null
       : HookInstaller.Install(BuildHookContext(req), Options.Hooks.ShimPath);
   var sysPrompt = UnattendedDirective.Compose(Options.SystemPrompt, req.Mode);
   var ctx = new AgentDriveContext(req, Options, sysPrompt, hookScope?.HooksJsonlPath, Sink);
   var drive = await DriveAsync(ctx, ct);
   var outcome = HookResolution.FromHooksJsonl(hookScope?.HooksJsonlPath, HookParser, req.Mode);
   if (outcome.Status == AgentStatus.Failed && outcome.Question is not null)
       await Sink.EmitAsync(new AgentEvent.NonInteractiveViolation(...), CancellationToken.None);
   return new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput,
       outcome.Status, outcome.Question, outcome.FailureReason);
   ```
5. **Shrink `ClaudeAgent`.** Delete `ExecuteAsync`+`RunInternalAsync`
   wrappers. Move PTY-drive code into `DriveAsync(ctx, ct)`. Keep
   `BuildClaudeArgs`, `DetectStartupDialog`, `SpawnPtyAsync`,
   `BuildPtyOptions`, `MaybeDismissDialogAsync`, `ExtractAssistantText`.
   Constructor takes `IHookInstaller<ClaudeAgent>` directly — no
   `Provider` property, no attribute. `HookParser => new ClaudeHookParser()`.
   Net: ~110 lines, down from 227.
6. **Shrink `CodexAgent`.** Same surgery. Constructor takes
   `IHookInstaller<CodexAgent>`. Codex's text-fallback sentinel
   (`StopPayloadInspector.InspectText`) stays inside `DriveAsync` and
   writes into `drive.QuestionOverride`, which the base prefers over
   the hooks-jsonl outcome when set. Net: ~130 lines, down from 249.
7. **Delete `NamedAgents/Planner.cs`, `Documenter.cs`, `Researcher.cs`.**
   Replace with static `AgentPresets.Planner|Documenter|Researcher`
   values of the polymorphic preset type. Prompts still load from the
   same files (now via `PromptRef`). Call sites change from
   `Planner.Create(sink)` to `AgentPresets.Planner.Build(sp, sink)`.
8. **Minimal or no `IAgentFactory`.** The preset's own `Build(sp, sink)`
   method is the construction site. `IAgentFactory` survives only if a
   cross-cutting concern (overrides, instrumentation) needs a single
   choke point — in which case it's a thin wrapper around
   `preset.Build(sp, sink)`. No string-keyed lookups, no provider
   registry; the preset's runtime type IS the provider selection.
9. **Rewrite `Reviews.AskCodexForVerdictAsync`** to take a preset
   (or, better, an `IReviewer` abstraction it resolves from). The
   helper stops constructing a `CodexAgent` directly.
10. **Move env-key scrub** into the agent base (one place) and into a
    single `Primitives.EnvScrub.Apply(envDict)` helper that
    `FlowRunner.BuildProcessStartInfo` also calls. Three copies → one.
11. **(Defer to Layer 5/separate.)** Decide whether `PtySession` stays
    generic with the Claude knobs or moves to `Providers/Claude/`.
    Out of scope for Layer 2 because it doesn't affect the base
    template-method shape.

## Acceptance criteria

Layer 2 is done when:

- `ClaudeAgent` and `CodexAgent` each have a single `DriveAsync`
  method that is the only place provider-specific code lives.
- The strings `"ANTHROPIC_API_KEY"`, `"CLAUDE_API_KEY"`,
  `"OPENAI_API_KEY"` appear in at most one source file.
- `try { ... } finally { Hooks.Uninstall(...) }` does not appear in
  any provider source file.
- `HookResolution.FromHooksJsonl` is called from exactly one place
  (the base).
- `UnattendedDirective.Compose` is called from exactly one place
  (the base).
- `NamedAgents/` is empty or deleted.
- `Reviews.AskCodexForVerdictAsync` does not contain `new CodexAgent(...)`.
- `Agent` has no `Provider` property; no `[Provider]` attribute exists.
- The string literal `"claude"` appears in `src/RemoteAgents/` only
  in (a) `Providers/Claude/` namespace declarations and (b) wire-side
  `JsonPolymorphic` discriminator attributes. Same for `"codex"`.
- `IHookInstaller<TAgent>` is the only hook-installer interface;
  `IHookInstaller` (non-generic) does not exist.
- Existing smoke tests pass with byte-identical session outputs
  (transcript.jsonl content unchanged modulo ordering).

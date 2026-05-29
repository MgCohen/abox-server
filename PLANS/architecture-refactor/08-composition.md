---
type: plan
status: draft
tags: [#architecture, #refactor, #layer-8, #composition, #di, #config]
---

# Layer 8 — Composition root & configuration

## Target structure

**One place wires the library: `services.AddRemoteAgents(opts => ...)`.**
Lives in `RemoteAgents.Hosting.csproj` (new, depends on
`Microsoft.Extensions.DependencyInjection.Abstractions`, contracts,
and the library). **No `IConfiguration` dependency.**

```csharp
services.AddRemoteAgents(opts =>
{
    // Provider registration — defaults baked into the options record;
    // override with a lambda when a knob actually needs to differ.
    opts.UseClaude();
    opts.UseCodex(o => o.Sandbox = "read-only");

    // Flow registrations.
    opts.AddFlow<ClaudeOnlyFlow>();
    opts.AddFlow<FullReviewFlow>();
    opts.AddFlow<UnityReviewFlow>();
    // ...

    // Sink set — chosen per host.
    opts.AddSink<ConsoleSink>();
    opts.AddSink<JsonlSink>();
    opts.AddSink<ProviderTranscriptArchiver>();
});

// Host adds its own sink:
services.AddRemoteAgentSink<ChannelSink>();
```

Three callers wire through this entry point:

1. **CLI dispatcher** (`cli/agents-dotnet.cs`) — creates a minimal
   `IServiceCollection`, calls `AddRemoteAgents`, resolves
   `IFlowRunner`, runs the requested flow.
2. **Host** (`ui/RemoteAgents.Host/Program.cs`) — already has DI.
   Calls `AddRemoteAgents` plus its `AddRemoteAgentSink<ChannelSink>()`,
   plus the Host-specific services (`LiveRunRegistry`, `RunStore`,
   `InProcessFlowExecutor`).
3. **Tests** — register a capture sink, fake hook installer, etc.

`UseClaude` / `UseCodex` are the bundle registrations: each one
registers its agent, its `IHookInstaller<TAgent>`, and binds its
options record. Defaults live on the record (positional defaults
or property initializers — same place they live today); a lambda
overrides any knob explicitly.

**Agent options are plain records with defaults, not
`IConfiguration`-bound.** This project is local-first, single-host;
there are no environments to layer per-key overrides across. A
config-file binder buys nothing and costs a magic string per knob
(see [`99-rejected.md`](99-rejected.md) R13). If one knob ever
genuinely needs to differ per host (e.g. `OrchestratorRoot` between
laptop and VM), it reads from a named env var at the override site:

```csharp
opts.UseClaude(o =>
{
    if (System.Environment.GetEnvironmentVariable("CLAUDE_BIN") is { } bin)
        o.ClaudeBinary = bin;
});
```

Direct. Greppable. Typed at the property assignment, not at a colon-
delimited path. No binder dependency.

**Agents are obtained from preset values, not a factory lookup.**
`AgentPreset` is polymorphic; the subtype encodes the provider:

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

// Usage in a flow:
var planner = AgentPresets.Planner.Build(sp, ctx.Sink);
```

No `services.AddAgentPreset("planner", ...)`. No `IAgentFactory.Create("planner", ...)`.
The preset value is its own identity; the field name `Planner`
appears exactly once. (See [`99-rejected.md`](99-rejected.md) R14.)

`IAgentFactory` survives only as a thin pass-through wrapper —
`factory.Build(preset, sink) => preset.Build(sp, sink)` — IF a
cross-cutting concern (capture-sink injection in tests, instrumentation
hook) needs a choke point. If nothing needs it, the interface
doesn't exist. The default plan: no `IAgentFactory`. Add it only
when something asks for it.

## Current structure

- **No `AddRemoteAgents` extension.** The library has no DI surface.
- **No `IOptions<>` binding** for agent options. Defaults are
  positional record arguments; overrides are `new ClaudeAgentOptions(...)`
  at each construction site.
- **Three ad-hoc compositions** (one per entry point):
  - CLI dispatcher: no DI, just static calls.
  - Flow scripts: `await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME)` + `new ClaudeAgent { ... }` + `new CodexAgent { ... }`.
  - Host: minimal DI — `AddSingleton<RunRegistry>()`,
    `AddSingleton<RunStore>()`, `AddSingleton<FlowRunner>()`,
    `AddSignalR()`, `AddCors(...)`.
- **Four agent construction sites** (per Layer 2): each flow,
  `NamedAgents/*`, `Reviews.AskCodexForVerdictAsync`, (eventually)
  the Host.
- **Hardcoded sink composition** in `FlowBootstrap.cs:79-83`.
- **No agent registry / preset registry.** `NamedAgents/Planner.cs`
  etc. are static factory methods, not data.
- **`appsettings.json` carries only `RemoteAgents:OrchestratorRoot`.**

## Gap

1. **No composition root.** Three entry points each wire what they
   need ad hoc, with overlapping responsibilities.
2. **Knob overrides require recompiling.** Defaults live on positional
   record args; there's no per-host override path. (The fix is a
   lambda override at registration, not `IConfiguration` binding —
   see Target / R13.)
3. **No typed preset values.** Every consumer that wants a "planner"
   or "codex reviewer" hand-rolls construction with a different shape.
4. **No flow registry through DI.** The two `flows/*.cs` enumerations
   are file-system globs, not service registrations.
5. **Sink composition is locked in `FlowBootstrap`.** Cannot be
   extended without a library edit.
6. **Hook installers are static classes**, not services. Can't be
   replaced for tests.

## Migration steps

1. **Create `RemoteAgents.Hosting.csproj`.** TargetFramework matches
   the library. Depends on
   `Microsoft.Extensions.DependencyInjection.Abstractions` + contracts
   + library. **No `Microsoft.Extensions.Configuration.*` references**
   — options are records with defaults; overrides are lambdas. (We
   may consolidate into the library later; separate for now so the
   library stays DI-agnostic if a no-DI caller emerges.)
2. **Define `RemoteAgentsOptions`** — the builder type passed to
   `AddRemoteAgents`. Methods: `UseClaude`, `UseCodex`, `AddSink`,
   `AddFlow`. (No `AddAgentPreset`, no `AddHookInstaller` — the
   `UseClaude`/`UseCodex` bundle registers the agent type plus its
   `IHookInstaller<TAgent>`; presets are static values, not
   registered DI entries.)
3. **`services.AddRemoteAgents(Action<RemoteAgentsOptions>)`**
   extension method registers everything internally: `IFlowRegistry`,
   `IFlowRunner`, `IEventSinkBuilder`, `OrchestratorPaths`, the
   provider agent types + their hook installers (via `UseClaude` /
   `UseCodex`), etc.
4. **Options stay as records with defaults.** No `IOptions<>` binder.
   `UseClaude(Action<ClaudeAgentOptions>? configure = null)` reads
   `new ClaudeAgentOptions()`, applies the optional lambda, registers
   the result as a singleton.
5. **Define `AgentPreset` (polymorphic) + `ClaudePreset` / `CodexPreset`
   subtypes** in contracts. Define `AgentPresets` static value class
   in the library. Call sites construct an agent via
   `AgentPresets.Foo.Build(sp, sink)`.
6. **Delete `NamedAgents/Planner|Documenter|Researcher`.** Replace
   with entries in `AgentPresets`.
7. **Register flows** through `AddFlow<T>()`. Replace file-system
   discovery in both `cli/agents-dotnet.cs` and `Program.cs` with
   the registry.
8. **Register sinks** through `AddSink<T>()`. Replace hardcoded
   composite in `FlowBootstrap`.
9. **CLI dispatcher migration.** `cli/agents-dotnet.cs` builds a
   minimal host:
   ```csharp
   var host = Host.CreateDefaultBuilder(args)
       .ConfigureServices((_, services) =>
           services.AddRemoteAgents(o => RegisterEverything(o)))
       .Build();
   var runner = host.Services.GetRequiredService<IFlowRunner>();
   var result = await runner.RunAsync(flowName, flowArgs);
   Environment.ExitCode = MapResult(result);
   ```
10. **Host migration.** `Program.cs` adds `services.AddRemoteAgents(...)`
    and `services.AddRemoteAgentSink<ChannelSink>()`. Deletes the
    direct registrations that overlap.
11. **Test migration.** Existing smokes that hand-roll an agent
    switch to `AgentPresets.Foo.Build(sp, sink)`.

## Acceptance criteria

Layer 8 is done when:

- `services.AddRemoteAgents(...)` is the only entry point used by
  the CLI dispatcher, the Host, and the test harness.
- `new ClaudeAgent` and `new CodexAgent` do not appear outside the
  preset `Build` methods and `ActivatorUtilities.CreateInstance` /
  its tests.
- `RemoteAgents.Hosting.csproj` does not reference any
  `Microsoft.Extensions.Configuration.*` package.
- No source file calls `IConfiguration.GetSection(...)` to populate
  `ClaudeAgentOptions` or `CodexAgentOptions`.
- `FlowRegistry` is the only source of "what flows exist."
- Sink set is controlled by registration; `FlowBootstrap` (or its
  successor) has no `new CompositeSink(...)`.
- Hook installers are resolved through DI as `IHookInstaller<TAgent>`,
  not static classes.
- `NamedAgents/*` is gone — replaced by static `AgentPresets.*`
  values consumed via `preset.Build(sp, sink)`.
- No `AddAgentPreset(string, ...)` registration call exists; presets
  are values, not DI entries.

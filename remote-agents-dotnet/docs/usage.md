# Usage

Day-to-day commands for the C# orchestrator. For internals, read [`architecture.md`](architecture.md) first.

---

## 1. Prerequisites

- **Windows** (v1 is Windows-only; Linux follow-up is a known port tax)
- **.NET 10 SDK** (`dotnet --version` ≥ 10.0)
- **`claude` CLI** on PATH, logged into a Claude Max subscription (`claude auth status` → `authMethod: "claude.ai"`)
- **`codex` CLI** on PATH, logged into a ChatGPT subscription
- **No API-key env vars set** — `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY`, `OPENAI_API_KEY` must all be unset. `SubscriptionGuard.CheckAsync()` will refuse to start otherwise.

Verify the toolchain in one shot:

```pwsh
claude --version
codex --version
claude auth status
dotnet --version
```

---

## 2. Register projects

`<repo>/projects.json` maps short names to absolute paths:

```json
{
  "card-framework":      "C:/Unity/CardFramework",
  "scaffold":            "C:/Unity/Scaffold",
  "gear-engine":         "C:/Unity/Gear-Engine",
  "remote-unity-agents": "C:/Unity/remote-unity-agents"
}
```

Both orchestrators (JS and C#) read from this file. Add your projects here once.

---

## 3. The CLI

```pwsh
dotnet run bin/agents-dotnet.cs list                       # show available flows
dotnet run bin/agents-dotnet.cs projects                   # show configured projects
dotnet run bin/agents-dotnet.cs run <flow> <project> "<prompt>"  # run a flow
```

The CLI is a thin shim — `run <flow>` just spawns `dotnet run flows/<flow>.cs -- ...args`. You can call the flow file directly if you prefer.

---

## 4. The three example flows

### claude-only — baseline

```pwsh
dotnet run bin/agents-dotnet.cs run claude-only card-framework `
    "Add an XML doc comment to GameManager.Awake."
```

Runs Claude once, captures whatever changed, writes the session transcript. No validation, no review, no git.

### claude-validate — Claude + project validator

```pwsh
dotnet run bin/agents-dotnet.cs run claude-validate remote-unity-agents `
    "Fix the syntax error in remote-agents-dotnet/scratch/Broken.cs."
```

Runs Claude, then runs the project validator. On failure, feeds errors back to the same Claude session and retries — up to 3 attempts. Exit 0 if validation passes, exit 2 if it doesn't.

### full-review — full pipeline

```pwsh
dotnet run bin/agents-dotnet.cs run full-review card-framework `
    "Refactor InventorySlot.OnDrop to early-return on null."
```

Claude → validator (fix loop) → Codex review of the diff → optional Claude revision pass → commit. Add `--push` to push the commit on the current branch (refuses force-push to `main`/`master`).

Refuses to run if the working tree is dirty. That's deliberate — you don't want the orchestrator's commit to include unrelated edits.

---

## 5. Reading a session

Every flow run writes `remote-agents-dotnet/sessions/<isoTs>-<slug>/` with:

| File | What's in it |
|---|---|
| `prompt.txt` | the verbatim user prompt |
| `meta.json` | id, orchestrator, schemaVersion, timing, result |
| `transcript.jsonl` | one JSON event per line (Started/StreamChunk/DialogDismissed/Completed/Failed) |
| `claude-turn-N.jsonl` | the corresponding Claude session JSONL (tool calls, token usage, rate-limit signals) |
| `codex-turn-N.jsonl` | the corresponding Codex rollout JSONL |
| `claude-raw.txt` | the raw PTY byte stream (debugging only) |
| `claude-text.txt` | best-effort extracted assistant text |
| `codex-review.jsonl` / `codex-review.txt` | full-review only: verdict + text |

If you want to know *what Claude actually did*, read `claude-turn-N.jsonl`. If you want to know *what the orchestrator did with it*, read `transcript.jsonl`.

---

## 6. Writing a new flow

Drop a `.cs` file in `flows/`. The first two non-comment lines are project references:

```csharp
#:project ../RemoteAgents/RemoteAgents.csproj
#:project ../validation/Validators.csproj   // only if you use IValidator impls

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string FLOW_NAME = "my-flow";

if (args.Length < 2) { /* usage + Environment.ExitCode = 2 + return */ }
var projectName = args[0];
var userPrompt = string.Join(' ', args[1..]).Trim();

await SubscriptionGuard.CheckAsync();
var projectDir = ProjectRegistry.Resolve(projectName);
var session = Session.Start(new StartSessionRequest(projectDir, projectName, userPrompt, FLOW_NAME));

var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile),
    new ProviderJsonlIngestSink(session.Dir, projectDir));

try
{
    var claude = new ClaudeAgent { Name = "claude", Sink = sink };
    var result = await claude.RunAsync(new AgentRunRequest(userPrompt, null, projectDir));

    // …your flow logic here…

    session.End("done");
}
catch (Exception ex)
{
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
```

Run it:

```pwsh
dotnet run flows/my-flow.cs <project> "<prompt>"
```

The library imposes zero control flow on you. The validate-and-fix loop in `claude-validate.cs` is a hand-written `while`. The commit step in `full-review.cs` is a hand-written `if`. Tune them by editing.

---

## 7. Writing a project validator

Add a file under `validation/` implementing `IValidator`:

```csharp
using RemoteAgents.Validation;
using RemoteAgents.Primitives;

namespace RemoteAgents.Validation.MyProject;

public sealed class MyProjectValidator : IValidator
{
    public async Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync(
            "dotnet build /nologo /clp:Summary",
            new RunCommandOptions(Cwd: projectDir),
            ct);
        return new ValidationResult(
            Ok: res.ExitCode == 0,
            Summary: res.ExitCode == 0 ? "build ok" : $"build failed (exit {res.ExitCode})",
            Errors: res.Stderr);
    }
}
```

The `Validators.csproj` picks up everything in `validation/*.cs` automatically. Reference it from your flow:

```csharp
#:project ../validation/Validators.csproj
using RemoteAgents.Validation.MyProject;
// ...
IValidator validator = new MyProjectValidator();
```

---

## 8. Naming a configured agent

If you find yourself constructing the same `ClaudeAgent { Model = ..., SystemPrompt = ... }` in multiple flows, hoist it into `agents/`:

```csharp
// agents/Refactorer.cs
using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace Flows.Agents;

public static class Refactorer
{
    public static ClaudeAgent Create(IEventSink? sink = null) => new()
    {
        Name = "refactorer",
        Sink = sink ?? NoOpSink.Instance,
        Options = new ClaudeAgentOptions(
            Model: "opus",
            SystemPrompt: Prompts.Load("refactorer")),
    };
}
```

Drop the system prompt as `agents/prompts/refactorer.md` (embedded resource — picked up automatically by `NamedAgents.csproj`).

Flow usage:

```csharp
#:project ../agents/NamedAgents.csproj
using Flows.Agents;
// ...
var agent = Refactorer.Create(sink);
```

---

## 9. Per-call provider tweaks

`ClaudeAgentOptions` / `CodexAgentOptions` are records — override only what you need:

```csharp
new ClaudeAgent
{
    Name = "tight-claude",
    Sink = sink,
    Options = new ClaudeAgentOptions(
        IdleThresholdMs: 12_000,   // wait longer for slow responses
        PermissionMode: "default",  // require approval per tool call
        Model: "haiku"),
};
```

Defaults are in [`ClaudeAgentOptions.cs`](../RemoteAgents/Agents/ClaudeAgentOptions.cs) and [`CodexAgentOptions.cs`](../RemoteAgents/Agents/CodexAgentOptions.cs).

---

## 10. Subclassing a provider

When Claude's trust-dialog wording changes (it has, twice), or your project needs a different idle-completion signal, subclass:

```csharp
public sealed class GameDevClaude : ClaudeAgent
{
    protected override string? DetectStartupDialog(string buf)
    {
        // Claude v2.4 reworded "trust this folder" — recognize the new wording too.
        var plain = RemoteAgents.Pty.AnsiHelpers.StripAnsi(buf);
        if (plain.Contains("Do you trust this directory?", StringComparison.Ordinal))
            return "trust";
        return base.DetectStartupDialog(buf);
    }
}
```

Only `DetectStartupDialog` and `IsResponseComplete` are `virtual` in v1. Others are `private` until a real subclass need shows up.

---

## 11. Common edits

| You want to… | Edit… |
|---|---|
| Add a project | `<repo>/projects.json` |
| Add a flow | `flows/<name>.cs` |
| Add a validator | `validation/<MyProject>Validator.cs` |
| Tune Claude timings | `flows/<your-flow>.cs` (pass `ClaudeAgentOptions`) |
| Change a named agent's prompt | `agents/prompts/<name>.md` |
| Add a new sink | `RemoteAgents/Events/<Name>Sink.cs` implementing `IEventSink` |

Anything else, see [`architecture.md`](architecture.md).

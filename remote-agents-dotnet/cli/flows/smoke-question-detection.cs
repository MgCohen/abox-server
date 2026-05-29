#:project ../../src/RemoteAgents/RemoteAgents.csproj
// End-to-end hook-integration smoke for both providers. Table-driven:
// each row in `Passes` is one (provider × mode × prompt) combination.
// After each run the flow dumps:
//
//   - AgentResult.Status / Question / FailureReason
//   - every line written to <sessionDir>/hooks-<label>.jsonl
//   - what the provider's parser made of each line
//
// Two things this characterizes that unit tests can't:
//
//   1. install/shim/env-var/parser chain actually fires against real
//      claude and codex (hooks.jsonl non-empty after a turn)
//   2. how each provider's Stop payload behaves on (a) a trivial prompt
//      it can answer and (b) an ambiguous prompt with no context — under
//      the NonInteractive directive, does the model actually emit the
//      <<NEEDS_INPUT>> sentinel, or does it just guess?
//
// Run:  dotnet run flows/smoke-question-detection.cs

using System.Text.Json;
using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string PROMPT_PONG  = "Reply with exactly the word PONG and nothing else.";
const string PROMPT_AMBIG = "What time is the meeting?";
const string WORK_DIR     = @"C:\Unity\dotnet-pty-smoke\stage2-work";

Directory.CreateDirectory(WORK_DIR);
await SubscriptionGuard.CheckAsync();

var orchestratorRoot = RepoRoot.Find("RemoteAgents.slnx")
    ?? throw new InvalidOperationException("orchestrator root not found");
var shimPath = Path.Combine(orchestratorRoot, "scripts", "hookshim.ps1");
if (!File.Exists(shimPath))
    throw new InvalidOperationException($"hookshim.ps1 missing at {shimPath}");

var session = Session.Start(new StartSessionRequest(
    ProjectDir: WORK_DIR,
    ProjectName: "smoke",
    UserPrompt: "(multiple — see passes below)",
    FlowName: "smoke-question-detection"));

Console.WriteLine($"[smoke] session dir: {session.Dir}");
Console.WriteLine($"[smoke] shim path:   {shimPath}");
Console.WriteLine();

var passes = new (string Provider, InteractionMode Mode, string Prompt, string Label)[]
{
    ("claude", InteractionMode.Interactive,    PROMPT_PONG,  "claude-int-pong"),
    ("claude", InteractionMode.NonInteractive, PROMPT_PONG,  "claude-nonint-pong"),
    ("claude", InteractionMode.NonInteractive, PROMPT_AMBIG, "claude-nonint-ambig"),
    ("codex",  InteractionMode.NonInteractive, PROMPT_PONG,  "codex-nonint-pong"),
    ("codex",  InteractionMode.NonInteractive, PROMPT_AMBIG, "codex-nonint-ambig"),
};

foreach (var p in passes)
    await RunPass(p.Provider, p.Mode, p.Prompt, p.Label);

session.End("ok");
Environment.ExitCode = 0;

async Task RunPass(string provider, InteractionMode mode, string prompt, string label)
{
    Console.WriteLine($"━━━ {label} — {provider} × {mode} ━━━");
    Console.WriteLine($"[{label}] prompt: {prompt}");

    var hooksJsonl = Path.Combine(session.Dir, $"hooks-{label}.jsonl");
    File.WriteAllText(hooksJsonl, "");
    var hooks = new HookIntegrationOptions(hooksJsonl, shimPath);

    var sink = new CompositeSink(
        new ConsoleSink(),
        new JsonlSink(session.TranscriptFile));

    Agent agent = provider switch
    {
        "claude" => new ClaudeAgent
        {
            Name    = "claude",
            Sink    = sink,
            Options = new ClaudeAgentOptions(LaunchSettleIdleMs: 2000, Hooks: hooks),
        },
        "codex" => new CodexAgent
        {
            Name    = "codex",
            Sink    = sink,
            Options = new CodexAgentOptions(JsonStreamTimeoutMs: 5 * 60_000, Hooks: hooks),
        },
        _ => throw new InvalidOperationException($"unknown provider {provider}"),
    };

    IAgentHookParser parser = provider == "claude"
        ? new ClaudeHookParser()
        : new CodexHookParser();

    AgentResult result;
    try
    {
        result = await agent.RunAsync(new AgentRunRequest(
            Prompt: prompt,
            SessionId: null,
            ProjectDir: WORK_DIR,
            Mode: mode));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{label}] EXCEPTION: {ex.Message}");
        Console.WriteLine();
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"[{label}] exitCode      = {result.ExitCode}");
    Console.WriteLine($"[{label}] status        = {result.Status}");
    Console.WriteLine($"[{label}] question      = {result.Question?.Source ?? "(none)"}");
    if (result.Question is not null)
    {
        Console.WriteLine($"[{label}] question.text = {Truncate(result.Question.Text, 240)}");
        if (result.Question is AgentQuestion.OpenQuestion oq)
            Console.WriteLine($"[{label}] fromSentinel  = {oq.FromSentinel}");
        if (result.Question is AgentQuestion.TuiPrompt tp)
            Console.WriteLine($"[{label}] tool          = {tp.ToolName}");
    }
    if (result.FailureReason is not null)
        Console.WriteLine($"[{label}] failureReason = {Truncate(result.FailureReason, 240)}");
    Console.WriteLine($"[{label}] result.text   = {Truncate(result.Text, 240)}");

    DumpHooksJsonl(label, hooksJsonl, parser);
    Console.WriteLine();
}

void DumpHooksJsonl(string label, string path, IAgentHookParser parser)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"[{label}] hooks.jsonl  = (file not created — shim never ran)");
        return;
    }
    var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
    Console.WriteLine($"[{label}] hooks.jsonl  = {lines.Length} line(s)");

    for (int i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        try
        {
            using var doc = JsonDocument.Parse(line);
            var src    = doc.RootElement.TryGetProperty("source", out var s) ? s.GetString() : null;
            var parsed = parser.TryParse(doc.RootElement);
            Console.WriteLine($"[{label}]   [{i}] source = {src,-32} parsed = {(parsed?.GetType().Name ?? "(null)")}");

            // For Stop events, surface the actual assistant message so we
            // can see what the model produced under the directive.
            if (doc.RootElement.TryGetProperty("payload", out var payload) &&
                payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("last_assistant_message", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
            {
                Console.WriteLine($"[{label}]       last_assistant_message = {Truncate(msg.GetString() ?? "", 240)}");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[{label}]   [{i}] (JSON parse failed: {ex.Message})");
        }
    }
}

static string Truncate(string s, int max)
{
    if (string.IsNullOrEmpty(s)) return "";
    s = s.Replace("\r", "").Replace("\n", " | ");
    return s.Length <= max ? s : s[..max] + "…";
}

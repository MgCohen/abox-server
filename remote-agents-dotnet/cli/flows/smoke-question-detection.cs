#:project ../../src/RemoteAgents/RemoteAgents.csproj
// Step-3+4 smoke: wires the hook install/shim/parser path end-to-end
// against a real claude run. Two passes, same simple "PONG" prompt:
//
//   pass 1 — InteractionMode.Interactive    (no directive injected)
//   pass 2 — InteractionMode.NonInteractive (directive forces sentinel)
//
// After each pass dumps:
//   - AgentResult.Status / Question / FailureReason
//   - every line written to <sessionDir>/hooks.jsonl
//   - what the provider's parser made of each line
//
// Purpose is characterization, not pass/fail. The two main things this
// surfaces:
//   (a) does the install/shim/env-var chain actually fire? hooks.jsonl
//       being non-empty after the run answers that.
//   (b) what does claude's idle_prompt payload look like, and does it
//       fire on EVERY clean turn end (in which case Interactive runs
//       would always read NeedsInput) or only when claude explicitly
//       asks something? The research note is ambiguous; this answers it.
//
// Run:  dotnet run flows/smoke-question-detection.cs

using System.Text.Json;
using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string PROMPT = "Reply with exactly the word PONG and nothing else.";
const string WORK_DIR = @"C:\Unity\dotnet-pty-smoke\stage2-work";

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
    UserPrompt: PROMPT,
    FlowName: "smoke-question-detection"));

Console.WriteLine($"[smoke] session dir: {session.Dir}");
Console.WriteLine($"[smoke] shim path:   {shimPath}");
Console.WriteLine();

await RunPass(InteractionMode.Interactive,    "pass 1 — Interactive");
await RunPass(InteractionMode.NonInteractive, "pass 2 — NonInteractive");

session.End("ok");
Environment.ExitCode = 0;

async Task RunPass(InteractionMode mode, string label)
{
    Console.WriteLine($"━━━ {label} ━━━");

    var hooksJsonl = Path.Combine(session.Dir, $"hooks-{(mode == InteractionMode.Interactive ? "interactive" : "non-interactive")}.jsonl");
    File.WriteAllText(hooksJsonl, "");

    var sink = new CompositeSink(
        new ConsoleSink(),
        new JsonlSink(session.TranscriptFile));

    var agent = new ClaudeAgent
    {
        Name = "claude",
        Sink = sink,
        Options = new ClaudeAgentOptions(
            InitialDwellMs: 4000,
            Hooks: new HookIntegrationOptions(hooksJsonl, shimPath)),
    };

    AgentResult? result;
    try
    {
        result = await agent.RunAsync(new AgentRunRequest(
            Prompt: PROMPT,
            SessionId: null,
            ProjectDir: WORK_DIR,
            Mode: mode));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{label}] EXCEPTION: {ex.Message}");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"[{label}] exitCode      = {result.ExitCode}");
    Console.WriteLine($"[{label}] status        = {result.Status}");
    Console.WriteLine($"[{label}] question      = {result.Question?.Source ?? "(none)"}");
    if (result.Question is not null)
    {
        Console.WriteLine($"[{label}] question.text = {Truncate(result.Question.Text, 200)}");
        if (result.Question is AgentQuestion.TuiPrompt tp)
            Console.WriteLine($"[{label}] tool          = {tp.ToolName}");
    }
    if (result.FailureReason is not null)
        Console.WriteLine($"[{label}] failureReason = {Truncate(result.FailureReason, 200)}");

    DumpHooksJsonl(label, hooksJsonl, mode);
    Console.WriteLine();
}

void DumpHooksJsonl(string label, string path, InteractionMode mode)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"[{label}] hooks.jsonl  = (file not created — shim never ran)");
        return;
    }
    var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
    Console.WriteLine($"[{label}] hooks.jsonl  = {lines.Length} line(s) at {path}");

    var parser = new ClaudeHookParser();
    for (int i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        Console.WriteLine($"[{label}]   [{i}] {Truncate(line, 240)}");

        try
        {
            using var doc = JsonDocument.Parse(line);
            var src = doc.RootElement.TryGetProperty("source", out var s) ? s.GetString() : null;
            var parsed = parser.TryParse(doc.RootElement);
            Console.WriteLine($"[{label}]       source = {src,-32}   parsed = {(parsed?.GetType().Name ?? "(null)")}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[{label}]       (JSON parse failed: {ex.Message})");
        }
    }
}

static string Truncate(string s, int max)
{
    if (string.IsNullOrEmpty(s)) return "";
    s = s.Replace("\r", "").Replace("\n", " | ");
    return s.Length <= max ? s : s[..max] + "…";
}

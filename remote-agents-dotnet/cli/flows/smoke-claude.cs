#:project ../../src/RemoteAgents/RemoteAgents.csproj
// One-shot smoke for ClaudeAgent — reproduces the dotnet-pty-smoke
// acceptance (--session-id honored, PONG round-trips, exit 0,
// subscription billing intact).
//
// Run:  dotnet run flows/smoke-claude.cs
//
// Uses C:\Unity\dotnet-pty-smoke\stage2-work as the project dir to keep
// noise out of any real repo. Writes a session under
// remote-agents-dotnet/sessions/.

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string PROMPT = "Reply with exactly the word PONG and nothing else.";
const string WORK_DIR = @"C:\Unity\dotnet-pty-smoke\stage2-work";

Directory.CreateDirectory(WORK_DIR);

await SubscriptionGuard.CheckAsync();

var session = Session.Start(new StartSessionRequest(
    ProjectDir: WORK_DIR,
    ProjectName: "smoke",
    UserPrompt: PROMPT,
    FlowName: "smoke-claude"));

Console.WriteLine($"[smoke] session dir: {session.Dir}");

var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile));

var agent = new ClaudeAgent
{
    Name = "claude",
    Sink = sink,
    Options = new ClaudeAgentOptions(LaunchSettleIdleMs: 2000),
};

try
{
    // SessionId: null → fresh session, agent generates a UUID and passes
    // it via --session-id. (Passing a non-null SessionId triggers --resume.)
    var result = await agent.RunAsync(new AgentRunRequest(
        Prompt: PROMPT,
        SessionId: null,
        ProjectDir: WORK_DIR));
    var sessionId = result.SessionId;

    Console.WriteLine();
    Console.WriteLine($"[smoke] exitCode={result.ExitCode}");
    Console.WriteLine($"[smoke] sessionId={result.SessionId}");
    Console.WriteLine($"[smoke] raw output: {result.RawOutput.Length} chars");

    var sawPong = result.RawOutput.Contains("PONG", StringComparison.OrdinalIgnoreCase);

    // Verify claude wrote its own session JSONL with our UUID.
    var claudeSessionsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");
    var sessionFile = Directory.Exists(claudeSessionsRoot)
        ? Directory.EnumerateFiles(claudeSessionsRoot, "*.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).StartsWith(sessionId, StringComparison.OrdinalIgnoreCase))
        : null;

    Console.WriteLine($"[smoke] sawPong={sawPong}");
    Console.WriteLine($"[smoke] claude session file: {sessionFile ?? "(NOT FOUND)"}");

    session.End(sawPong && sessionFile is not null ? "ok" : "failed");
    Environment.ExitCode = sawPong && sessionFile is not null ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[smoke] EXCEPTION: {ex}");
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}

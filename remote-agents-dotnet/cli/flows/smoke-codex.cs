#:project ../../src/RemoteAgents/RemoteAgents.csproj
// One-shot smoke for CodexAgent — runs `codex exec` against a fixed
// review prompt and asserts the session id round-trips + the final
// message is parseable.
//
// Run:  dotnet run flows/smoke-codex.cs

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string WORK_DIR = @"C:\Unity\dotnet-pty-smoke\stage2-work";
const string FIXED_DIFF = """
diff --git a/foo.cs b/foo.cs
index 1111..2222 100644
--- a/foo.cs
+++ b/foo.cs
@@ -1,3 +1,3 @@
 public class Foo {
-    public int Bar() { return 1; }
+    public int Bar() { return 2; }
 }
""";

const string PROMPT = """
You are reviewing a one-line change. Reply with exactly one word: APPROVE
or REJECT. Nothing else. No prose. No JSON.

DIFF:
""" + FIXED_DIFF;

Directory.CreateDirectory(WORK_DIR);
await SubscriptionGuard.CheckAsync();

var session = Session.Start(new StartSessionRequest(
    ProjectDir: WORK_DIR,
    ProjectName: "smoke",
    UserPrompt: PROMPT,
    FlowName: "smoke-codex"));

Console.WriteLine($"[smoke] session dir: {session.Dir}");

var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile));

var agent = new CodexAgent
{
    Name = "codex",
    Sink = sink,
    Options = new CodexAgentOptions(JsonStreamTimeoutMs: 5 * 60_000),
};

try
{
    var result = await agent.RunAsync(new AgentRunRequest(
        Prompt: PROMPT,
        SessionId: null,
        ProjectDir: WORK_DIR));

    Console.WriteLine();
    Console.WriteLine($"[smoke] exitCode={result.ExitCode}");
    Console.WriteLine($"[smoke] sessionId='{result.SessionId}'");
    Console.WriteLine($"[smoke] text length={result.Text.Length}");
    Console.WriteLine($"[smoke] text preview: {result.Text.Trim().Replace("\n", " \\n ")}");

    var verdict = result.Text.Contains("APPROVE", StringComparison.OrdinalIgnoreCase) ? "approve"
                : result.Text.Contains("REJECT", StringComparison.OrdinalIgnoreCase)  ? "reject"
                : "unparseable";
    var sessionRoundTripped = !string.IsNullOrEmpty(result.SessionId);

    Console.WriteLine($"[smoke] verdict={verdict}");
    Console.WriteLine($"[smoke] sessionRoundTripped={sessionRoundTripped}");

    var ok = sessionRoundTripped && verdict != "unparseable";
    session.End(ok ? "ok" : "failed");
    Environment.ExitCode = ok ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[smoke] EXCEPTION: {ex}");
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}

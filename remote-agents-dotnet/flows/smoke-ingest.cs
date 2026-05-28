#:project ../RemoteAgents/RemoteAgents.csproj
// Step-7 smoke: ProviderJsonlIngestSink copies the provider's own session
// JSONL into the orchestrator session folder. Runs one Claude turn and
// one Codex turn, then asserts:
//   sessions/<id>/claude-turn-1.jsonl  exists and is non-empty
//   sessions/<id>/codex-turn-1.jsonl   exists and is non-empty
//   sessions/<id>/transcript.jsonl     also exists (live event sink)

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string WORK_DIR = @"C:\Unity\dotnet-pty-smoke\stage2-work";
Directory.CreateDirectory(WORK_DIR);

await SubscriptionGuard.CheckAsync();

var session = Session.Start(new StartSessionRequest(
    ProjectDir: WORK_DIR,
    ProjectName: "smoke",
    UserPrompt: "ingest smoke",
    FlowName: "smoke-ingest"));

Console.WriteLine($"[smoke] session dir: {session.Dir}");

var ingest = new ProviderJsonlIngestSink(session.Dir, WORK_DIR);
var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile),
    ingest);

// --- Claude turn ---
var claude = new ClaudeAgent { Name = "claude", Sink = sink, Options = new ClaudeAgentOptions(InitialDwellMs: 4000) };
var c1 = await claude.RunAsync(new AgentRunRequest("Reply with exactly the word PONG.", null, WORK_DIR));
Console.WriteLine($"[smoke] claude session={c1.SessionId} exit={c1.ExitCode}");

// --- Codex turn ---
var codex = new CodexAgent { Name = "codex", Sink = sink, Options = new CodexAgentOptions(JsonStreamTimeoutMs: 5 * 60_000) };
var c2 = await codex.RunAsync(new AgentRunRequest("Reply with exactly the word OK.", null, WORK_DIR));
Console.WriteLine($"[smoke] codex session={c2.SessionId} exit={c2.ExitCode}");

// --- Verification ---
var claudeTurn = Path.Combine(session.Dir, "claude-turn-1.jsonl");
var codexTurn  = Path.Combine(session.Dir, "codex-turn-1.jsonl");

var claudeOk = File.Exists(claudeTurn) && new FileInfo(claudeTurn).Length > 0;
var codexOk  = File.Exists(codexTurn)  && new FileInfo(codexTurn).Length > 0;
var transcriptOk = new FileInfo(session.TranscriptFile).Length > 0;

Console.WriteLine();
Console.WriteLine($"[smoke] claude-turn-1.jsonl     exists/non-empty: {claudeOk} ({(File.Exists(claudeTurn) ? new FileInfo(claudeTurn).Length : 0)} bytes)");
Console.WriteLine($"[smoke] codex-turn-1.jsonl      exists/non-empty: {codexOk}  ({(File.Exists(codexTurn) ? new FileInfo(codexTurn).Length : 0)} bytes)");
Console.WriteLine($"[smoke] transcript.jsonl        non-empty:        {transcriptOk}");

var ok = claudeOk && codexOk && transcriptOk;
session.End(ok ? "ok" : "failed");
Environment.ExitCode = ok ? 0 : 1;

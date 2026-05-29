using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using RemoteAgents.Events;
using RemoteAgents.Host.Sinks;
using RemoteAgents.Runs;

namespace RemoteAgents.Host.Runs;

// Today's transport: dotnet run cli/agents-dotnet.cs as a child process,
// regex-sniff the session-id line, tail <sessionDir>/transcript.jsonl,
// rehydrate AgentEvents into the run's ChannelSink. Also starts the
// legacy ClaudeJsonlTailer for ChatEvent emission — that path dies in
// step 4 once the in-process executor is the default.
//
// Extracted from Host/Runs/FlowRunner.cs as part of Phase 6 step 2 with
// no semantic change. CanHandle returns true unconditionally — this is
// the fallback when InProcessFlowExecutor can't resolve the flow name.
public sealed class SubprocessFlowExecutor : IFlowExecutor
{
    private static readonly Regex SessionIdLine = new(
        @"^\[(?<id>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-\d{3}Z-[a-z0-9-]+)\]\s*$",
        RegexOptions.Compiled);

    private readonly string _orchestratorRoot;
    private readonly ILogger<SubprocessFlowExecutor> _log;

    public SubprocessFlowExecutor(string orchestratorRoot, ILogger<SubprocessFlowExecutor> log)
    {
        _orchestratorRoot = orchestratorRoot;
        _log = log;
    }

    public bool CanHandle(string flowName) => true;

    public async Task ExecuteAsync(Run run, CancellationToken ct)
    {
        var psi = BuildProcessStartInfo(run);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        run.Status = RunStatus.Running;

        // The chat-content events the Host used to tail Claude's own JSONL
        // for (assistant text / thinking / tool use) now ride out through
        // the subprocess's transcript.jsonl as AgentEvent variants — the
        // lib-side ClaudeJsonlEmitter inside ClaudeAgent.DriveAsync emits
        // them, JsonlSink writes them, TailTranscriptAsync below
        // deserialises them, and the broadcaster forwards them to clients.
        // No separate Host-owned tailer needed.

        var stdoutTask = ReadStdoutAsync(proc, run);
        var stderrTask = ReadStderrAsync(proc, run);

        using var killReg = run.Cts.Token.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch (Exception ex) { _log.LogWarning(ex, "Kill failed for run {RunId}", run.Id); }
        });

        await proc.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask);

        run.ExitCode = proc.ExitCode;
        run.EndedAt = DateTimeOffset.UtcNow;

        // EndedAt is now set — the tailer's "is the run done?" check can
        // return true on its next loop iteration. Await so we don't
        // promote-to-history with the tail still in flight.
        if (run.TailerTask is not null)
        {
            try { await run.TailerTask; }
            catch (Exception ex) { _log.LogWarning(ex, "Transcript tailer fault for run {RunId}", run.Id); }
        }

        run.Status = run.Cts.IsCancellationRequested
            ? RunStatus.Canceled
            : proc.ExitCode == 0 ? RunStatus.Completed : RunStatus.Failed;
    }

    private ProcessStartInfo BuildProcessStartInfo(Run run)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _orchestratorRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(Path.Combine(_orchestratorRoot, "cli", "agents-dotnet.cs"));
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(run.Flow);
        psi.ArgumentList.Add(run.Project);
        psi.ArgumentList.Add(run.Prompt);
        foreach (var a in run.Args) psi.ArgumentList.Add(a);

        // Belt-and-suspenders: explicitly blank any API-key envs the parent
        // service might have inherited. Library's SubscriptionGuard would
        // refuse if set, but better to also strip at boundary.
        psi.Environment["ANTHROPIC_API_KEY"] = "";
        psi.Environment["CLAUDE_API_KEY"] = "";
        psi.Environment["OPENAI_API_KEY"] = "";

        return psi;
    }

    private async Task ReadStdoutAsync(Process proc, Run run)
    {
        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync(run.Cts.Token)) is not null)
        {
            if (run.SessionId is null)
            {
                var m = SessionIdLine.Match(line);
                if (m.Success)
                {
                    run.SessionId = m.Groups["id"].Value;
                    run.SessionDir = Path.Combine(_orchestratorRoot, "sessions", run.SessionId);
                    run.TailerTask = Task.Run(() => TailTranscriptAsync(run));
                    _log.LogInformation("Run {RunId} bound to session {SessionId}", run.Id, run.SessionId);
                }
            }
        }
    }

    private async Task ReadStderrAsync(Process proc, Run run)
    {
        string? line;
        while ((line = await proc.StandardError.ReadLineAsync(run.Cts.Token)) is not null)
        {
            _log.LogWarning("[run {RunId} stderr] {Line}", run.Id, line);
        }
    }

    // Opens transcript.jsonl with FileShare.ReadWrite and follows appends
    // in a polling loop. Each line is one AgentEvent JSON object (polymorphic
    // on "kind"). Parsed events are forwarded into the run's ChannelSink.
    private async Task TailTranscriptAsync(Run run)
    {
        if (run.SessionDir is null) return;
        var path = Path.Combine(run.SessionDir, "transcript.jsonl");

        for (var i = 0; i < 40 && !File.Exists(path); i++)
            await Task.Delay(50, run.Cts.Token);
        if (!File.Exists(path)) return;

        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);

        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        while (true)
        {
            var line = await reader.ReadLineAsync(run.Cts.Token);
            if (line is null)
            {
                if (run.EndedAt is not null) return;
                await Task.Delay(50, run.Cts.Token);
                continue;
            }
            if (line.Length == 0) continue;

            AgentEvent? evt = null;
            try { evt = JsonSerializer.Deserialize<AgentEvent>(line, jsonOpts); }
            catch (JsonException jex)
            {
                _log.LogWarning(jex, "Bad transcript line for run {RunId}: {Line}", run.Id, line);
            }
            if (evt is not null)
            {
                try { await run.Sink.EmitAsync(evt, run.Cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}

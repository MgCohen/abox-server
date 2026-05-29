using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using RemoteAgents.Events;
using RemoteAgents.Host.Hubs;
using RemoteAgents.Host.Sinks;
using RemoteAgents.Primitives;

namespace RemoteAgents.Host.Runs;

// Spawns flows as child processes (mirrors what cli/agents-dotnet.cs does
// today), sniffs the session ID from stdout, then tails the per-session
// transcript.jsonl and re-emits each line as an AgentEvent into the run's
// ChannelSink. The Host doesn't import flow code or instantiate Agents
// directly in v1 — keeps the Host fully additive on top of the existing
// CLI execution shape.
public sealed class FlowRunner
{
    private static readonly Regex SessionIdLine = new(
        @"^\[(?<id>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-\d{3}Z-[a-z0-9-]+)\]\s*$",
        RegexOptions.Compiled);

    private readonly RunRegistry _registry;
    private readonly RunStore _store;
    private readonly ILogger<FlowRunner> _log;
    private readonly string _orchestratorRoot;

    public FlowRunner(RunRegistry registry, RunStore store, ILogger<FlowRunner> log, IConfiguration config)
    {
        _registry = registry;
        _store = store;
        _log = log;
        _orchestratorRoot = ResolveOrchestratorRoot(config)
            ?? throw new InvalidOperationException(
                "Could not locate remote-agents-dotnet/ — set RemoteAgents:OrchestratorRoot in appsettings.json or run from inside the repo.");
    }

    public string OrchestratorRoot => _orchestratorRoot;

    public Run Start(string project, string flow, string prompt, string[] args)
    {
        var run = new Run
        {
            Id = Guid.NewGuid(),
            Project = project,
            Flow = flow,
            Prompt = prompt,
            Args = args,
            StartedAt = DateTimeOffset.UtcNow,
            Sink = new ChannelSink(),
            Chat = new ChatChannel(),
            Cts = new CancellationTokenSource(),
            Status = RunStatus.Starting,
        };

        // Resolve the absolute project dir so the JSONL tailer can find
        // Claude's session file. Best-effort — if resolution fails the
        // tailer just no-ops.
        try { run.ProjectDir = ProjectRegistry.Resolve(project); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not resolve project {Project}", project); }

        _registry.Register(run);

        // Detached: the run lifecycle continues after StartAsync returns.
        _ = Task.Run(() => ExecuteAsync(run));

        return run;
    }

    public bool Cancel(Guid runId)
    {
        var run = _registry.Get(runId);
        if (run is null) return false;
        if (run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Canceled) return false;
        run.Cts.Cancel();
        return true;
    }

    private async Task ExecuteAsync(Run run)
    {
        try
        {
            var psi = BuildProcessStartInfo(run);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start dotnet process.");

            run.Status = RunStatus.Running;

            // Start the Claude JSONL tailer immediately — it watches the
            // projects/<encoded>/ dir for the new .jsonl file Claude
            // creates, no UUID handshake needed.
            if (run.ProjectDir is not null)
            {
                var chatTailer = new ClaudeJsonlTailer(run, _log);
                run.ChatTailerTask = Task.Run(() => chatTailer.RunAsync(run.Cts.Token));
            }

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

            // EndedAt is now set — the tailer's "is the run done?" check
            // can return true on its next loop iteration. Await both so we
            // don't promote-to-history with tails still in flight.
            if (run.TailerTask is not null)
            {
                try { await run.TailerTask; }
                catch (Exception ex) { _log.LogWarning(ex, "Transcript tailer fault for run {RunId}", run.Id); }
            }
            if (run.ChatTailerTask is not null)
            {
                try { await run.ChatTailerTask; }
                catch (Exception ex) { _log.LogWarning(ex, "Chat tailer fault for run {RunId}", run.Id); }
            }

            run.Status = run.Cts.IsCancellationRequested
                ? RunStatus.Canceled
                : proc.ExitCode == 0 ? RunStatus.Completed : RunStatus.Failed;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Run {RunId} crashed in FlowRunner", run.Id);
            run.Status = RunStatus.Failed;
            run.FailureReason = ex.Message;
            run.EndedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            run.Sink.Complete();
            run.Chat.Complete();
            try
            {
                _registry.PromoteToHistory(run);
                await _store.SaveAsync(_registry.HistorySnapshot());
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to persist run {RunId}", run.Id);
            }
        }
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
            // Console output beyond the session-id line is informational —
            // the structured events come from the transcript tailer, not stdout.
        }
        // Tailer is NOT awaited here — ExecuteAsync awaits it after EndedAt
        // is set so the tailer's exit condition can actually fire.
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

        // Wait a moment for the file to exist if the flow hasn't reached
        // JsonlSink construction yet.
        for (var i = 0; i < 40 && !File.Exists(path); i++)
        {
            await Task.Delay(50, run.Cts.Token);
        }
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
                // No more data right now. If the child process has exited,
                // do one final drain pass then stop.
                if (run.EndedAt is not null) return;
                await Task.Delay(50, run.Cts.Token);
                continue;
            }
            if (line.Length == 0) continue;

            AgentEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<AgentEvent>(line, jsonOpts);
            }
            catch (JsonException jex)
            {
                _log.LogWarning(jex, "Bad transcript line for run {RunId}: {Line}", run.Id, line);
            }
            if (evt is not null)
            {
                try
                {
                    await run.Sink.EmitAsync(evt, run.Cts.Token);
                }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private static string? ResolveOrchestratorRoot(IConfiguration config)
    {
        // Explicit override wins.
        var fromConfig = config["RemoteAgents:OrchestratorRoot"];
        if (!string.IsNullOrWhiteSpace(fromConfig) && Directory.Exists(fromConfig))
            return Path.GetFullPath(fromConfig);

        // Walk up looking for RemoteAgents.slnx (the orchestrator's marker).
        var slnxOwner = RepoRoot.Find("RemoteAgents.slnx");
        if (slnxOwner is not null) return slnxOwner;

        // Fallback: repo root containing remote-agents-dotnet/.
        var repoRoot = RepoRoot.Find("remote-agents-dotnet");
        return repoRoot is null ? null : Path.Combine(repoRoot, "remote-agents-dotnet");
    }
}

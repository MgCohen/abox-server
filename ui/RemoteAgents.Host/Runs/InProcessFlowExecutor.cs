using RemoteAgents.Events;
using RemoteAgents.Flows;
using RemoteAgents.Host.Sinks;
using RemoteAgents.Hosting;
using RemoteAgents.Runs;
using RemoteAgents.Sessions;

namespace RemoteAgents.Host.Runs;

// Drive IFlow.RunAsync directly on the Host's event loop. No subprocess,
// no stdout regex, no transcript tailer — the run's ChannelSink is folded
// into the FlowContext's CompositeSink so every AgentEvent the lib emits
// lands on the same broadcaster the SignalR clients subscribe to.
//
// CanHandle is keyed on FlowCatalog.Get(name); flows not registered in
// the Host's process fall back to SubprocessFlowExecutor.
public sealed class InProcessFlowExecutor : IFlowExecutor
{
    private readonly FlowCatalog _flows;
    private readonly ILogger<InProcessFlowExecutor> _log;

    public InProcessFlowExecutor(FlowCatalog flows, ILogger<InProcessFlowExecutor> log)
    {
        _flows = flows;
        _log = log;
    }

    public bool CanHandle(string flowName) => _flows.Get(flowName) is not null;

    public async Task ExecuteAsync(Run run, CancellationToken ct)
    {
        if (run.ProjectDir is null)
            throw new InvalidOperationException(
                $"Run {run.Id}: ProjectDir is null; in-process executor needs a resolved project.");

        var flow = _flows.Get(run.Flow)
            ?? throw new InvalidOperationException(
                $"Run {run.Id}: flow '{run.Flow}' not registered with FlowCatalog. " +
                "Either register it in Program.cs or accept the subprocess fallback.");

        await using var ctx = await FlowBootstrap.StartInProcessAsync(
            flowName:     flow.Name,
            projectName:  run.Project,
            projectDir:   run.ProjectDir,
            userPrompt:   run.Prompt,
            shouldPush:   run.Args.Contains("--push"),
            injectedSinks: [run.Sink],
            ct:           ct);

        run.SessionId  = ctx.Session.Id;
        run.SessionDir = ctx.Session.Dir;
        run.Status     = RunStatus.Running;
        _log.LogInformation("Run {RunId} in-process bound to session {SessionId}", run.Id, run.SessionId);

        try
        {
            var flowArgs = new FlowArgs(run.Project, run.Prompt, run.Args, ctx.ShouldPush);
            var result = await new RemoteAgents.Flows.FlowRunner().RunAsync(flow, ctx, flowArgs, run.Cts.Token);
            run.ExitCode = RemoteAgents.Flows.FlowRunner.MapToExitCode(result.Reason);
            run.FailureReason = result.Detail;
            run.EndedAt = DateTimeOffset.UtcNow;
            run.Status = run.Cts.IsCancellationRequested
                ? RunStatus.Canceled
                : result.Reason switch
                {
                    SessionResult.Shipped or SessionResult.Ok or SessionResult.NoChanges => RunStatus.Completed,
                    _ => RunStatus.Failed,
                };
        }
        catch (Exception ex) when (run.Cts.IsCancellationRequested)
        {
            _log.LogInformation(ex, "Run {RunId} canceled mid-flight", run.Id);
            run.Status = RunStatus.Canceled;
            run.EndedAt = DateTimeOffset.UtcNow;
        }
    }
}

using RemoteAgents.Host.Sinks;
using RemoteAgents.Primitives;
using RemoteAgents.Runs;

namespace RemoteAgents.Host.Runs;

// Owner of the Run lifecycle: registers the Run, picks an executor by
// CanHandle priority, drives it on a detached task, persists on
// completion. The transport choice (in-process vs subprocess) is
// pluggable — see IFlowExecutor implementations.
public sealed class FlowRunner
{
    private readonly RunRegistry _registry;
    private readonly RunStore _store;
    private readonly IReadOnlyList<IFlowExecutor> _executors;
    private readonly ILogger<FlowRunner> _log;
    private readonly string _orchestratorRoot;

    public FlowRunner(
        RunRegistry registry,
        RunStore store,
        IEnumerable<IFlowExecutor> executors,
        ILogger<FlowRunner> log)
    {
        _registry = registry;
        _store = store;
        _executors = executors.ToArray();
        _log = log;
        _orchestratorRoot = OrchestratorPaths.FindOrThrow();
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
            Cts = new CancellationTokenSource(),
            Status = RunStatus.Starting,
        };

        // Resolve project dir up front — in-process needs it.
        try { run.ProjectDir = ProjectRegistry.Resolve(project); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not resolve project {Project}", project); }

        _registry.Register(run);

        // Observe the run's event stream so AgentEvent.ProviderSessionAttached
        // lands on Run.ProviderSession without the executor needing to know.
        _ = RunStateSink.StartAsync(run, run.Cts.Token);

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
            var executor = _executors.FirstOrDefault(e => e.CanHandle(run.Flow))
                ?? throw new InvalidOperationException(
                    $"No executor accepted flow '{run.Flow}'. Register a fallback IFlowExecutor.");
            _log.LogInformation("Run {RunId} dispatched to {Executor}", run.Id, executor.GetType().Name);
            await executor.ExecuteAsync(run, run.Cts.Token);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Run {RunId} crashed in executor", run.Id);
            run.Status = RunStatus.Failed;
            run.FailureReason = ex.Message;
            run.EndedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            run.Sink.Complete();
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
}

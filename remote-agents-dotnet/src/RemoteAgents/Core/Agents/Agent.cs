using RemoteAgents.Events;

namespace RemoteAgents.Agents;

// Sealed lifecycle wrapper around provider-specific ExecuteAsync. Subclasses
// implement ExecuteAsync; this class owns Started/Completed/Failed emission,
// exception propagation, and the install/uninstall envelope around hooks.
// RunAsync is sealed so subclasses cannot weaken the contract — the only
// extension surface is ExecuteAsync and InstallHooksAsync.
public abstract class Agent
{
    public required string Name { get; init; }
    public IEventSink Sink { get; init; } = NoOpSink.Instance;

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        await Sink.EmitAsync(
            new AgentEvent.Started(DateTimeOffset.UtcNow, Name, req.Prompt, req.SessionId),
            ct);

        IAsyncDisposable? hookScope = await InstallHooksAsync(req, ct);

        AgentResult result;
        try
        {
            try
            {
                result = await ExecuteAsync(req, ct);
            }
            catch (Exception ex)
            {
                // Always emit Failed — use CancellationToken.None so callers
                // get the failure event even when their token has been canceled.
                await Sink.EmitAsync(
                    new AgentEvent.Failed(DateTimeOffset.UtcNow, Name, ex.Message, ex.GetType().Name),
                    CancellationToken.None);
                throw;
            }
        }
        finally
        {
            // Uninstall happens regardless of success/failure/cancellation.
            // CT.None: tearing down hooks must not be skipped by caller cancel.
            if (hookScope is not null) await hookScope.DisposeAsync();
        }

        await Sink.EmitAsync(
            new AgentEvent.Completed(
                DateTimeOffset.UtcNow,
                Name,
                result.SessionId,
                result.ExitCode,
                result.Text.Length),
            ct);

        return result;
    }

    // Override to install provider-specific hooks. The returned disposable
    // is awaited in a finally — providers do not repeat install/try/finally
    // boilerplate in their own ExecuteAsync. Default: no hooks.
    protected virtual ValueTask<IAsyncDisposable?> InstallHooksAsync(
        AgentRunRequest req, CancellationToken ct)
        => ValueTask.FromResult<IAsyncDisposable?>(null);

    protected abstract Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct);
}

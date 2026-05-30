using RemoteAgents.Agents.Hooks;
using RemoteAgents.Events;
using RemoteAgents.Primitives;

namespace RemoteAgents.Agents;

// Sealed lifecycle wrapper around a provider's DriveAsync. The base owns
// everything that is the same for every provider:
//
//   * Started / Completed / Failed event emission + exception propagation
//   * the install/try/finally/uninstall envelope around hooks
//   * hook resolution (hooks.jsonl → Status / Question / FailureReason)
//   * NonInteractiveViolation emission
//   * UnattendedDirective.Compose — composed once, handed to DriveAsync via
//     AgentDriveContext.SystemPrompt; providers never recompose
//   * assembling the final AgentResult
//
// Providers implement DriveAsync — the drive loop body, nothing else —
// expose their typed options via BaseOptions, and opt into hooks by
// returning a non-null HookIntegration. RunAsync is sealed so subclasses
// cannot weaken the contract.
public abstract class Agent : IAgent
{
    // Default name comes from the provider type (passed via the subclass
    // constructor). Callers can still override via object initializer
    // (`new ClaudeAgent { Name = "planner" }`) — init runs after the ctor.
    protected Agent(string defaultName) { Name = defaultName; }

    public string Name { get; init; }
    public IEventSink Sink { get; init; } = NoOpSink.Instance;

    // Provider's typed options viewed as AgentOptions — gives the base
    // generic access to SystemPrompt + Hooks without knowing the
    // provider-specific fields. Defaults to empty so test fakes don't
    // need to wire one up.
    protected virtual AgentOptions BaseOptions => EmptyOptions;
    private static readonly AgentOptions EmptyOptions = new EmptyAgentOptions();
    private sealed record EmptyAgentOptions() : AgentOptions(null, null, null);

    // Provider hook wiring. Null = no hooks: base skips install + resolution
    // and reports Completed. See HookIntegration.
    protected virtual HookIntegration? Hooks => null;

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        await Sink.EmitAsync(
            new AgentEvent.Started(DateTimeOffset.UtcNow, Name, req.Prompt, req.SessionId),
            ct);

        var hooks = Hooks;
        Action? teardown = hooks?.Install(req);

        DriveResult raw;
        try
        {
            try
            {
                var ctx = new AgentDriveContext(
                    Request:        req,
                    SystemPrompt:   UnattendedDirective.Compose(BaseOptions.SystemPrompt, req.Mode),
                    HooksJsonlPath: hooks?.HooksJsonlPath);
                raw = await DriveAsync(ctx, ct);
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
            teardown?.Invoke();
        }

        // Resolve the run's outcome from the provider's hooks.jsonl.
        // Codex's text-only sentinel/heuristic detection writes a synthetic
        // line into hooks.jsonl from inside DriveAsync, so this single path
        // covers both real hook events and provider-detected questions.
        var outcome = hooks is null
            ? HookResolution.Completed
            : HookResolution.FromHooksJsonl(hooks.HooksJsonlPath, hooks.Parser, req.Mode);

        if (outcome.Status == AgentStatus.Failed && outcome.Question is not null)
            await Sink.EmitAsync(new AgentEvent.NonInteractiveViolation(
                DateTimeOffset.UtcNow, Name, outcome.Question.Source, outcome.Question.Text),
                CancellationToken.None);

        var result = new AgentResult(
            Text:          raw.Text,
            SessionId:     raw.SessionId,
            ExitCode:      raw.ExitCode,
            RawOutput:     raw.RawOutput,
            Status:        outcome.Status,
            Question:      outcome.Question,
            FailureReason: outcome.FailureReason);

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

    // The drive loop body — launch the CLI, feed it the prompt, capture
    // the response. Providers return the raw bytes; the base attaches the
    // hook-derived Status / Question. This is the only method a provider
    // must implement.
    protected abstract Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct);
}

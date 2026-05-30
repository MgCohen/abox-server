using RemoteAgents.Agents.Hooks;
using RemoteAgents.Primitives;

namespace RemoteAgents.Agents;

// Sealed lifecycle wrapper around a provider's DriveAsync. The base owns:
//
//   * the install/try/finally/uninstall envelope around hooks
//   * hook resolution (hooks.jsonl → Status / Question / FailureReason)
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
    protected Agent(string defaultName) { Name = defaultName; }

    public string Name { get; init; }

    protected virtual AgentOptions BaseOptions => EmptyOptions;
    private static readonly AgentOptions EmptyOptions = new EmptyAgentOptions();
    private sealed record EmptyAgentOptions() : AgentOptions(null, null, null);

    // Provider hook wiring. Null = no hooks: base skips install + resolution
    // and reports Completed. See HookIntegration.
    protected virtual HookIntegration? Hooks => null;

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        var hooks = Hooks;
        Action? teardown = hooks?.Install(req);

        DriveResult raw;
        try
        {
            var ctx = new AgentDriveContext(
                Request:        req,
                SystemPrompt:   UnattendedDirective.Compose(BaseOptions.SystemPrompt, req.Mode),
                HooksJsonlPath: hooks?.HooksJsonlPath);
            raw = await DriveAsync(ctx, ct);
        }
        finally
        {
            teardown?.Invoke();
        }

        var outcome = hooks is null
            ? HookResolution.Completed
            : HookResolution.FromHooksJsonl(hooks.HooksJsonlPath, hooks.Parser, req.Mode);

        return new AgentResult(
            Text:          raw.Text,
            SessionId:     raw.SessionId,
            ExitCode:      raw.ExitCode,
            RawOutput:     raw.RawOutput,
            Status:        outcome.Status,
            Question:      outcome.Question,
            FailureReason: outcome.FailureReason,
            Transcript:    raw.Transcript);
    }

    protected abstract Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct);
}

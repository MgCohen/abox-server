using RemoteAgents.Events;
using RemoteAgents.Sessions;

namespace RemoteAgents.Flows;

// Wraps IFlow.RunAsync in the uniform error envelope so no flow body
// needs its own try/catch around the whole script. Exceptions become
// FlowResult(Failed, ex.Message); the session is marked failed; the
// sink gets a final phase event.
//
// Distinct from the Host's Host/Runs/FlowRunner.cs (process-spawn
// dispatcher) — that one renames in Layer 6 once the in-process
// executor lands.
public sealed class FlowRunner
{
    public async Task<FlowResult> RunAsync(IFlow flow, FlowContext ctx, FlowArgs args, CancellationToken ct = default)
    {
        try
        {
            var result = await flow.RunAsync(ctx, args, ct);
            ctx.Session.End(MapToSessionResult(result.Reason), result.Detail);
            return result;
        }
        catch (Exception ex)
        {
            await ctx.Sink.PhaseFailAsync(flow.Name, $"unhandled: {ex.Message}", CancellationToken.None);
            ctx.Session.End(SessionResult.Failed, failureReason: ex.Message);
            return new FlowResult(FlowExitReason.Failed, ex.Message);
        }
    }

    // FlowExitReason → SessionResult. Same value set with one rename
    // (BadArgs has no SessionResult counterpart — flow's session never
    // started in that case so this branch isn't reachable here).
    public static SessionResult MapToSessionResult(FlowExitReason reason) => reason switch
    {
        FlowExitReason.Shipped                 => SessionResult.Shipped,
        FlowExitReason.Ok                      => SessionResult.Ok,
        FlowExitReason.NoChanges               => SessionResult.NoChanges,
        FlowExitReason.ValidationFailed        => SessionResult.ValidationFailed,
        FlowExitReason.VerdictUnclear          => SessionResult.VerdictUnclear,
        FlowExitReason.RevisionBrokeValidation => SessionResult.RevisionBrokeValidation,
        FlowExitReason.AbortedDirtyTree        => SessionResult.AbortedDirtyTree,
        FlowExitReason.Failed                  => SessionResult.Failed,
        FlowExitReason.BadArgs                 => SessionResult.Failed,
        _                                      => SessionResult.Failed,
    };

    // CLI dispatcher reads this when mapping FlowResult → process exit code.
    public static int MapToExitCode(FlowExitReason reason) => reason switch
    {
        FlowExitReason.Shipped                 => 0,
        FlowExitReason.Ok                      => 0,
        FlowExitReason.NoChanges               => 0,
        FlowExitReason.ValidationFailed        => 2,
        FlowExitReason.VerdictUnclear          => 2,
        FlowExitReason.RevisionBrokeValidation => 2,
        FlowExitReason.AbortedDirtyTree        => 2,
        FlowExitReason.BadArgs                 => 2,
        FlowExitReason.Failed                  => 1,
        _                                      => 1,
    };
}

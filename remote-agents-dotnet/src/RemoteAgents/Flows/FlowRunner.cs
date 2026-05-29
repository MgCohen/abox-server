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
            ctx.Session.End(result.Reason, result.Detail);
            return result;
        }
        catch (Exception ex)
        {
            await ctx.Sink.PhaseFailAsync(flow.Name, $"unhandled: {ex.Message}", CancellationToken.None);
            ctx.Session.End(SessionResult.Failed, failureReason: ex.Message);
            return new FlowResult(SessionResult.Failed, ex.Message);
        }
    }

    // SessionResult → process exit code. The one place a run's outcome
    // becomes a CLI exit status: Shipped/Ok/NoChanges are success (0),
    // gate failures (validation, unclear verdict, dirty tree) are 2, an
    // unhandled error is 1.
    public static int MapToExitCode(SessionResult reason) => reason switch
    {
        SessionResult.Shipped                 => 0,
        SessionResult.Ok                      => 0,
        SessionResult.NoChanges               => 0,
        SessionResult.ValidationFailed        => 2,
        SessionResult.VerdictUnclear          => 2,
        SessionResult.RevisionBrokeValidation => 2,
        SessionResult.AbortedDirtyTree        => 2,
        SessionResult.Failed                  => 1,
        _                                     => 1,
    };
}

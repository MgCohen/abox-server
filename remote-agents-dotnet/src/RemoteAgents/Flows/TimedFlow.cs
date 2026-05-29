using System.Diagnostics;
using RemoteAgents.Events;

namespace RemoteAgents.Flows;

// Cross-cutting decorator: times the inner flow's RunAsync and emits a
// Phase event with the elapsed milliseconds. Observation only — does
// NOT encode any domain rule (see 99-rejected.md R10).
public sealed class TimedFlow : IFlow
{
    private readonly IFlow _inner;

    public TimedFlow(IFlow inner) { _inner = inner; }

    public string Name => _inner.Name;
    public string? Summary => _inner.Summary;

    public async Task<FlowResult> RunAsync(FlowContext ctx, FlowArgs args, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await ctx.Sink.PhaseStartAsync(_inner.Name, "start", ct);
        try
        {
            var result = await _inner.RunAsync(ctx, args, ct);
            await ctx.Sink.PhaseOkAsync(_inner.Name, $"done in {sw.ElapsedMilliseconds}ms ({result.Reason})", ct);
            return result;
        }
        catch
        {
            await ctx.Sink.PhaseFailAsync(_inner.Name, $"threw after {sw.ElapsedMilliseconds}ms", CancellationToken.None);
            throw;
        }
    }
}

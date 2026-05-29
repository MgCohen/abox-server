namespace RemoteAgents.Events;

// Sugar around Sink.EmitAsync(new AgentEvent.Phase(...)). Keeps flow
// pipelines readable — `await sink.PhaseOkAsync("validate", summary)`
// instead of constructing the event by hand.
public static class EventSinkExtensions
{
    public static Task PhaseStartAsync(this IEventSink sink, string agent, string detail, CancellationToken ct = default)
        => sink.EmitAsync(new AgentEvent.Phase(DateTimeOffset.UtcNow, agent, PhaseStatus.Start, detail), ct);

    public static Task PhaseOkAsync(this IEventSink sink, string agent, string detail, CancellationToken ct = default)
        => sink.EmitAsync(new AgentEvent.Phase(DateTimeOffset.UtcNow, agent, PhaseStatus.Ok, detail), ct);

    public static Task PhaseFailAsync(this IEventSink sink, string agent, string detail, CancellationToken ct = default)
        => sink.EmitAsync(new AgentEvent.Phase(DateTimeOffset.UtcNow, agent, PhaseStatus.Fail, detail), ct);

    public static Task PhaseInfoAsync(this IEventSink sink, string agent, string detail, CancellationToken ct = default)
        => sink.EmitAsync(new AgentEvent.Phase(DateTimeOffset.UtcNow, agent, PhaseStatus.Info, detail), ct);
}

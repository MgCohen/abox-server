namespace RemoteAgents.Flows;

/// <summary>
/// The catalog declaration — the named, configured flows this orchestrator offers.
/// Code now, data-shaped (immutable records) so a JSON/asset source can replace it
/// additively later. The composition root walks this list rather than hand-picking
/// each flow; a static list of data is not a service, so it doesn't fight "DI over
/// statics". See ADR 0001.
/// </summary>
public static class FlowManifest
{
    public static readonly IReadOnlyList<FlowDefinition> Definitions =
    [
        new(typeof(StubFlow), new FlowConfig("stub", "Walking-skeleton stub: placeholder steps, no real work.")),
    ];
}

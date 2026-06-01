namespace RemoteAgents.Flows;

/// <summary>
/// Resolves the flow instance for a catalog definition from the container — the
/// "create via factory, never <c>new</c> inline at composition" seam (R-SPINE-2,
/// ADR 0001). Fakeable, so a test can swap every flow the orchestrator runs.
/// </summary>
public interface IFlowFactory
{
    /// <summary>Resolve the flow instance for <paramref name="definition"/> from DI.</summary>
    Flow Create(FlowDefinition definition);
}

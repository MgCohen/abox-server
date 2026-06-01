namespace RemoteAgents.Flows;

/// <summary>
/// Produces a configured flow instance on demand from a catalog name — the "create
/// via factory, never <c>new</c> inline at composition" seam (R-SPINE-2, ADR 0001).
/// Fakeable, so a test can swap every flow the orchestrator runs.
/// </summary>
public interface IFlowFactory
{
    /// <summary>Resolve + configure the flow registered under <paramref name="name"/>; null if unknown.</summary>
    Flow? Create(string name);
}

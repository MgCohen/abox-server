namespace RemoteAgents.Flows;

/// <summary>
/// Name → <see cref="FlowDefinition"/>. The named, configured flows the orchestrator
/// offers are declared in <see cref="Build"/> — one typed <c>Register</c> line per
/// entry, no dictionary literal, no inline <c>new</c>. Endpoints list it;
/// <see cref="IFlowFactory"/> resolves through it. Distinct from <see cref="FlowRegistry"/>
/// (runtime, Guid-keyed live + history). See ADR 0001.
/// </summary>
public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlowDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    private FlowCatalog() { }

    /// <summary>
    /// Declare the catalog. Runs at composition, so a blank or duplicate name is a
    /// fail-fast boot error. Adding a flow is a single <c>Register&lt;T&gt;</c> line here.
    /// </summary>
    public static FlowCatalog Build()
    {
        var catalog = new FlowCatalog();
        catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub: placeholder steps, no real work."));
        return catalog;
    }

    private void Register<TFlow>(FlowConfig config) where TFlow : Flow
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            throw new ArgumentException($"Flow {typeof(TFlow).Name} registered with a blank name.");
        if (!_byName.TryAdd(config.Name, new FlowDefinition(typeof(TFlow), config)))
            throw new ArgumentException($"Duplicate flow name '{config.Name}'.");
    }

    public FlowDefinition? Resolve(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyList<FlowDefinition> All() => [.. _byName.Values];
}

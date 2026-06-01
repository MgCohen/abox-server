namespace RemoteAgents.Flows;

/// <summary>
/// Name → <see cref="FlowDefinition"/>, built from the registered manifest. Endpoints
/// list it; <see cref="IFlowFactory"/> resolves through it. Distinct from
/// <see cref="FlowRegistry"/> (runtime, Guid-keyed live + history).
/// </summary>
/// <remarks>
/// The constructor is the fail-fast boot guard (ADR 0001): a duplicate name, blank
/// metadata, or a non-Flow type throws at startup rather than failing on first request.
/// </remarks>
public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlowDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    public FlowCatalog(IEnumerable<FlowDefinition> definitions)
    {
        foreach (var d in definitions)
        {
            if (string.IsNullOrWhiteSpace(d.Config.Name))
                throw new ArgumentException($"Flow definition for {d.FlowType.Name} has a blank name.");
            if (!typeof(Flow).IsAssignableFrom(d.FlowType))
                throw new ArgumentException($"Flow definition '{d.Config.Name}' type {d.FlowType.Name} is not a Flow.");
            if (!_byName.TryAdd(d.Config.Name, d))
                throw new ArgumentException($"Duplicate flow name '{d.Config.Name}'.");
        }
    }

    public FlowDefinition? Resolve(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyList<FlowDefinition> All() => [.. _byName.Values];
}

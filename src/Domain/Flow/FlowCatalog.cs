namespace RemoteAgents.Domain.Flow;

public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlowDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Register<TFlow>(FlowConfig config) where TFlow : Flow
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            throw new ArgumentException($"Flow {typeof(TFlow).Name} registered with a blank name.");
        if (!_byName.TryAdd(config.Name, new FlowDefinition(typeof(TFlow), config)))
            throw new ArgumentException($"Duplicate flow name '{config.Name}'.");
    }

    public FlowDefinition? Resolve(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyList<FlowDefinition> All() => [.. _byName.Values];
}

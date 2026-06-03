namespace RemoteAgents.Flows;

public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlowDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    private FlowCatalog() { }

    public static FlowCatalog Build()
    {
        var catalog = new FlowCatalog();
        catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub: placeholder steps, no real work."));
        catalog.Register<CodexPingFlow>(new FlowConfig("codex-ping", "Drive the Codex reviewer with the run prompt."));
        catalog.Register<ClaudePingFlow>(new FlowConfig("claude-ping", "Drive the Claude implementer with the run prompt."));
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

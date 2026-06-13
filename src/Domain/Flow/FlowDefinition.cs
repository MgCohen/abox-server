namespace ABox.Domain.Flow;

public sealed record FlowDefinition
{
    public Type FlowType { get; }
    public FlowConfig Config { get; }

    public FlowDefinition(Type flowType, FlowConfig config)
    {
        if (flowType.IsAbstract || !flowType.IsSubclassOf(typeof(Flow)))
            throw new ArgumentException(
                $"FlowType must be a concrete Flow subclass; got '{flowType}'.", nameof(flowType));
        FlowType = flowType;
        Config = config;
    }
}

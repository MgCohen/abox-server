namespace RemoteAgents.Flows;

/// <summary>
/// Per-definition flow configuration. For now just catalog metadata (name +
/// description); flow-specific knobs subclass this when a flow actually needs them
/// (e.g. a future <c>ReviewFlowConfig</c>). See ADR 0001.
/// </summary>
public record FlowConfig(string Name, string Description);

namespace RemoteAgents.Flows;

/// <summary>
/// A catalog entry: a flow <see cref="FlowType"/> paired with its
/// <see cref="Config"/>. The type is the code↔data bridge the factory resolves
/// from DI; the config carries metadata (and, later, knobs). The same type may
/// appear in several definitions with different configs. See ADR 0001.
/// </summary>
public sealed record FlowDefinition(Type FlowType, FlowConfig Config);

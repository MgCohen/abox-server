namespace RemoteAgents.Contracts;

/// <summary>A catalog entry: a runnable flow's name + human description.</summary>
public sealed record FlowInfo(string Name, string Description);

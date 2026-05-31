namespace RemoteAgents.Contracts;

/// <summary>A registered project: short name + absolute directory.</summary>
public sealed record ProjectInfo(string Name, string Path);

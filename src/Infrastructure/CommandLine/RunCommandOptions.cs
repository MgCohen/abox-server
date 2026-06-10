namespace RemoteAgents.Infrastructure.CommandLine;

public sealed record RunCommandOptions(
    string? Cwd = null,
    IDictionary<string, string?>? Env = null,
    int TimeoutMs = 5 * 60_000,
    string? Input = null);

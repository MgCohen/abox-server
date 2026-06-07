namespace RemoteAgents.Actors.Agents;

public abstract record AgentConfig(
    string Name,
    string Description,
    string Model,
    string SystemPrompt,
    PermissionPolicy Policy = PermissionPolicy.Bypass,
    Resolution Resolution = Resolution.Auto,
    int ResolveCap = 8);

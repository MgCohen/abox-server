namespace RemoteAgents.Actors.Agents;

public abstract record AgentConfig(
    string Name,
    string Description,
    string Model,
    string SystemPrompt,
    PermissionPolicy Policy = PermissionPolicy.Bypass,
    Interactivity Interactivity = Interactivity.Autonomous,
    int ResolveCap = 8);

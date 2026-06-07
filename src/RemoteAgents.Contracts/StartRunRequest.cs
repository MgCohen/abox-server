namespace RemoteAgents.Contracts;

public sealed record StartRunRequest(
    string Project,
    string Flow,
    string Prompt,
    bool Push = false);

public sealed record StartRunResponse(Guid Id);

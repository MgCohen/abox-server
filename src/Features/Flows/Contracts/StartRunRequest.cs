namespace RemoteAgents.Features.Flows.Contracts;

public sealed record StartRunRequest(
    string Project,
    string Flow,
    string Prompt,
    bool Push = false);

namespace ABox.Features.Flows.Contracts;

public sealed record StartRunRequest(
    Guid ProjectId,
    string Flow,
    string Prompt,
    bool Push = false);

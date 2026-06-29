namespace ABox.Features.Flows.Contract;

public sealed record StartRunRequest(
    Guid ProjectId,
    string Flow,
    string Prompt,
    bool Push = false);

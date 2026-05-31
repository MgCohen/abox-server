namespace RemoteAgents.Contracts;

/// <summary>Body of <c>POST /flows</c> — launch a run of <see cref="Flow"/> against <see cref="Project"/>.</summary>
public sealed record StartRunRequest(
    string Project,
    string Flow,
    string Prompt,
    string[]? Args = null,
    bool Push = false);

/// <summary>Response to <c>POST /flows</c> — the new run's id.</summary>
public sealed record StartRunResponse(Guid Id);

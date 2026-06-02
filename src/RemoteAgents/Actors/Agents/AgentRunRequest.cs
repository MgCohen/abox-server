namespace RemoteAgents.Actors.Agents;

public sealed record AgentRunRequest
{
    public string Prompt { get; }
    public string ProjectDir { get; }
    public string Model { get; }
    public string SystemPrompt { get; }
    public string? SessionId { get; }

    public AgentRunRequest(string prompt, string projectDir, string model, string systemPrompt, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        if (string.IsNullOrWhiteSpace(projectDir))
            throw new ArgumentException("Project directory is required.", nameof(projectDir));
        Prompt = prompt;
        ProjectDir = projectDir;
        Model = model;
        SystemPrompt = systemPrompt;
        SessionId = sessionId;
    }
}

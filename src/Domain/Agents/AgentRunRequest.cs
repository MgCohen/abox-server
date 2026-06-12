namespace ABox.Domain.Agents;

public sealed record AgentRunRequest
{
    public string Prompt { get; }
    public string ProjectDir { get; }
    public string? SessionId { get; }

    public AgentRunRequest(string prompt, string projectDir, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        if (string.IsNullOrWhiteSpace(projectDir))
            throw new ArgumentException("Project directory is required.", nameof(projectDir));
        Prompt = prompt;
        ProjectDir = projectDir;
        SessionId = sessionId;
    }
}

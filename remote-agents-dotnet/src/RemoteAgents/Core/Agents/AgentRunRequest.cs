namespace RemoteAgents.Agents;

public sealed record AgentRunRequest
{
    public string           Prompt     { get; }
    public string?          SessionId  { get; }
    public string           ProjectDir { get; }
    public InteractionMode  Mode       { get; }

    public AgentRunRequest(
        string          Prompt,
        string?         SessionId,
        string          ProjectDir,
        InteractionMode Mode = InteractionMode.NonInteractive)
    {
        if (string.IsNullOrEmpty(Prompt))
            throw new ArgumentException("prompt is required", nameof(Prompt));
        if (string.IsNullOrEmpty(ProjectDir))
            throw new ArgumentException("projectDir is required", nameof(ProjectDir));
        this.Prompt     = Prompt;
        this.SessionId  = SessionId;
        this.ProjectDir = ProjectDir;
        this.Mode       = Mode;
    }
}

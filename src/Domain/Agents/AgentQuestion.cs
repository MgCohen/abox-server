namespace RemoteAgents.Actors.Agents;

public abstract record AgentQuestion(string Prompt, string RawTail)
{
    public sealed record Open(string Prompt, string RawTail)
        : AgentQuestion(Prompt, RawTail);

    public sealed record Choice(
        string Prompt,
        IReadOnlyList<string> Options,
        bool AllowFreeText,
        string RawTail)
        : AgentQuestion(Prompt, RawTail);
}

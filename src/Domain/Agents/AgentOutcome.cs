namespace RemoteAgents.Domain.Agents;

public abstract record AgentOutcome
{
    public sealed record Completed(AgentResult Result) : AgentOutcome
    {
        public override string ToString() => Result.Text;
    }

    public sealed record NeedsInput(AgentResult Result, AgentQuestion Question) : AgentOutcome
    {
        public override string ToString() => $"Needs input: {Question.Prompt}";
    }

    public sealed record Faulted(AgentResult Result, AgentError Error) : AgentOutcome
    {
        public override string ToString() => $"Faulted: {Error}";
    }
}

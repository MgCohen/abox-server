namespace RemoteAgents.Actors.Agents.Claude;

// Oracle A1: claude bills against the API (not the Max subscription) if any of
// these are visible to the child. The provider blanks them on the child env;
// SubscriptionGuard refuses to start if any are set on the parent.
public static class EnvScrub
{
    public static readonly IReadOnlyList<string> SubscriptionKeys =
    [
        "ANTHROPIC_API_KEY",
        "CLAUDE_API_KEY",
    ];
}

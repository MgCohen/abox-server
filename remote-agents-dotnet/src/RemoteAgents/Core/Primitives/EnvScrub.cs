namespace RemoteAgents.Primitives;

// Single owner of the "blank the API-key env vars" list. SubscriptionGuard
// refuses to start if any of these are set on the parent process;
// providers ALSO blank them on the child env they hand to the subprocess
// as defense in depth — if SubscriptionGuard ever regresses, the child
// still can't fall through to API billing.
public static class EnvScrub
{
    public static readonly IReadOnlyList<string> SubscriptionKeys = new[]
    {
        "ANTHROPIC_API_KEY",
        "CLAUDE_API_KEY",
        "OPENAI_API_KEY",
    };
}

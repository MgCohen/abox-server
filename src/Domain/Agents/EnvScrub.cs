namespace ABox.Domain.Agents;

// Oracle A1: an agent CLI bills the metered API instead of the Max / ChatGPT subscription
// if its own billing key is visible to the child. Each provider hands its keys to
// SubscriptionGuard, which refuses to start when any are set — forcing the subscription
// rail. Per-provider so a stray OPENAI_API_KEY never blocks claude, nor an ANTHROPIC_API_KEY codex.
public static class EnvScrub
{
    public static readonly IReadOnlyList<string> ClaudeKeys =
    [
        "ANTHROPIC_API_KEY",
        "CLAUDE_API_KEY",
    ];

    public static readonly IReadOnlyList<string> CodexKeys =
    [
        "OPENAI_API_KEY",
    ];
}

namespace ABox.Domain.Agents;

// Turns a config's Resolution into the resolver that answers its questions plus the loop cap.
// Auto always produces an answer and could loop forever, so it runs capped; Deny and Human
// self-terminate (they return null when done), so they run uncapped.
public sealed class ResolverSelector(IDecisionResolver human, AutoResolver auto, DenyResolver deny)
{
    public (IDecisionResolver Resolver, int? Cap) For(AgentConfig config) => config.Resolution switch
    {
        Resolution.Auto => (auto, config.ResolveCap),
        Resolution.Deny => (deny, null),
        Resolution.Human => (human, null),
        var r => throw new NotSupportedException($"Resolution.{r} is not wired yet."),
    };
}

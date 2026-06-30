namespace ABox.Governance.Hooks;

public sealed record TurnEndedOutcome(int Dispatched, HookFeedback Feedback)
{
    public static readonly TurnEndedOutcome NotOptedIn = new(-1, HookFeedback.None);
}

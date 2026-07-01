namespace ABox.Governance.Hooks;

public abstract record HookAction
{
    public sealed record Run(string Command) : HookAction;

    public sealed record Agent(string Prompt) : HookAction;
}

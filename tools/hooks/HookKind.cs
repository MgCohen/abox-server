namespace ABox.Governance.Hooks;

public enum HookKind
{
    SessionBegan,
    PromptSubmitted,
    ToolPending,
    ToolDone,
    AwaitingInput,
    TurnEnded,
    CommitLanded,
}

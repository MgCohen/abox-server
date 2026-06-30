namespace ABox.Governance.Hooks;

public sealed record HookLogSlice(IReadOnlyList<HookEvent> Events, long NextOffset);

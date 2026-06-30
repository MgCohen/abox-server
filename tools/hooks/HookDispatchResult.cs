namespace ABox.Governance.Hooks;

public sealed record HookDispatchResult(string HookPath, int ExitCode, bool TimedOut, string? Error)
{
    public bool Ok => Error is null && !TimedOut && ExitCode == 0;

    public override string ToString()
    {
        if (Error is not null) return $"hook {HookPath} errored: {Error}";
        if (TimedOut) return $"hook {HookPath} timed out";
        return $"hook {HookPath} exited {ExitCode}";
    }
}

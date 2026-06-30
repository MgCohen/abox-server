namespace ABox.Governance.Hooks;

public enum HookFeedbackKind
{
    None,
    Context,
    Block,
}

public sealed record HookFeedback(HookFeedbackKind Kind, string Text)
{
    public static readonly HookFeedback None = new(HookFeedbackKind.None, "");

    // Translate the synchronous check-hook results into one decision for the running agent: any check
    // that exits non-zero (or errors/times out) BLOCKS, and its output is the reason fed back; otherwise
    // any check output is advisory CONTEXT; no check output means there is nothing to say.
    public static HookFeedback FromChecks(IEnumerable<HookDispatchResult> checks)
    {
        var list = checks.ToList();

        var blocking = list.Where(r => !r.Ok).Select(Reason).ToList();
        if (blocking.Count > 0) return new HookFeedback(HookFeedbackKind.Block, string.Join("\n\n", blocking));

        var context = list.Select(r => r.Feedback).Where(t => t.Length > 0).ToList();
        if (context.Count > 0) return new HookFeedback(HookFeedbackKind.Context, string.Join("\n\n", context));

        return None;
    }

    private static string Reason(HookDispatchResult r)
    {
        if (r.Feedback.Length > 0) return r.Feedback;
        if (r.Error is not null) return $"{r.HookPath}: {r.Error}";
        return r.TimedOut ? $"{r.HookPath} timed out" : $"{r.HookPath} exited {r.ExitCode}";
    }
}

namespace RemoteAgents.Primitives;

// cmd.exe-style argument quoting. ClaudeAgent and CodexAgent both
// hand-built launch lines that get parsed by cmd.exe; deduped here.
public static class Shell
{
    public static string QuoteArg(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (arg.IndexOfAny(QuoteTriggers) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static readonly char[] QuoteTriggers = { ' ', '\t', '"' };
}

namespace RemoteAgents.Tools.CommandLine;

public static class Shell
{
    private static readonly char[] QuoteTriggers = { ' ', '\t', '"' };

    public static string QuoteArg(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (arg.IndexOfAny(QuoteTriggers) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}

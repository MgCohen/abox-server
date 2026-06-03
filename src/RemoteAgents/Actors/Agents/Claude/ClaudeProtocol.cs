using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Claude;

public static class ClaudeProtocol
{
    // Oracle A8: --session-id and --resume are mutually exclusive.
    public static List<string> BuildArgs(string sessionId, bool isResume, string permissionMode, string model, string systemPrompt)
    {
        var args = new List<string>();
        if (isResume) { args.Add("--resume"); args.Add(sessionId); }
        else { args.Add("--session-id"); args.Add(sessionId); }
        if (!string.IsNullOrEmpty(permissionMode)) { args.Add("--permission-mode"); args.Add(permissionMode); }
        if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
        if (!string.IsNullOrEmpty(systemPrompt)) { args.Add("--append-system-prompt"); args.Add(systemPrompt); }
        return args;
    }

    // Oracle A7: match against the ANSI-stripped buffer; the wordings are Claude's.
    public static StartupDialog? DetectStartupDialog(string buffer)
    {
        var plain = AnsiHelpers.StripAnsi(buffer);
        if (plain.Contains("Bypass Permissions mode", StringComparison.Ordinal) ||
            plain.Contains("Yes, I accept", StringComparison.Ordinal))
            return StartupDialog.BypassWarning;
        if (plain.Contains("trust this folder", StringComparison.OrdinalIgnoreCase) ||
            plain.Contains("Is this a project you", StringComparison.OrdinalIgnoreCase))
            return StartupDialog.Trust;
        return null;
    }

    public static string ExtractAssistantText(string buffer, string prompt)
    {
        var plain = AnsiHelpers.StripAnsi(buffer);
        var idx = plain.IndexOf(prompt, StringComparison.Ordinal);
        if (idx < 0) return "";
        var tail = plain[(idx + prompt.Length)..];
        var next = tail.IndexOf("\n> ", StringComparison.Ordinal);
        if (next > 0) tail = tail[..next];
        return tail.Trim();
    }
}

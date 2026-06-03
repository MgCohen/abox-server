using System.Text.RegularExpressions;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Claude;

public static class ClaudeProtocol
{
    // Keybind hint shown only on Claude's live input bar — the positive "ready" signal.
    private const string PromptReadyMarker = "shift+tab";
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static bool IsPromptReady(string buffer) =>
        Normalize(buffer).Contains(PromptReadyMarker, StringComparison.OrdinalIgnoreCase);

    // The TUI positions each glyph with cursor moves, so the ANSI-stripped
    // buffer has no inter-word spaces — markers must be matched whitespace-free.
    private static string Normalize(string buffer) =>
        Whitespace.Replace(AnsiHelpers.StripAnsi(buffer), "");

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

    // Oracle A7: match the dialog wordings (Claude's) against the normalized,
    // whitespace-free buffer. Tweak the needles here if Claude changes them.
    public static StartupDialog? DetectStartupDialog(string buffer)
    {
        var n = Normalize(buffer);
        if (n.Contains("BypassPermissionsmode", StringComparison.Ordinal) ||
            n.Contains("Yes,Iaccept", StringComparison.Ordinal))
            return StartupDialog.BypassWarning;
        if (n.Contains("trustthisfolder", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Isthisaprojectyou", StringComparison.OrdinalIgnoreCase))
            return StartupDialog.Trust;
        return null;
    }
}

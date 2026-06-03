using System.Text.RegularExpressions;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Claude;

public static class ClaudeProtocol
{
    // The keybind hint that appears only on Claude's live input bar — its
    // presence is the positive "ready to accept a prompt" signal. The TUI
    // positions glyphs with cursor moves, so the stripped buffer collapses
    // whitespace; match against the whitespace-free form. Tweak here if
    // Claude changes the input bar.
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

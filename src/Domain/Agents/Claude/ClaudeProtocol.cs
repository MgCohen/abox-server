using System.Text.RegularExpressions;
using RemoteAgents.Infrastructure.CommandLine;

namespace RemoteAgents.Domain.Agents.Claude;

public static class ClaudeProtocol
{
    // Hints shown only on Claude's live input bar — the positive "ready" signal.
    // "shift+tab" rides the bypass-mode footer; "? for shortcuts" shows in default
    // mode where the permission-cycle hint is absent. Match either.
    private static readonly string[] PromptReadyMarkers = ["shift+tab", "forshortcuts"];
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static bool IsPromptReady(string buffer)
    {
        var normalized = Normalize(buffer);
        return PromptReadyMarkers.Any(m => normalized.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    // Bypass skips every check; Auto and Ask both run the default prompt-mode so
    // the PreToolUse hook gates each tool — they differ only in who decides
    // (Auto auto-approves, Ask routes to the resolver), not in the launch flag.
    public static string PermissionMode(PermissionPolicy policy) => policy switch
    {
        PermissionPolicy.Bypass => "bypassPermissions",
        PermissionPolicy.Auto => "default",
        PermissionPolicy.Ask => "default",
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown permission policy."),
    };

    // The TUI positions each glyph with cursor moves, so the ANSI-stripped
    // buffer has no inter-word spaces — markers must be matched whitespace-free.
    private static string Normalize(string buffer) =>
        Whitespace.Replace(AnsiHelpers.StripAnsi(buffer), "");

    // Oracle A8: --session-id and --resume are mutually exclusive.
    // The system prompt is passed by FILE, not inline: the launch line is typed
    // into cmd.exe through the PTY, and a multiline prompt's newlines would submit
    // the command early and mangle it (Windows ConPTY). A file path is single-line.
    public static List<string> BuildArgs(string sessionId, bool isResume, string permissionMode, string model, string? systemPromptFile, string? settingsFile = null)
    {
        var args = new List<string>();
        if (isResume) { args.Add("--resume"); args.Add(sessionId); }
        else { args.Add("--session-id"); args.Add(sessionId); }
        if (!string.IsNullOrEmpty(permissionMode)) { args.Add("--permission-mode"); args.Add(permissionMode); }
        if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
        if (!string.IsNullOrEmpty(systemPromptFile)) { args.Add("--append-system-prompt-file"); args.Add(systemPromptFile); }
        if (!string.IsNullOrEmpty(settingsFile)) { args.Add("--settings"); args.Add(settingsFile); }
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

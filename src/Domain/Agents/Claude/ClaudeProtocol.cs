using System.Text.RegularExpressions;
using ABox.Infrastructure.CommandLine;

namespace ABox.Domain.Agents.Claude;

public static class ClaudeProtocol
{
    // The subscription credential reaches the box here (ADR 0013): an OAuth token, not an
    // API key, so the ANTHROPIC_API_KEY scrub still selects subscription billing (oracle A1).
    public const string OAuthTokenEnvVar = "CLAUDE_CODE_OAUTH_TOKEN";

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

    // The non-secret env injected per turn via `docker exec -e` (ADR 0013): HOME at the
    // mounted skeleton, the Stop/permission shim paths as in-box mount paths, the egress
    // proxy. Per-turn and per-exec: nothing here is baked into the image. The subscription
    // credential is deliberately NOT here — it rides `docker run` (BuildCredentialEnv) so it
    // never reaches the exec line the driving PTY echoes into its buffer.
    public static Dictionary<string, string> BuildBoxEnv(
        string homeMount, string signalPathInBox, string? permissionDirInBox, string? proxyUrl)
    {
        var env = new Dictionary<string, string>
        {
            ["HOME"] = homeMount,
            [ClaudeHooks.SignalEnvVar] = signalPathInBox,
        };
        if (permissionDirInBox is not null)
            env[ClaudeHooks.PermissionEnvVar] = permissionDirInBox;
        if (proxyUrl is { } proxy)
        {
            env["HTTPS_PROXY"] = proxy;
            env["HTTP_PROXY"] = proxy;
        }
        return env;
    }

    // The subscription credential, set at `docker run` (inherited by the turn's `docker
    // exec`) so it stays off the PTY-echoed exec line. An OAuth token, not an API key, so
    // the ANTHROPIC_API_KEY scrub still selects subscription billing (oracle A1); no
    // ANTHROPIC_API_KEY is ever set. Empty when there is no token (an unbilled turn).
    public static IReadOnlyDictionary<string, string>? BuildCredentialEnv(string? setupToken) =>
        string.IsNullOrEmpty(setupToken)
            ? null
            : new Dictionary<string, string> { [OAuthTokenEnvVar] = setupToken };

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

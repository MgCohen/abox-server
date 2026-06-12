using System.Text.RegularExpressions;

namespace ABox.Domain.Agents.Claude;

public sealed class AutoPolicy
{
    public sealed record Rule(Regex Pattern, string Reason);
    public sealed record Verdict(bool Allow, string Reason);

    private readonly IReadOnlyList<Rule> _denylist;

    public AutoPolicy() : this(DefaultDenylist) { }

    public AutoPolicy(IReadOnlyList<Rule> denylist) => _denylist = denylist;

    // Default-allow with a denylist guardrail: Auto runs unattended but blocks the
    // catastrophic, hard-to-undo commands. A guardrail, not a sandbox — the OS
    // sandbox / VM is the real boundary; this just stops the obvious footguns.
    public Verdict Evaluate(PermissionRequest request)
    {
        var (_, detail) = ClaudePermission.Describe(request.Payload);
        if (detail is not null)
            foreach (var rule in _denylist)
                if (rule.Pattern.IsMatch(detail))
                    return new Verdict(false, $"auto guardrail blocked: {rule.Reason}");
        return new Verdict(true, "auto-approved");
    }

    private static Rule Deny(string pattern, string reason) =>
        new(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), reason);

    public static readonly IReadOnlyList<Rule> DefaultDenylist =
    [
        Deny(@"\brm\b[^|&;]*\s-\S*[rf]", "recursive or forced delete (rm)"),
        Deny(@"\bremove-item\b[^|&;]*-recurse", "recursive delete (Remove-Item -Recurse)"),
        Deny(@"\b(rd|rmdir)\b[^|&;]*\s/s", "recursive directory removal (rd /s)"),
        Deny(@"\bgit\s+push\b", "git push"),
        Deny(@"\b(curl|wget|iwr|invoke-webrequest)\b.*\|\s*(sh|bash|zsh|iex|invoke-expression)\b", "pipe a download into a shell"),
        Deny(@"\bsudo\b", "privilege escalation (sudo)"),
        Deny(@"\b(mkfs|diskpart)\b", "disk format"),
        Deny(@"\bformat\s+[a-z]:", "disk format"),
    ];
}

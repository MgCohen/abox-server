using System.Text;

namespace ABox.Governance.Hooks;

public static class HookManifestParser
{
    public static HookManifest Parse(string path, string text)
    {
        List<HookKind>? on = null;
        HookSource? source = null;
        string? cwdGlob = null;
        string? tool = null;
        var mode = HookMode.React;
        string? run = null;

        var lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var colon = line.IndexOf(':');
            if (colon < 0) throw Bad(path, lineNo, raw, "expected 'key: value'");

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "on": on = ParseKinds(path, lineNo, raw, value); break;
                case "when": ApplyWhen(path, lineNo, raw, value, ref source, ref cwdGlob, ref tool); break;
                case "mode": mode = ParseMode(path, lineNo, raw, value); break;
                case "run": run = value; break;
                default: throw Bad(path, lineNo, raw, $"unknown key '{key}' (expected on/when/mode/run)");
            }
        }

        if (on is null || on.Count == 0) throw Bad(path, 0, "", "missing required 'on:' (at least one event kind)");
        if (string.IsNullOrWhiteSpace(run)) throw Bad(path, 0, "", "missing required 'run:' command");

        return new HookManifest(path, on, new HookWhen(source, cwdGlob, tool), mode, run);
    }

    private static List<HookKind> ParseKinds(string path, int lineNo, string raw, string value)
    {
        var inner = value;
        if (inner.StartsWith('[') && inner.EndsWith(']')) inner = inner[1..^1];

        var kinds = new List<HookKind>();
        foreach (var part in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<HookKind>(part, ignoreCase: true, out var k))
                throw Bad(path, lineNo, raw, $"unknown event kind '{part}'");
            kinds.Add(k);
        }
        if (kinds.Count == 0) throw Bad(path, lineNo, raw, "'on:' lists no event kinds");
        return kinds;
    }

    private static void ApplyWhen(
        string path, int lineNo, string raw, string value,
        ref HookSource? source, ref string? cwdGlob, ref string? tool)
    {
        var tokens = Tokenize(value);
        if (tokens.Count == 0) throw Bad(path, lineNo, raw, "empty 'when:' clause");

        switch (tokens[0].ToLowerInvariant())
        {
            case "source":
                if (tokens.Count != 2 || !Enum.TryParse<HookSource>(tokens[1], ignoreCase: true, out var s))
                    throw Bad(path, lineNo, raw, "expected 'source <claude|codex|git>'");
                source = s;
                break;
            case "cwd":
                if (tokens.Count != 3 || !tokens[1].Equals("glob", StringComparison.OrdinalIgnoreCase))
                    throw Bad(path, lineNo, raw, "expected 'cwd glob \"<pattern>\"'");
                cwdGlob = tokens[2];
                break;
            case "tool":
                if (tokens.Count != 2) throw Bad(path, lineNo, raw, "expected 'tool <name>'");
                tool = tokens[1];
                break;
            default:
                throw Bad(path, lineNo, raw, $"unknown when-clause '{tokens[0]}' (expected source/cwd/tool)");
        }
    }

    private static HookMode ParseMode(string path, int lineNo, string raw, string value)
    {
        if (!Enum.TryParse<HookMode>(value, ignoreCase: true, out var mode))
            throw Bad(path, lineNo, raw, $"unknown mode '{value}' (expected react|gate)");
        return mode;
    }

    private static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in value)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static FormatException Bad(string path, int lineNo, string raw, string why)
    {
        var where = lineNo > 0 ? $"{path}:{lineNo}" : path;
        var snippet = raw.Trim().Length > 0 ? $" near \"{raw.Trim()}\"" : "";
        return new FormatException(
            $"Invalid .hook {where}: {why}{snippet}. A .hook needs 'on:' (event kinds) and 'run:' (command); " +
            "optional 'when:' and 'mode:'.");
    }
}

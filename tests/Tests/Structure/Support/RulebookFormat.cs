using System.Text.RegularExpressions;

namespace ABox.Tests.Structure.Support;

// Parses a Rulebook's markdown into the structure the format/grammar guards check: the headings (with level)
// and each '### ' Rule's set of bold-label bullets. A type's template.md carries one example Rule that fixes
// the schema; every Rule in rules.md must match it.
internal static class RulebookFormat
{
    private static readonly Regex LabelBullet = new(@"^\s*-\s+\*\*(?<label>[^:*]+):\*\*", RegexOptions.Compiled);
    private const char Arrow = '→';

    public sealed record Schema(bool HeaderHasArrow, IReadOnlySet<string> Labels);

    public sealed record Heading(int Level, string Text);

    public sealed record RuleBlock(string Header, bool HeaderHasArrow, IReadOnlySet<string> Labels);

    public static Schema ReadSchema(string templatePath)
    {
        var rules = Rules(File.ReadAllLines(templatePath));
        if (rules.Count != 1)
            throw new InvalidOperationException(
                $"Template '{templatePath}' must contain exactly one '### ' example Rule defining the schema; " +
                $"found {rules.Count}.");
        return new Schema(rules[0].HeaderHasArrow, rules[0].Labels);
    }

    public static IReadOnlyList<Heading> Headings(IReadOnlyList<string> lines)
    {
        var headings = new List<Heading>();
        foreach (var line in lines)
        {
            var level = HeadingLevel(line);
            if (level > 0)
                headings.Add(new Heading(level, line[(level + 1)..].Trim()));
        }
        return headings;
    }

    public static IReadOnlyList<RuleBlock> Rules(IReadOnlyList<string> lines)
    {
        var rules = new List<RuleBlock>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (HeadingLevel(lines[i]) != 3)
                continue;

            var header = lines[i][4..].Trim();
            var labels = new HashSet<string>(StringComparer.Ordinal);
            for (var j = i + 1; j < lines.Count && HeadingLevel(lines[j]) == 0; j++)
            {
                var match = LabelBullet.Match(lines[j]);
                if (match.Success)
                    labels.Add(match.Groups["label"].Value.Trim());
            }
            rules.Add(new RuleBlock(header, header.Contains(Arrow), labels));
        }
        return rules;
    }

    private static int HeadingLevel(string line)
    {
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
            hashes++;
        return hashes > 0 && hashes < line.Length && line[hashes] == ' ' ? hashes : 0;
    }
}

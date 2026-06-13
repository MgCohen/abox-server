using System.Text.RegularExpressions;

namespace ABox.Tests.Harness;

// Parses a Rulebook's markdown into the structure the Meta format guards check: the headings (with level) and
// each '### ' Rule's set of bold-label bullets. A type's template.md carries one bare example Rule that fixes
// the schema (fenced examples in it are ignored); every Rule in rules.md must match it.
public static class RulebookFormat
{
    private static readonly Regex LabelBullet = new(@"^\s*-\s+\*\*(?<label>[^:*]+):\*\*", RegexOptions.Compiled);
    private const char Arrow = '→';

    public sealed record Schema(bool HeaderHasArrow, IReadOnlySet<string> Labels);

    public sealed record Heading(int Level, string Text);

    public sealed record RuleBlock(string Header, bool HeaderHasArrow, IReadOnlySet<string> Labels);

    public static Schema ReadSchema(string templatePath)
    {
        var rules = Rules(OutsideFences(File.ReadAllLines(templatePath)));
        if (rules.Count != 1)
            throw new InvalidOperationException(
                $"Template '{templatePath}' must contain exactly one bare '### ' example Rule defining the " +
                $"schema (fenced examples are ignored); found {rules.Count}.");
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

    // Template files may show fenced examples for humans; those lines are dropped before the schema is read, so
    // only the bare example counts. rules.md is never fence-filtered — every '### ' there is a real Rule.
    private static IReadOnlyList<string> OutsideFences(IReadOnlyList<string> lines)
    {
        var kept = new List<string>();
        var inFence = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence)
                kept.Add(line);
        }
        return kept;
    }

    private static int HeadingLevel(string line)
    {
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
            hashes++;
        return hashes > 0 && hashes < line.Length && line[hashes] == ' ' ? hashes : 0;
    }
}

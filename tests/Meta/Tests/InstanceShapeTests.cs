using System.Text.RegularExpressions;
using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Meta.Tests;

// Instance conformance: a type's template.md may declare a ## Shape — the sections every instance must carry.
// Read from disk (Artifacts + home), so a type's structural contract is enforced the moment its template
// declares it. Presence of sections only; whether a section is good stays the judge's (advisory) call.
public class InstanceShapeTests
{
    [Rule("Every artifact instance conforms to its type's declared shape")]
    [Fact]
    public void EveryInstanceConformsToItsTypeShape()
    {
        var violations = new List<string>();
        foreach (var artifact in Artifacts.All())
        {
            var templatePath = Path.Combine(Path.GetDirectoryName(artifact.Path)!, "template.md");
            if (!File.Exists(templatePath))
                continue;

            var required = ShapeHeadings(File.ReadAllLines(templatePath));
            if (required.Count == 0)
                continue;

            if (!artifact.Fields.TryGetValue("home", out var home))
                continue;
            var homeDir = Path.Combine(RepoTree.Root, home);
            if (!Directory.Exists(homeDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(homeDir, "*.md", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                    continue;
                var headings = File.ReadLines(file)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("## "))
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var heading in required)
                    if (!headings.Contains(heading))
                        violations.Add($"{Rel(file)}: missing '{heading}' (required by {artifact.Name} shape)");
            }
        }

        Assert.True(violations.Count == 0,
            $"""
            Artifact instances are missing template-declared sections:
            {Bullets(violations)}
            Each instance under a type's home must carry every '## ' heading its template's ## Shape declares.
            """);
    }

    private static string Rel(string path) => Path.GetRelativePath(RepoTree.Root, path);

    // The instance sections a type requires: the `- `## Heading`` bullets under the template's ## Shape.
    private static IReadOnlyList<string> ShapeHeadings(string[] lines)
    {
        var headings = new List<string>();
        var inShape = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("## "))
                inShape = line == "## Shape";
            else if (inShape)
            {
                var match = Regex.Match(line, @"^-\s+`(#{2,6}\s+.+)`$");
                if (match.Success)
                    headings.Add(match.Groups[1].Value.Trim());
            }
        }
        return headings;
    }
}

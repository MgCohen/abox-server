namespace ABox.Tests.Harness;

// The artifact registry: each governance/registry/<Name>/ folder declares one agent-first artifact type in a
// flat artifact.yml (key: value, one per line). Read leniently from the source tree so the floor meta-guard can
// validate the registry from outside, the way the Rulebook guards read each type's template from disk.
public static class Artifacts
{
    public sealed record Entry(string Name, string Path, IReadOnlyDictionary<string, string> Fields);

    public static IReadOnlyList<Entry> All()
    {
        if (!Directory.Exists(RepoTree.RegistryRoot))
            return Array.Empty<Entry>();

        return Directory.EnumerateDirectories(RepoTree.RegistryRoot)
            .Select(dir => new { Name = System.IO.Path.GetFileName(dir)!, File = System.IO.Path.Combine(dir, "artifact.yml") })
            .Where(x => File.Exists(x.File))
            .Select(x => new Entry(x.Name, x.File, ParseFlat(File.ReadAllLines(x.File))))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    // Flat `key: value` only — no nesting or lists, matching the registry's declared shape. Blank lines and
    // `#` comments are ignored; the value is everything after the first colon, trimmed.
    private static IReadOnlyDictionary<string, string> ParseFlat(IEnumerable<string> lines)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            fields[trimmed[..colon].Trim()] = trimmed[(colon + 1)..].Trim();
        }
        return fields;
    }
}

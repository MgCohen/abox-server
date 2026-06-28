namespace ABox.Tests.Central.Docs.Support;

// Finds every structured document in the repo by its leading front matter, never by path: any *.md whose first
// line opens a `---` block carrying a `docType:` key is an instance the doc-engine must validate (ADR 0015). So a
// new ADR / plan / research instance is covered the moment it lands, while prose that merely shows `docType:` in a
// fenced example (READMEs, howtos) is skipped because its front matter doesn't lead the file. prototype/ is the
// behavioral reference and is never enforced; build outputs and .git hold no authored docs.
public static class DocInstances
{
    private static readonly string[] SkipDirs =
        RepoTree.BuildOutputDirs.Concat(new[] { ".git", "prototype" }).ToArray();

    public static IReadOnlyList<string> Discover()
    {
        var found = new List<string>();
        var stack = new Stack<string>();
        stack.Push(RepoTree.Root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!SkipDirs.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                    stack.Push(sub);
            foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
                if (HasDocTypeFrontMatter(file))
                    found.Add(file);
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static bool HasDocTypeFrontMatter(string path)
    {
        using var reader = new StreamReader(path);
        if (reader.ReadLine() != "---") return false;
        for (var line = reader.ReadLine(); line is not null && line != "---"; line = reader.ReadLine())
            if (line.StartsWith("docType:", StringComparison.Ordinal))
                return true;
        return false;
    }
}

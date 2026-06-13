namespace ABox.Infrastructure.Paths;

public static class RepoRoot
{
    // No Find(string start, params string[] markers) overload: C# would bind Find("x")
    // to it with start="x" and an empty marker list, silently breaking every caller.
    public static string? Find(params string[] markers) =>
        FindFrom(Environment.CurrentDirectory, markers);

    public static string? FindFrom(string start, IReadOnlyList<string> markers)
    {
        foreach (var seed in new[] { start, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(seed); dir is not null; dir = dir.Parent)
            {
                foreach (var marker in markers)
                {
                    var candidate = Path.Combine(dir.FullName, marker);
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                        return dir.FullName;
                }
            }
        }
        return null;
    }
}

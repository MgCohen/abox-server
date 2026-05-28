namespace RemoteAgents.Primitives;

// Single home for "walk up from CWD (and the executing assembly) looking
// for a marker file or directory." Three places used to do this with
// slightly different rules; collapse them here.
public static class RepoRoot
{
    // Returns the first ancestor of the current working directory (or the
    // executing assembly's BaseDirectory) that contains any of `markers`
    // (as a file or directory). Null if not found.
    //
    // NB: do NOT add a `Find(string start, params string[] markers)`
    // overload — C# would resolve `Find("x")` to that one with start="x"
    // and markers=[], silently breaking every caller.
    public static string? Find(params string[] markers) =>
        FindFrom(Environment.CurrentDirectory, markers);

    public static string? FindFrom(string start, IReadOnlyList<string> markers)
    {
        foreach (var seed in new[] { start, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(seed);
            while (dir is not null)
            {
                foreach (var marker in markers)
                {
                    var candidate = Path.Combine(dir.FullName, marker);
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                        return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        return null;
    }
}

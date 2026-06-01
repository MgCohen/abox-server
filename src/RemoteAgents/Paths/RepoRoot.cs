namespace RemoteAgents.Paths;

/// <summary>
/// Walks up from the current working directory (and the executing assembly's
/// base directory) looking for a marker file or directory. One home for
/// "where's the repo/orchestrator root?".
/// </summary>
public static class RepoRoot
{
    /// <summary>
    /// First ancestor of the CWD (or the app base directory) that contains any
    /// of <paramref name="markers"/> as a file or directory; null if none.
    /// </summary>
    /// <remarks>
    /// Do NOT add a <c>Find(string start, params string[] markers)</c> overload:
    /// C# would bind <c>Find("x")</c> to it with <c>start="x"</c> and an empty
    /// marker list, silently breaking every caller.
    /// </remarks>
    public static string? Find(params string[] markers) =>
        FindFrom(Environment.CurrentDirectory, markers);

    /// <summary>As <see cref="Find"/> but from an explicit starting directory.</summary>
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

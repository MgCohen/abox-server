namespace RemoteAgents.Primitives;

public sealed record FsStat(long Size, long MtimeMs);

public sealed record FsDiffResult(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed)
{
    public IReadOnlyList<string> All =>
        [.. Changed, .. Added, .. Removed];
}

public static class FsDiff
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.Ordinal)
    {
        ".git",
        "node_modules",
        "sessions",
        // Unity-specific noise
        "Library",
        "Temp",
        "Logs",
        "UserSettings",
        "obj",
        "bin",
        // misc
        ".next",
        ".cache",
        "dist",
        "build",
    };

    public static IReadOnlyDictionary<string, FsStat> Snapshot(string rootDir)
    {
        var out_ = new Dictionary<string, FsStat>(StringComparer.Ordinal);
        Walk(rootDir, rootDir, out_);
        return out_;
    }

    private static void Walk(string root, string dir, Dictionary<string, FsStat> sink)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch { return; }

        foreach (var full in entries)
        {
            var name = Path.GetFileName(full);
            try
            {
                var attrs = File.GetAttributes(full);
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    if (SkipDirs.Contains(name)) continue;
                    Walk(root, full, sink);
                }
                else
                {
                    var info = new FileInfo(full);
                    var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                    sink[rel] = new FsStat(info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
                }
            }
            catch { /* skip unreadable */ }
        }
    }

    public static FsDiffResult Diff(IReadOnlyDictionary<string, FsStat> before, IReadOnlyDictionary<string, FsStat> after)
    {
        var changed = new List<string>();
        var added = new List<string>();
        var removed = new List<string>();

        foreach (var (rel, a) in after)
        {
            if (!before.TryGetValue(rel, out var b)) added.Add(rel);
            else if (b.Size != a.Size || b.MtimeMs != a.MtimeMs) changed.Add(rel);
        }
        foreach (var rel in before.Keys)
        {
            if (!after.ContainsKey(rel)) removed.Add(rel);
        }
        return new FsDiffResult(changed, added, removed);
    }
}

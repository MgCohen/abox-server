namespace ABox.Governance.Hooks;

public sealed class HookCatalog
{
    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.Ordinal) { ".git", "bin", "obj", "artifacts", "node_modules" };

    private readonly IReadOnlyList<string> _scanRoots;
    private readonly Action<string> _report;

    public HookCatalog(IReadOnlyList<string> scanRoots, Action<string>? report = null)
    {
        _scanRoots = scanRoots;
        _report = report ?? (_ => { });
    }

    public IReadOnlyList<HookManifest> Scan()
    {
        var manifests = new List<HookManifest>();
        foreach (var root in _scanRoots)
            if (Directory.Exists(root))
                ScanInto(root, manifests);
        return manifests;
    }

    private void ScanInto(string dir, List<HookManifest> manifests)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.hook"))
        {
            try
            {
                manifests.Add(HookManifestParser.Parse(file, File.ReadAllText(file)));
            }
            catch (FormatException e)
            {
                _report(e.Message);
            }
            catch (IOException e)
            {
                _report($"Could not read .hook {file}: {e.Message}");
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
            if (!SkipDirs.Contains(Path.GetFileName(sub)))
                ScanInto(sub, manifests);
    }
}

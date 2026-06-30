namespace ABox.Governance.Hooks;

public sealed class HookCatalog
{
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
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.hook", SearchOption.AllDirectories))
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
        }
        return manifests;
    }
}

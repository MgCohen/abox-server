namespace ABox.DocEngine;

public static class Catalog
{
    public static string Slug(string label) => label.Trim().ToLowerInvariant().Replace(" ", "-");

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> LoadBlocks(string root)
    {
        var blocks = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        foreach (var file in Files(root, "blocks/*.yaml"))
        {
            var def = Yaml.AsMap(Yaml.Load(file))!;
            blocks[Yaml.AsString(def["type"])!] = def;
        }
        return blocks;
    }

    public static IReadOnlyDictionary<string, object?> LoadDoctype(string root, string name) =>
        Yaml.AsMap(Yaml.Load(Path.Combine(root, "doctypes", name + ".yaml")))!;

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> AllDoctypes(string root) =>
        Files(root, "doctypes/*.yaml").Select(f => Yaml.AsMap(Yaml.Load(f))!).ToList();

    public static IReadOnlyList<string> Files(string root, string spec)
    {
        var dir = Path.Combine(root, Path.GetDirectoryName(spec)!);
        var pattern = Path.GetFileName(spec);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, pattern).OrderBy(p => p, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();
    }
}

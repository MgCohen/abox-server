using System.Collections;
using System.Text.Json;

namespace ABox.DocEngine;

public static class CatalogExport
{
    public const string Version = "1";

    public static string Json(string root)
    {
        var catalog = new Dictionary<string, object?>
        {
            ["catalogVersion"] = Version,
            ["kinds"] = Catalog.LoadKinds(root),
            ["blocks"] = Catalog.LoadBlocks(root),
            ["doctypes"] = Catalog.AllDoctypes(root)
                .ToDictionary(d => Yaml.AsString(d["docType"])!, d => (object?)d),
        };
        return JsonSerializer.Serialize(Normalize(catalog), Options);
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static object? Normalize(object? node)
    {
        switch (node)
        {
            case null:
                return null;
            case string s:
                return s;
            case bool b:
                return b;
            case IDictionary dict:
                var map = new Dictionary<string, object?>();
                foreach (DictionaryEntry e in dict) map[Convert.ToString(e.Key) ?? ""] = Normalize(e.Value);
                return map;
            case IEnumerable list:
                return list.Cast<object?>().Select(Normalize).ToList();
            default:
                return node.ToString();
        }
    }
}

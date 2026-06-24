using System.Collections;
using YamlDotNet.Serialization;

namespace ABox.DocEngine;

internal static class Yaml
{
    private static readonly IDeserializer Deser = new DeserializerBuilder().Build();

    public static object? Load(string path) => Deser.Deserialize<object?>(File.ReadAllText(path));

    public static object? Parse(string text) => Deser.Deserialize<object?>(text);

    public static IReadOnlyDictionary<string, object?>? AsMap(object? node)
    {
        if (node is not IDictionary d) return null;
        var map = new Dictionary<string, object?>();
        foreach (DictionaryEntry e in d) map[Convert.ToString(e.Key) ?? ""] = e.Value;
        return map;
    }

    public static string? AsString(object? node) => node as string;

    public static IReadOnlyList<object?> AsList(object? node) => node switch
    {
        null => Array.Empty<object?>(),
        string s => new object?[] { s },
        IDictionary => new[] { node },
        IEnumerable e => e.Cast<object?>().ToList(),
        _ => new[] { node },
    };

    public static bool IsList(object? node) => node is IEnumerable and not string and not IDictionary;

    private static readonly HashSet<string> FalseTokens = new(StringComparer.OrdinalIgnoreCase) { "false", "no", "off", "0" };
    private static readonly HashSet<string> TrueTokens = new(StringComparer.OrdinalIgnoreCase) { "true", "yes", "on", "1" };

    public static bool IsBoolToken(object? node) =>
        node is bool || (node is string s && (TrueTokens.Contains(s) || FalseTokens.Contains(s)));

    public static bool Truthy(object? node) => node switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0 && !FalseTokens.Contains(s),
        _ => true,
    };
}

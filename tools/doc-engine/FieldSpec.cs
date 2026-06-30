namespace ABox.DocEngine;

public sealed record FieldSpec(string? Type, bool Required, IReadOnlyList<string> Values, string? Pattern, bool Hidden)
{
    public static FieldSpec Normalize(object? spec, bool defaultRequired)
    {
        var map = Yaml.AsMap(spec) ?? throw new InvalidDataException(
            $"field spec must be a map, got '{spec}' — write `type:`/`enum:` explicitly (no bare-string shorthand)");

        IReadOnlyList<string> values = Array.Empty<string>();
        var type = Yaml.AsString(map.GetValueOrDefault("type"));
        if (map.ContainsKey("enum"))
        {
            values = Yaml.AsList(map["enum"]).OfType<string>().ToList();
            type ??= "enum";
        }
        var required = map.ContainsKey("required") ? Yaml.Truthy(map["required"]) : defaultRequired;
        var pattern = Yaml.AsString(map.GetValueOrDefault("pattern"));
        var hidden = Yaml.Truthy(map.GetValueOrDefault("hidden"));
        return new FieldSpec(type, required, values, pattern, hidden);
    }
}

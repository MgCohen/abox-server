namespace ABox.DocEngine;

public sealed class SchemaChecker
{
    private static readonly string[] Types = { "markdown", "string", "enum" };

    private readonly string _root;

    public SchemaChecker(string root) => _root = root;

    public IReadOnlyList<string> Run()
    {
        var errs = new List<string>();
        var floorPath = Path.Combine(_root, "_schema", "kind.schema.yaml");
        var floor = LoadMap(errs, floorPath);
        if (floor is null) return errs;
        Conform(errs, floor, floor, floorPath);

        var floorDefs = Yaml.AsString(floor.GetValueOrDefault("defs"));
        if (floorDefs is null) return errs;
        foreach (var kindFile in Catalog.Files(_root, floorDefs))
        {
            var kind = LoadMap(errs, kindFile);
            if (kind is null) continue;
            Conform(errs, kind, floor, kindFile);

            var kindDefs = Yaml.AsString(kind.GetValueOrDefault("defs"));
            if (kindDefs is null) continue;
            foreach (var defFile in Catalog.Files(_root, kindDefs))
            {
                var def = LoadMap(errs, defFile);
                if (def is not null) Conform(errs, def, kind, defFile);
            }
        }
        return errs;
    }

    private IReadOnlyDictionary<string, object?>? LoadMap(List<string> errs, string path)
    {
        var map = Yaml.AsMap(Yaml.Load(path));
        if (map is null) errs.Add($"{Path.GetRelativePath(_root, path)}: not a YAML map");
        return map;
    }

    private void Conform(List<string> errs, IReadOnlyDictionary<string, object?> defn,
                         IReadOnlyDictionary<string, object?> kind, string path)
    {
        var local = new List<string>();
        var fields = Yaml.AsMap(kind["fields"])!;
        CheckFields(local, defn, fields);
        CheckFieldOrder(local, defn, fields);
        RunConstraints(local, defn, kind.GetValueOrDefault("constraints"));
        var rel = Path.GetRelativePath(_root, path);
        foreach (var e in local) errs.Add($"{rel}: {e}");
    }

    private static void CheckFields(List<string> errs, IReadOnlyDictionary<string, object?> defn,
                                    IReadOnlyDictionary<string, object?> fields)
    {
        foreach (var (name, specNode) in fields)
        {
            var spec = Yaml.AsMap(specNode)!;
            if (Yaml.Truthy(spec.GetValueOrDefault("required")) && !defn.ContainsKey(name))
                errs.Add($"missing required field '{name}'");
            if (defn.TryGetValue(name, out var value))
                CheckField(errs, name, spec, value);
        }
        foreach (var name in defn.Keys)
            if (!fields.ContainsKey(name))
                errs.Add($"unknown field '{name}'");
    }

    private static void CheckFieldOrder(List<string> errs, IReadOnlyDictionary<string, object?> defn,
                                        IReadOnlyDictionary<string, object?> fields)
    {
        // Map order == YAML file order: YamlDotNet yields an insertion-ordered Dictionary and Yaml.AsMap copies it as-is.
        var order = fields.Keys.ToList();
        var present = defn.Keys.Where(order.Contains).ToList();
        var canonical = present.OrderBy(order.IndexOf).ToList();
        if (!present.SequenceEqual(canonical, StringComparer.Ordinal))
            errs.Add($"fields out of canonical order: found [{string.Join(", ", present)}], expected [{string.Join(", ", canonical)}]");
    }

    private static void CheckField(List<string> errs, string name,
                                   IReadOnlyDictionary<string, object?> spec, object? value)
    {
        switch (Yaml.AsString(spec.GetValueOrDefault("kind")))
        {
            case "string":
                if (value is not string) errs.Add($"{name}: expected string");
                break;
            case "bool":
                if (!Yaml.IsBoolToken(value)) errs.Add($"{name}: expected bool");
                break;
            case "list":
                if (!Yaml.IsList(value)) { errs.Add($"{name}: expected list"); break; }
                if (Yaml.AsString(spec.GetValueOrDefault("of")) == "string" && Yaml.AsList(value).Any(x => x is not string))
                    errs.Add($"{name}: expected a list of strings");
                break;
            case "typespec":
                if (!IsTypespec(value)) errs.Add($"{name}: type must be one of {TypeList}");
                break;
            case "attrs":
                var attrs = Yaml.AsMap(value);
                if (attrs is null) { errs.Add($"{name}: expected map"); break; }
                foreach (var (an, av) in attrs)
                    if (!IsTypespec(av)) errs.Add($"{name}.{an}: type must be one of {TypeList}");
                break;
            case "strmap":
                var strmap = Yaml.AsMap(value);
                if (strmap is null) { errs.Add($"{name}: expected map (id: rule)"); break; }
                foreach (var (k, v) in strmap)
                    if (v is not string) errs.Add($"{name}.{k}: expected a string rule");
                break;
            case "fieldmap":
                var fieldmap = Yaml.AsMap(value);
                if (fieldmap is null) { errs.Add($"{name}: expected map (field: spec)"); break; }
                foreach (var (fn, fv) in fieldmap)
                    if (!IsFieldspec(fv)) errs.Add($"{name}.{fn}: each field spec needs a `kind`");
                break;
            case "labelmap":
                var labelmap = Yaml.AsMap(value);
                if (labelmap is null) { errs.Add($"{name}: expected map (label: spec)"); break; }
                foreach (var (ln, lv) in labelmap)
                {
                    var lspec = Yaml.AsMap(lv);
                    if (lspec is null || !lspec.ContainsKey("required"))
                        errs.Add($"{name}.{ln}: each label needs a `required` bool");
                    else if (!Yaml.IsBoolToken(lspec.GetValueOrDefault("required")))
                        errs.Add($"{name}.{ln}.required: expected bool");
                }
                break;
        }
    }

    private static bool IsTypespec(object? value)
    {
        if (value is string s) return Types.Contains(s);
        var map = Yaml.AsMap(value);
        if (map is null) return false;
        var type = Yaml.AsString(map.GetValueOrDefault("type")) ?? (map.ContainsKey("enum") ? "enum" : null);
        return type is not null && Types.Contains(type);
    }

    private static bool IsFieldspec(object? value) => Yaml.AsMap(value)?.GetValueOrDefault("kind") is string;

    private static void RunConstraints(List<string> errs, IReadOnlyDictionary<string, object?> defn, object? constraints)
    {
        foreach (var node in Yaml.AsList(constraints))
        {
            var c = Yaml.AsMap(node);
            if (c is null) { errs.Add("constraint must be a map"); continue; }
            switch (Yaml.AsString(c.GetValueOrDefault("rule")))
            {
                case "requires_when":
                {
                    var when = Yaml.AsString(c.GetValueOrDefault("when"));
                    var then = Yaml.AsString(c.GetValueOrDefault("then"));
                    if (when is null || then is null) { errs.Add("requires_when constraint needs `when` and `then`"); break; }
                    if (Yaml.Truthy(defn.GetValueOrDefault(when)) && !Yaml.Truthy(defn.GetValueOrDefault(then)))
                        errs.Add($"`{when}` is set but `{then}` is missing");
                    break;
                }
                case "subset":
                {
                    var of = Yaml.AsString(c.GetValueOrDefault("of"));
                    var inField = Yaml.AsString(c.GetValueOrDefault("in"));
                    if (of is null || inField is null) { errs.Add("subset constraint needs `of` and `in`"); break; }
                    var extra = StrSet(defn.GetValueOrDefault(of)).Except(StrSet(defn.GetValueOrDefault(inField)))
                        .OrderBy(x => x, StringComparer.Ordinal).ToList();
                    if (extra.Count > 0)
                        errs.Add($"`{of}` is not a subset of `{inField}`: [{string.Join(", ", extra.Select(x => $"'{x}'"))}]");
                    break;
                }
                default:
                    errs.Add($"unknown constraint rule '{Yaml.AsString(c.GetValueOrDefault("rule"))}'");
                    break;
            }
        }
    }

    private static HashSet<string> StrSet(object? node) =>
        Yaml.AsList(node).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static string TypeList =>
        $"[{string.Join(", ", Types.OrderBy(t => t, StringComparer.Ordinal).Select(t => $"'{t}'"))}]";
}

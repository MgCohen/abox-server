namespace ABox.DocEngine;

public static class DocValidator
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs,
        IReadOnlyDictionary<string, object?> dt,
        IReadOnlyList<ParsedBlock> blocks,
        IReadOnlyList<string> groupsSeen,
        IReadOnlyDictionary<string, object?> fm)
    {
        var errs = new List<string>();
        var allowed = Yaml.AsList(dt.GetValueOrDefault("blocks")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var required = Yaml.AsList(dt.GetValueOrDefault("required")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var docType = Yaml.AsString(dt.GetValueOrDefault("docType"));

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var where = $"#{i + 1} (id={(b.Id.Length > 0 ? b.Id : "?")})";
            if (b.Id.Length == 0) errs.Add($"{where}: missing `<!-- id: -->`");
            else if (seenIds.Contains(b.Id)) errs.Add($"{where}: duplicate id '{b.Id}'");
            seenIds.Add(b.Id);

            present.Add(b.Type);
            if (b.Unknown || !defs.ContainsKey(b.Type))
            {
                errs.Add($"{where}: unknown block/section '{b.Type}'");
                continue;
            }
            if (!allowed.Contains(b.Type))
                errs.Add($"{where}: '{b.Type}' not in the '{docType}' catalog");

            var specAttrs = Yaml.AsMap(defs[b.Type].GetValueOrDefault("attrs")) ?? new Dictionary<string, object?>();
            foreach (var (an, raw) in specAttrs)
            {
                var asp = FieldSpec.Normalize(raw, false);
                if (asp.Required && !b.Attrs.ContainsKey(an))
                    errs.Add($"{where} {b.Type}: missing required attr '{an}'");
                if (b.Attrs.TryGetValue(an, out var av) && asp.Type == "enum" && !asp.Values.Contains(av))
                    errs.Add($"{where} {b.Type}: {an}='{av}' not in [{string.Join(", ", asp.Values)}]");
            }
            foreach (var an in b.Attrs.Keys)
                if (!specAttrs.ContainsKey(an))
                    errs.Add($"{where} {b.Type}: unknown attr '{an}'");

            var bodySpec = defs[b.Type].GetValueOrDefault("body");
            if (bodySpec is not null)
            {
                var body = FieldSpec.Normalize(bodySpec, true);
                if (body.Required && b.Body.Length == 0)
                    errs.Add($"{where} {b.Type}: required body is empty");
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in blocks) counts[b.Type] = counts.GetValueOrDefault(b.Type) + 1;
        foreach (var gt in groupsSeen)
            if (counts.GetValueOrDefault(gt) == 0)
                errs.Add($"group '{Yaml.AsString(defs[gt].GetValueOrDefault("group"))}' has no members");

        foreach (var t in required.Except(present).OrderBy(x => x, StringComparer.Ordinal))
            errs.Add($"doctype: required block '{t}' is missing");

        foreach (var (an, raw) in Yaml.AsMap(dt.GetValueOrDefault("attrs")) ?? new Dictionary<string, object?>())
        {
            var asp = FieldSpec.Normalize(raw, false);
            var fav = Yaml.AsString(fm.GetValueOrDefault(an));
            if (asp.Required && !fm.ContainsKey(an))
                errs.Add($"doc: missing required front-matter attr '{an}'");
            if (fm.ContainsKey(an) && asp.Type == "enum" && (fav is null || !asp.Values.Contains(fav)))
                errs.Add($"doc: front-matter {an}='{fav}' not in [{string.Join(", ", asp.Values)}]");
        }
        return errs;
    }
}

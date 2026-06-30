using System.Text.RegularExpressions;

namespace ABox.DocEngine;

public static class DocValidator
{
    private static readonly Regex LabelBullet = new(@"^-\s+\*\*(?<label>[^:*]+):\*\*");
    private static readonly Regex OnChange = new(@"^(\.claude/(agents|hooks)|scripts)/[A-Za-z0-9._/-]+$");

    private static HashSet<string> LabelsIn(string body)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var inFence = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
            if (!inFence && LabelBullet.Match(line) is { Success: true } m)
                labels.Add(m.Groups["label"].Value.Trim());
        }
        return labels;
    }

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
        var present = new HashSet<string>(StringComparer.Ordinal);
        var docType = Yaml.AsString(dt.GetValueOrDefault("docType"));

        CheckDuplicateIds(blocks, "", errs);
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var where = $"#{i + 1} ({Label(b)})";
            present.Add(b.Type);
            if (b.Unknown || !defs.ContainsKey(b.Type))
            {
                errs.Add($"{where}: unknown block/section '{b.Type}'");
                continue;
            }
            if (!allowed.Contains(b.Type))
                errs.Add($"{where}: '{b.Type}' not in the '{docType}' catalog");
            ValidateBlock(defs, b, where, errs);
            ValidateChildren(defs, b, where, errs);
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

        // onChange is a universal optional handler any doc may declare; the engine validates the
        // pointer (a relative path under a runnable root) but never executes it — a dispatcher does.
        var onChange = Yaml.AsString(fm.GetValueOrDefault("onChange"));
        if (onChange is not null && (onChange.Contains("..", StringComparison.Ordinal) || !OnChange.IsMatch(onChange)))
            errs.Add($"doc: onChange '{onChange}' must be a relative path under .claude/agents, .claude/hooks, or scripts/");
        return errs;
    }

    private static void ValidateBlock(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs,
        ParsedBlock b, string where, List<string> errs)
    {
        var specAttrs = Yaml.AsMap(defs[b.Type].GetValueOrDefault("attrs")) ?? new Dictionary<string, object?>();
        foreach (var (an, raw) in specAttrs)
        {
            var asp = FieldSpec.Normalize(raw, false);
            if (asp.Required && !b.Attrs.ContainsKey(an))
                errs.Add($"{where} {b.Type}: missing required attr '{an}'");
            if (!b.Attrs.TryGetValue(an, out var av)) continue;
            if (asp.Type == "enum" && !asp.Values.Contains(av))
                errs.Add($"{where} {b.Type}: {an}='{av}' not in [{string.Join(", ", asp.Values)}]");
            if (asp.Pattern is { } pat && !Regex.IsMatch(av, pat))
                errs.Add($"{where} {b.Type}: {an}='{av}' does not match /{pat}/");
        }
        foreach (var an in b.Attrs.Keys)
            if (!specAttrs.ContainsKey(an))
                errs.Add($"{where} {b.Type}: unknown attr '{an}'");

        var bodySpec = defs[b.Type].GetValueOrDefault("body");
        if (bodySpec is not null && FieldSpec.Normalize(bodySpec, true).Required && b.Body.Length == 0)
            errs.Add($"{where} {b.Type}: required body is empty");

        var labelSpec = Yaml.AsMap(defs[b.Type].GetValueOrDefault("labels"));
        if (labelSpec is null) return;
        var labels = LabelsIn(b.Body);
        foreach (var (label, spec) in labelSpec)
        {
            var ls = Yaml.AsMap(spec);
            if (ls is not null && Yaml.Truthy(ls.GetValueOrDefault("required")) && !labels.Contains(label))
                errs.Add($"{where} {b.Type}: missing required label '**{label}:**'");
        }
        foreach (var label in labels)
            if (!labelSpec.ContainsKey(label))
                errs.Add($"{where} {b.Type}: unexpected label '**{label}:**'");
    }

    private static void ValidateChildren(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs,
        ParsedBlock parent, string where, List<string> errs)
    {
        var composes = Yaml.AsList(defs[parent.Type].GetValueOrDefault("composes"))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (composes.Count == 0) return;
        if (parent.Children.Count == 0)
            errs.Add($"{where} {parent.Type}: requires at least one {string.Join("/", composes)}");

        CheckDuplicateIds(parent.Children, where + " ", errs);
        for (var j = 0; j < parent.Children.Count; j++)
        {
            var c = parent.Children[j];
            var cw = $"{where} > #{j + 1} ({Label(c)})";
            if (!defs.ContainsKey(c.Type))
            {
                errs.Add($"{cw}: unknown block/section '{c.Type}'");
                continue;
            }
            if (!composes.Contains(c.Type))
                errs.Add($"{cw}: '{c.Type}' is not composable by '{parent.Type}'");
            ValidateBlock(defs, c, cw, errs);
            ValidateChildren(defs, c, cw, errs);
        }
    }

    private static void CheckDuplicateIds(IReadOnlyList<ParsedBlock> siblings, string where, List<string> errs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in siblings)
            if (b.Attrs.TryGetValue("id", out var id) && !seen.Add(id))
                errs.Add($"{where}duplicate id '{id}'");
    }

    private static string Label(ParsedBlock b) =>
        b.Attrs.TryGetValue("id", out var id) ? $"id={id}" : b.Title;
}

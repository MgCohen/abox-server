using System.Text.RegularExpressions;

namespace ABox.DocEngine;

public static class InstanceParser
{
    private static readonly Regex H2 = new(@"^##\s+(.+?)\s*$");
    private static readonly Regex H3 = new(@"^###\s+(.+?)\s*$");
    // A composed child renders two levels below its member (h3 procedure → h5 step), one heading level
    // deeper than a plain sub-block, so the nesting reads at a glance in raw markdown. See ADR 0017.
    private static readonly Regex H5 = new(@"^#####\s+(.+?)\s*$");
    private static readonly Regex AttrRe = new(@"^([\w-]+):\s*(.+?)\s*$");
    private static readonly Regex LabelBulletRe = new(@"^-?\s*\*\*(?<label>[^:*]+):\*\*");

    public static IReadOnlyDictionary<string, object?> ParseFrontmatter(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].Trim() != "---") return new Dictionary<string, object?>();
        var buf = new List<string>();
        foreach (var raw in lines.Skip(1))
        {
            if (raw.Trim() == "---") break;
            buf.Add(raw);
        }
        return Yaml.AsMap(Yaml.Parse(string.Join("\n", buf))) ?? new Dictionary<string, object?>();
    }

    public static string DoctypeOf(string path, IReadOnlyList<string> lines)
    {
        var dt = Yaml.AsString(ParseFrontmatter(lines).GetValueOrDefault("docType"));
        if (string.IsNullOrEmpty(dt))
            throw new InvalidDataException($"no `docType` in the `---` front matter of {path}");
        return dt;
    }

    public static (List<ParsedBlock> Blocks, List<string> GroupsSeen) Parse(
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs)
    {
        var (singleton, group) = LabelMaps(defs);
        var headingAttr = HeadingAttrKeys(defs);
        var labels = LabelNames(defs);
        var blocks = new List<ParsedBlock>();
        var groupsSeen = new List<string>();
        ParsedBlock? cur = null;
        ParsedBlock? child = null;
        string? mode = null, gtype = null;
        var meta = false;

        void CloseChild()
        {
            if (child is null) return;
            child.Body = string.Join("\n", child.Lines).Trim();
            cur!.Children.Add(child);
            child = null;
        }

        void Close()
        {
            CloseChild();
            if (cur is null) return;
            cur.Body = string.Join("\n", cur.Lines).Trim();
            blocks.Add(cur);
            cur = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\n');
            var m2 = H2.Match(line);
            var m3 = m2.Success ? Match.Empty : H3.Match(line);
            var m5 = m2.Success || m3.Success ? Match.Empty : H5.Match(line);
            if (m2.Success)
            {
                Close();
                var lab = Catalog.Slug(m2.Groups[1].Value);
                if (group.TryGetValue(lab, out var gt))
                {
                    mode = "group";
                    gtype = gt;
                    groupsSeen.Add(gt);
                }
                else if (singleton.TryGetValue(lab, out var st))
                {
                    mode = "single";
                    gtype = null;
                    cur = new ParsedBlock { Type = st };
                    meta = true;
                }
                else
                {
                    mode = "single";
                    gtype = null;
                    cur = new ParsedBlock { Type = lab, Unknown = true };
                    meta = true;
                }
                continue;
            }
            if (m3.Success)
            {
                if (mode == "group")
                {
                    Close();
                    cur = new ParsedBlock
                    {
                        Type = gtype!,
                        Title = m3.Groups[1].Value,
                        Group = Yaml.AsString(defs[gtype!].GetValueOrDefault("group")),
                    };
                    meta = true;
                    continue;
                }
                (child ?? cur)?.Lines.Add(line);
                continue;
            }
            if (m5.Success && cur is not null && ComposedType(defs, cur.Type) is { } childType)
            {
                CloseChild();
                child = new ParsedBlock { Type = childType };
                var heading = m5.Groups[1].Value;
                if (headingAttr.TryGetValue(childType, out var ordinalAttr) && SplitOrdinal(heading) is (string id, var title))
                {
                    child.Attrs[ordinalAttr] = id;
                    child.Title = title;
                }
                else
                {
                    child.Title = heading;
                }
                meta = true;
                continue;
            }
            var tgt = child ?? cur;
            if (tgt is null) continue;
            if (meta)
            {
                if (line.Trim().Length == 0) { meta = false; continue; }
                var am = AttrRe.Match(line);
                if (am.Success) { tgt.Attrs[am.Groups[1].Value] = am.Groups[2].Value; continue; }
                meta = false;
            }
            if (child is not null && LabelBulletRe.Match(line) is { Success: true } lm)
            {
                var name = lm.Groups["label"].Value.Trim();
                if (labels.GetValueOrDefault(cur!.Type)?.Contains(name) == true &&
                    labels.GetValueOrDefault(child.Type)?.Contains(name) != true)
                {
                    CloseChild();
                    cur!.Lines.Add(line);
                    continue;
                }
            }
            tgt.Lines.Add(line);
        }
        Close();
        return (blocks, groupsSeen);
    }

    private static string? ComposedType(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs, string type) =>
        defs.TryGetValue(type, out var def)
            ? Yaml.AsList(def.GetValueOrDefault("composes")).OfType<string>().FirstOrDefault()
            : null;

    private static Dictionary<string, HashSet<string>> LabelNames(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs)
    {
        var labels = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (type, def) in defs)
            if (Yaml.AsMap(def.GetValueOrDefault("labels")) is { } ls)
                labels[type] = ls.Keys.ToHashSet(StringComparer.Ordinal);
        return labels;
    }

    private static (string Id, string Title)? SplitOrdinal(string heading)
    {
        var sp = -1;
        for (var i = 0; i < heading.Length; i++)
            if (char.IsWhiteSpace(heading[i])) { sp = i; break; }
        if (sp <= 0) return null;
        var token = heading[..sp].TrimEnd('.');
        if (token.Length == 0 || !char.IsDigit(token[0])) return null;
        return (token, heading[(sp + 1)..].Trim());
    }

    private static Dictionary<string, string> HeadingAttrKeys(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs)
    {
        var heading = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, def) in defs)
            foreach (var (name, spec) in Yaml.AsMap(def.GetValueOrDefault("attrs")) ?? new Dictionary<string, object?>())
                if (FieldSpec.Normalize(spec, false).InHeading)
                    heading[type] = name;
        return heading;
    }

    private static (Dictionary<string, string> Singleton, Dictionary<string, string> Group) LabelMaps(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs)
    {
        var singleton = new Dictionary<string, string>();
        var group = new Dictionary<string, string>();
        foreach (var (type, def) in defs)
        {
            if (Yaml.Truthy(def.GetValueOrDefault("collection")))
                group[Catalog.Slug(Yaml.AsString(def.GetValueOrDefault("group")) ?? type)] = type;
            else
                singleton[Catalog.Slug(type)] = type;
        }
        return (singleton, group);
    }
}

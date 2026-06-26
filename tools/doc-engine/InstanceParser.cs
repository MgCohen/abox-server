using System.Text.RegularExpressions;

namespace ABox.DocEngine;

public static class InstanceParser
{
    private static readonly Regex H2 = new(@"^##\s+(.+?)\s*$");
    private static readonly Regex H3 = new(@"^###\s+(.+?)\s*$");
    private static readonly Regex IdRe = new(@"^<!--\s*id:\s*(\S+)\s*-->\s*$");
    private static readonly Regex AttrRe = new(@"^([\w-]+):\s*(.+?)\s*$");

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
        var blocks = new List<ParsedBlock>();
        var groupsSeen = new List<string>();
        ParsedBlock? cur = null;
        string? mode = null, gtype = null;
        var meta = false;

        void Close()
        {
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
                cur?.Lines.Add(line);
                continue;
            }
            if (cur is null) continue;
            if (meta)
            {
                var idm = IdRe.Match(line);
                if (idm.Success) { cur.Id = idm.Groups[1].Value; continue; }
                if (line.Trim().Length == 0) { meta = false; continue; }
                var am = AttrRe.Match(line);
                if (am.Success) { cur.Attrs[am.Groups[1].Value] = am.Groups[2].Value; continue; }
                meta = false;
            }
            cur.Lines.Add(line);
        }
        Close();
        return (blocks, groupsSeen);
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

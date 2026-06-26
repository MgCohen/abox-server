namespace ABox.DocEngine;

internal static class Program
{
    private static int Main(string[] args)
    {
        var (positional, rootArg) = ParseArgs(args);
        var root = ResolveRoot(rootArg);
        if (positional.Count == 0) return Usage();

        try
        {
            return positional[0] switch
            {
                "check" => Check(root),
                "validate" => ValidateCmd(root, positional.ElementAtOrDefault(1)),
                "catalog" => CatalogCmd(root, positional.ElementAtOrDefault(1)),
                "outline" => OutlineCmd(root, positional.ElementAtOrDefault(1), args.Contains("--write")),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Check(string root)
    {
        var errs = new SchemaChecker(root).Run();
        foreach (var e in errs) Console.WriteLine($"  x {e}");
        if (errs.Count > 0)
        {
            Console.WriteLine($"\nFAIL — {errs.Count} definition violation(s).");
            return 1;
        }
        Console.WriteLine("PASS — meta-schema, kinds, and every definition conform.");
        return 0;
    }

    private static int ValidateCmd(string root, string? rel)
    {
        var path = ToPath(root, rel ?? Path.Combine("out", "git-feature.plan.md"));
        var lines = File.ReadAllLines(path);
        var defs = Catalog.LoadBlocks(root);
        var dt = Catalog.LoadDoctype(root, InstanceParser.DoctypeOf(path, lines));
        var (blocks, groupsSeen) = InstanceParser.Parse(lines, defs);
        var fm = InstanceParser.ParseFrontmatter(lines);
        var errs = DocValidator.Validate(defs, dt, blocks, groupsSeen, fm);
        Console.WriteLine($"doc: {Path.GetRelativePath(root, path)}   docType: {Yaml.AsString(dt.GetValueOrDefault("docType"))}   blocks: {blocks.Count}");
        if (errs.Count > 0)
        {
            Console.WriteLine($"\nFAIL — {errs.Count} violation(s):");
            foreach (var e in errs) Console.WriteLine("  x " + e);
            return 1;
        }
        Console.WriteLine("PASS — conforms to the catalog.");
        return 0;
    }

    private static int CatalogCmd(string root, string? doctype)
    {
        var defs = Catalog.LoadBlocks(root);
        if (doctype is not null)
        {
            var dt = Catalog.LoadDoctype(root, doctype);
            var names = Yaml.AsList(dt.GetValueOrDefault("blocks")).OfType<string>().ToList();
            var width = names.Count == 0 ? 0 : names.Max(n => n.Length);
            Console.WriteLine($"{Yaml.AsString(dt.GetValueOrDefault("docType"))} blocks — pick what carries substance:");
            foreach (var n in names) Console.WriteLine(Row(n, Description(defs, n), width));
            return 0;
        }

        var dts = Catalog.AllDoctypes(root);
        var dtWidth = dts.Count == 0 ? 0 : dts.Max(d => (Yaml.AsString(d.GetValueOrDefault("docType")) ?? "").Length);
        Console.WriteLine("Doc types — pick one:");
        foreach (var d in dts)
            Console.WriteLine(Row(Yaml.AsString(d.GetValueOrDefault("docType")) ?? "", Yaml.AsString(d.GetValueOrDefault("description")), dtWidth));

        var blockWidth = defs.Count == 0 ? 0 : defs.Keys.Max(k => k.Length);
        Console.WriteLine("\nBlocks — pick what carries substance:");
        foreach (var t in defs.Keys.OrderBy(k => k, StringComparer.Ordinal))
            Console.WriteLine(Row(t, Description(defs, t), blockWidth));
        return 0;
    }

    private static int OutlineCmd(string root, string? rel, bool write)
    {
        if (rel is null)
        {
            Console.Error.WriteLine("usage: docengine outline <file> [--write]");
            return 2;
        }
        var path = ToPath(root, rel);
        var lines = File.ReadAllLines(path);
        _ = InstanceParser.DoctypeOf(path, lines); // why: fail fast if the file isn't a valid instance (no docType front matter)
        var defs = Catalog.LoadBlocks(root);
        var (blocks, _) = InstanceParser.Parse(lines, defs);
        var index = Outline.IndexMd(blocks);
        if (write)
        {
            File.WriteAllText(path, Outline.Inject(File.ReadAllText(path), index));
            Console.WriteLine($"injected index ({blocks.Count} blocks) into {Path.GetRelativePath(root, path)}");
            return 0;
        }
        Console.WriteLine(index);
        Console.WriteLine(Outline.StatusBoard(blocks));
        return 0;
    }

    private static string Description(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> defs, string name) =>
        Yaml.AsString(defs[name].GetValueOrDefault("description")) ?? "";

    private static string Row(string name, string? description, int width) => $"  {name.PadRight(width)}  — {description ?? ""}";

    private static (List<string> Positional, string? Root) ParseArgs(string[] args)
    {
        var positional = new List<string>();
        string? root = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
            else if (args[i] != "--write") positional.Add(args[i]);
        }
        return (positional, root);
    }

    private static string ResolveRoot(string? explicitRoot)
    {
        if (explicitRoot is not null) return Path.GetFullPath(explicitRoot);
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "_schema", "kind.schema.yaml"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string ToPath(string root, string rel) =>
        Path.IsPathRooted(rel) || File.Exists(rel) ? rel : Path.Combine(root, rel);

    private static int Usage()
    {
        Console.Error.WriteLine("usage: docengine <check | validate <file> | catalog [doctype] | outline <file> [--write]> [--root <dir>]");
        return 2;
    }
}

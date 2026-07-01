namespace ABox.Versioning;

public static class Cli
{
    private const string Usage =
        "abox-version next --before <dir> --after <dir> --current <vX.Y.Z> [--catalog-before <file>] [--catalog-after <file>]\n" +
        "  Classify the *.Api surface delta and print the next version (or skip if unchanged).\n" +
        "  A changed doc catalog counts as an additive contract change on its own.\n" +
        "abox-version diff --before <dir> --after <dir>\n" +
        "  Print the classified surface delta between two build output dirs.\n" +
        "abox-version dump <dir>\n" +
        "  Print the *.Api public surface of one build output dir.";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help") { Console.WriteLine(Usage); return args.Length == 0 ? 1 : 0; }

        try
        {
            return args[0] switch
            {
                "dump" when args.Length == 2 => Dump(args[1]),
                "diff" => Diff(Opts.Parse(args[1..])),
                "next" => Next(Opts.Parse(args[1..])),
                _ => Fail($"unknown or malformed command '{args[0]}'"),
            };
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
        {
            return Fail(e.Message);
        }
    }

    private static int Dump(string dir)
    {
        foreach (var (name, surface) in SurfaceExtractor.SnapshotDir(dir).OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"# {name}  ({surface.Hash})");
            foreach (var m in surface.Members)
                Console.WriteLine($"  [{m.Kind}] {m.Type} :: {m.Signature}");
        }
        return 0;
    }

    private static int Diff(Opts o)
    {
        PrintDelta(Report(o));
        return 0;
    }

    private static int Next(Opts o)
    {
        var current = SemVer.Parse(o.Require("--current"));
        var report = Report(o);
        PrintDelta(report);

        var catalogChanged = CatalogSignal.Changed(o.Get("--catalog-before"), o.Get("--catalog-after"));
        if (catalogChanged) Console.WriteLine("~ doc catalog changed: additive contract signal");

        var change = CatalogSignal.Combine(report.Classify(), catalogChanged);
        var bump = VersionPolicy.Next(current, change);
        Console.WriteLine();
        if (bump is null)
        {
            Console.WriteLine($"no contract change since {current.Tag} — skip publish");
            Emit(publish: false, version: current.ToString(), level: "none");
            return 0;
        }

        Console.WriteLine($"{current.Tag}  ->  {bump.Next.Tag}   ({change} => {bump.Level})");
        Emit(publish: true, version: bump.Next.ToString(), level: bump.Level);
        return 0;
    }

    private static DiffReport Report(Opts o) =>
        SurfaceDiff.Compare(
            SurfaceExtractor.SnapshotDir(o.Require("--before")),
            SurfaceExtractor.SnapshotDir(o.Require("--after")));

    private static void PrintDelta(DiffReport r)
    {
        foreach (var a in r.AssembliesAdded) Console.WriteLine($"+ assembly added:    {a}");
        foreach (var a in r.AssembliesRemoved) Console.WriteLine($"- assembly removed:  {a}");
        foreach (var a in r.BinaryOnlyChanged) Console.WriteLine($"~ binary changed, surface identical: {a}");
        foreach (var d in r.MembersAdded) Console.WriteLine($"  + {d.Assembly} :: {d.Member.Type} :: {d.Member.Signature}");
        foreach (var d in r.MembersRemoved) Console.WriteLine($"  - {d.Assembly} :: {d.Member.Type} :: {d.Member.Signature}");
        foreach (var d in r.MembersChanged) Console.WriteLine($"  ~ {d.Assembly} :: {d.Member.Type} :: {d.Member.Name}: {d.What}");
        if (r.IsEmpty) Console.WriteLine("(no surface change)");
    }

    // Machine-readable tail for the workflow; also appended to $GITHUB_OUTPUT when present.
    private static void Emit(bool publish, string version, string level)
    {
        var lines = $"publish={publish.ToString().ToLowerInvariant()}\nversion={version}\nlevel={level}";
        Console.WriteLine(lines);
        var gh = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(gh)) File.AppendAllText(gh, lines + "\n");
    }

    private static int Fail(string why)
    {
        Console.Error.WriteLine($"abox-version: {why}");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private sealed class Opts
    {
        private readonly Dictionary<string, string> _values;

        private Opts(Dictionary<string, string> values) => _values = values;

        public string Require(string name) =>
            _values.TryGetValue(name, out var v) ? v : throw new ArgumentException($"missing required option {name}");

        public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

        public static Opts Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"unexpected argument '{args[i]}'");
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"option '{args[i]}' needs a value");
                values[args[i]] = args[++i];
            }
            return new Opts(values);
        }
    }
}

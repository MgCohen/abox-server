using ContractVersioning;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "abox-version-spike diff <beforeDir> <afterDir>   diff the *.Api surface between two build output dirs\n" +
        "abox-version-spike dump <dir>                     print the *.Api surface of one build output dir");
    return 1;
}

switch (args[0])
{
    case "dump" when args.Length == 2:
        Dump(args[1]);
        return 0;
    case "diff" when args.Length == 3:
        return Diff(args[1], args[2]);
    default:
        Console.Error.WriteLine("usage: diff <beforeDir> <afterDir> | dump <dir>");
        return 1;
}

static void Dump(string dir)
{
    foreach (var (name, surface) in SurfaceExtractor.SnapshotDir(dir).OrderBy(kv => kv.Key))
    {
        Console.WriteLine($"# {name}  ({surface.Hash})");
        foreach (var m in surface.Members)
            Console.WriteLine($"  [{m.Kind}] {m.Type} :: {m.Signature}");
    }
}

static int Diff(string beforeDir, string afterDir)
{
    var report = SurfaceDiff.Compare(
        SurfaceExtractor.SnapshotDir(beforeDir),
        SurfaceExtractor.SnapshotDir(afterDir));

    foreach (var a in report.AssembliesAdded) Console.WriteLine($"+ assembly added:    {a}");
    foreach (var a in report.AssembliesRemoved) Console.WriteLine($"- assembly removed:  {a}");
    foreach (var a in report.BinaryOnlyChanged) Console.WriteLine($"~ binary changed, surface identical: {a}");
    foreach (var d in report.MembersAdded) Console.WriteLine($"  + {d.Assembly} :: {d.Member.Type} :: {d.Member.Signature}");
    foreach (var d in report.MembersRemoved) Console.WriteLine($"  - {d.Assembly} :: {d.Member.Type} :: {d.Member.Signature}");
    foreach (var d in report.MembersChanged) Console.WriteLine($"  ~ {d.Assembly} :: {d.Member.Type} :: {d.Member.Name}: {d.What}");

    Console.WriteLine();
    Console.WriteLine($"detected: {string.Join(", ", report.Cases())}");
    return 0;
}

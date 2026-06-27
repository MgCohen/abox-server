namespace Spike;

static class Program
{
    static int Main()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");

        var recipe = new Block(
            new DefineNode(new Lit(0), acc),
            new LoopNode(new Lit(5), i, new Block(
                new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i))))),
            new ReturnNode(new Ref(acc)));

        // The type gate: neither line compiles — a Var<int> handle isn't an IExpr<int>
        // producer, and a handle can't be referenced before it's declared as a local.
        // var bad = new AddNode(new Lit(1), acc);
        // var unknown = new Ref(undeclared);

        var code = Generator.Generate(recipe);

        Console.WriteLine("=== generated ScriptData.cs ===");
        Console.WriteLine(code);

        var outDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Snippets.SourcePath)!, "..", "out"));
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "ScriptData.cs");
        File.WriteAllText(outPath, code);
        Console.WriteLine($"=> wrote {outPath}\n");

        var result = Runtime.CompileAndRun(code);
        Console.WriteLine($"ScriptData.Run() = {result}  (expected 10)");

        if (result != 10)
        {
            Console.Error.WriteLine("FAIL: expected 10");
            return 1;
        }

        Console.WriteLine("PASS");
        return 0;
    }
}

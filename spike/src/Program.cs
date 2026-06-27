namespace Spike;

static class Program
{
    static int Main()
    {
        var recipe = Samples.LoopVarSum;

        // Done-when #3 (the type gate): the next line does NOT compile — a string can't fill
        // an IExpr<int> hole. Uncomment to see the recipe rejected at authoring time.
        // var bad = new AddNode(new Ref("acc"), "oops");

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

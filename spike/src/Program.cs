namespace Spike;

static class Program
{
    static int Main()
    {
        // The type gate (authoring-time): neither line compiles — a Var<int> handle isn't an
        // IExpr<int> producer, and a handle can't be referenced before it's declared as a local.
        // var bad = new AddNode(new Lit(1), new Var<int>("acc"));
        // var unknown = new Ref(undeclared);

        var recipes = new (string Label, Block Recipe, int Expected)[]
        {
            ("define & return", DefineReturn(), 7),
            ("loop + var + sum", LoopVarSum(), 10),
            ("nested loops, two handles", NestedLoops(), 18),
        };

        var failed = false;
        foreach (var (label, recipe, expected) in recipes)
        {
            var code = Generator.Generate(recipe);
            var result = Runtime.CompileAndRun(code);
            var ok = result == expected;
            failed |= !ok;

            Console.WriteLine($"===== {label}  =>  Run() = {result} (expected {expected}) {(ok ? "PASS" : "FAIL")} =====");
            Console.WriteLine(code);
            Console.WriteLine();
        }

        // Emit the canonical sample as a plain, owned .cs — the spike's whole point: the output
        // is just a normal file, not "Roslyn code".
        var outDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Snippets.SourcePath)!, "..", "out"));
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "ScriptData.cs"), Generator.Generate(LoopVarSum()));

        Console.WriteLine(failed ? "FAIL" : "PASS");
        return failed ? 1 : 0;
    }

    // define x = 7; return x;   => 7
    static Block DefineReturn()
    {
        var x = new Var<int>("x");
        return new Block(
            new DefineNode(new Lit(7), x),
            new ReturnNode(new Ref(x)));
    }

    // acc = 0; for i in 0..4 acc = acc + i; return acc;   => 10
    static Block LoopVarSum()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return new Block(
            new DefineNode(new Lit(0), acc),
            new LoopNode(new Lit(5), i, new Block(
                new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i))))),
            new ReturnNode(new Ref(acc)));
    }

    // acc = 0; for i in 0..2 { for j in 0..2 { acc = acc + (i + j); } } return acc;   => 18
    static Block NestedLoops()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        var j = new Var<int>("j");
        return new Block(
            new DefineNode(new Lit(0), acc),
            new LoopNode(new Lit(3), i, new Block(
                new LoopNode(new Lit(3), j, new Block(
                    new AssignNode(acc,
                        new AddNode(new Ref(acc), new AddNode(new Ref(i), new Ref(j)))))))),
            new ReturnNode(new Ref(acc)));
    }
}

using static Spike.Recipe;

namespace Spike;

static class Program
{
    static int Main()
    {
        var failed = false;

        // The same loop-sum recipe authored four ways. They build different C# but the SAME node
        // tree, so the generator emits byte-identical source — these are authoring-only styles.
        var styles = new (string Label, Block Recipe)[]
        {
            ("style 1 — explicit ctors", LoopSum_Explicit()),
            ("style 2 — block brackets", LoopSum_Brackets()),
            ("style 3 — factories", LoopSum_Factories()),
            ("style 4 — literals + operators", LoopSum_Operators()),
        };

        var canonical = Generator.Generate(styles[0].Recipe);
        Console.WriteLine("loop + var + sum — four authoring styles:");
        foreach (var (label, recipe) in styles)
        {
            var code = Generator.Generate(recipe);
            var result = Runtime.CompileAndRun(code);
            var ok = result == 10 && code == canonical;
            failed |= !ok;
            Console.WriteLine($"  {label,-33} Run()={result}  byte-identical={code == canonical}  {(ok ? "PASS" : "FAIL")}");
        }
        Console.WriteLine("\nall four generate the same source:\n");
        Console.WriteLine(canonical);
        Console.WriteLine();

        var others = new (string Label, Block Recipe, int Expected)[]
        {
            ("define & return", DefineReturn(), 7),
            ("nested loops", NestedLoops(), 18),
            ("if/else in a loop", IfElseInLoop(), 26),
        };
        foreach (var (label, recipe, expected) in others)
        {
            var code = Generator.Generate(recipe);
            var result = Runtime.CompileAndRun(code);
            var ok = result == expected;
            failed |= !ok;
            Console.WriteLine($"===== {label}  =>  Run() = {result} (expected {expected}) {(ok ? "PASS" : "FAIL")} =====");
            Console.WriteLine(code);
            Console.WriteLine();
        }

        var outDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Snippets.SourcePath)!, "..", "out"));
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "ScriptData.cs"), canonical);

        // Recipe catalog (M2): the hardcoded type trees are now named, parameterized recipes — the
        // recipe's final shape (a class carrying name + params). CreateModel built twice with
        // different entities proves it is genuinely parameterized, not a literal tree. The gate is
        // compile + reflect (a bare type has nothing to Run()).
        Console.WriteLine("recipe catalog (M2) — named + parameterized:\n");
        var recipes = new IRecipe[]
        {
            new CreateModel("FavoriteArtist",
                new Field<Guid>("Id"), new Field<string>("ArtistId"), new Field<DateTime>("FavoritedAt")),
            new CreateModel("Playlist",
                new Field<Guid>("Id"), new Field<string>("Name"), new Field<int>("TrackCount")),
            new CreateEnum("FavoriteSource", "Search", "Profile", "Recommendation"),
        };
        foreach (var recipe in recipes)
        {
            var decl = recipe.Build();
            var code = TypeEmitter.Emit(decl);
            var ok = Runtime.CompileType(code, decl.Name).Name == decl.Name;
            failed |= !ok;
            Console.WriteLine($"===== {recipe.Name} => {decl.Name} {(ok ? "PASS" : "FAIL")} =====");
            Console.WriteLine(code);
            File.WriteAllText(Path.Combine(outDir, $"{decl.Name}.cs"), code);
        }

        Console.WriteLine(failed ? "FAIL" : "PASS");
        return failed ? 1 : 0;
    }

    // --- loop + var + sum, one recipe in four styles -> 10 -------------------------------------

    static Block LoopSum_Explicit()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return new Block(
            new DefineNode(acc, new Lit<int>(0)),
            new LoopNode(i, new Lit<int>(5), new Block(
                new AssignNode(acc, new AddNode(acc, i)))),
            new ReturnNode(acc));
    }

    static Block LoopSum_Brackets()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return [
            new DefineNode(acc, new Lit<int>(0)),
            new LoopNode(i, new Lit<int>(5), [
                new AssignNode(acc, new AddNode(acc, i))]),
            new ReturnNode(acc)];
    }

    static Block LoopSum_Factories()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return [
            Define(acc, 0),
            Loop(i, 5, Assign(acc, Add(acc, i))),
            Return(acc)];
    }

    static Block LoopSum_Operators()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return [
            Define(acc, 0),
            Loop(i, 5, Assign(acc, acc + i)),
            Return(acc)];
    }

    // --- other recipes, full style -------------------------------------------------------------

    static Block DefineReturn()
    {
        var x = new Var<int>("x");
        return [
            Define(x, 7),
            Return(x)];
    }

    static Block NestedLoops()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        var j = new Var<int>("j");
        return [
            Define(acc, 0),
            Loop(i, 3, Loop(j, 3, Assign(acc, acc + (i + j)))),
            Return(acc)];
    }

    static Block IfElseInLoop()
    {
        var acc = new Var<int>("acc");
        var i = new Var<int>("i");
        return [
            Define(acc, 0),
            Loop(i, 5,
                IfElse(i < 3,
                    [Assign(acc, acc + i), Assign(acc, acc + 1)],
                    Assign(acc, acc + 10))),
            Return(acc)];
    }
}

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Spike;

static class Program
{
    static int Main()
    {
        // The recipe: sum the loop indices 0..4.  (loop + var + sum)
        //   int acc = 0;
        //   for (int i = 0; i < 5; i++) acc = acc + i;
        //   return acc;
        var recipe = new Block(
            new DefineNode("acc", new Lit(0)),
            new LoopNode(new Lit(5), "i", new Block(
                new AssignNode("acc", new AddNode(new Ref("acc"), new Ref("i"))))),
            new ReturnNode(new Ref("acc")));

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

        var result = CompileAndRun(code);
        Console.WriteLine($"ScriptData.Run() = {result}  (expected 10)");

        if (result != 10)
        {
            Console.Error.WriteLine("FAIL: expected 10");
            return 1;
        }

        Console.WriteLine("PASS");
        return 0;
    }

    // Compiles the generated source in-memory and runs it — proving the output is real,
    // compiling, correct C#.
    static int CompileAndRun(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var refs = platform.Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create("Generated", [tree], refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
            throw new InvalidOperationException("generated code did not compile:\n" +
                string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        ms.Position = 0;
        var run = Assembly.Load(ms.ToArray()).GetType("ScriptData")!.GetMethod("Run")!;
        return (int)run.Invoke(null, null)!;
    }
}

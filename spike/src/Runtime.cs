using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Spike;

// Compiles generated source in-memory and runs ScriptData.Run() — the composition gate,
// proving the output is real, compiling, correct C#.
static class Runtime
{
    public static int CompileAndRun(string code)
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

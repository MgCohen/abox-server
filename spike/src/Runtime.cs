using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Spike;

// Compiles generated source in-memory — the composition gate, proving the output is real,
// compiling, correct C#. Body-tier recipes are run (ScriptData.Run()); declaration-tier recipes
// have nothing to run, so they are compiled and reflected to assert their shape.
static class Runtime
{
    public static int CompileAndRun(string code)
    {
        var run = Compile(code).GetType("ScriptData")!.GetMethod("Run")!;
        return (int)run.Invoke(null, null)!;
    }

    public static Type CompileType(string code, string typeName) =>
        Compile(code).GetType(typeName)
            ?? throw new InvalidOperationException($"type '{typeName}' not found in generated code");

    // Compile + construct + invoke a parameterless method — the gate for the method tier, proving
    // the body-tier code actually runs inside a real type.
    public static object Invoke(string code, string typeName, string methodName)
    {
        var type = CompileType(code, typeName);
        var instance = Activator.CreateInstance(type);
        return type.GetMethod(methodName)!.Invoke(instance, null)!;
    }

    static Assembly Compile(string code)
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

        return Assembly.Load(ms.ToArray());
    }
}

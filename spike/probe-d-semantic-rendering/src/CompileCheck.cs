using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ProbeD;

// Compile-check an emitted .cs (plus any support source it depends on, e.g. the Outer.Inner
// definition) with Roslyn. The whole point of the probe is that BOTH rendered variants are VALID,
// CORRECT C#: this is the gate that proves it, not eyeballing the output.
static class CompileCheck
{
    public sealed record Result(bool Ok, IReadOnlyList<string> Errors);

    public static Result Compile(string emitted, string supportTypes)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(emitted, options),
            CSharpSyntaxTree.ParseText(supportTypes, options),
        };
        var compilation = CSharpCompilation.Create(
            "ProbeD.CompileCheck",
            trees,
            Net.ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
        return new Result(errors.Count == 0, errors);
    }
}

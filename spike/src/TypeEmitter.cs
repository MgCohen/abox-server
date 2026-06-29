using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Spike;

// Lowers a declaration-tier node to a plain, owned .cs source string. No snippet substitution —
// a type declaration is structural, so it is rendered directly and normalized through Roslyn for
// the same canonical formatting the body-tier Generator produces.
static class TypeEmitter
{
    public static string Emit(TypeNode decl)
    {
        var source = decl switch
        {
            RecordNode r => $"using System;\n\npublic record {r.Name}({Positional(r.Members)});",
            ClassNode c => $"using System;\n\npublic class {c.Name}\n{{\n{Properties(c.Members)}\n}}",
            StructNode s => $"using System;\n\npublic struct {s.Name}\n{{\n{Properties(s.Members)}\n}}",
            EnumNode e => $"public enum {e.Name}\n{{\n{string.Join(",\n", e.Members)}\n}}",
            _ => throw new InvalidOperationException($"unknown declaration node {decl.GetType().Name}"),
        };

        return CSharpSyntaxTree.ParseText(source).GetRoot().NormalizeWhitespace().ToFullString() + "\n";
    }

    static string Positional(Field[] members) =>
        string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));

    static string Properties(Field[] members) =>
        string.Join("\n", members.Select(m => $"public {m.Type} {m.Name} {{ get; set; }}"));
}

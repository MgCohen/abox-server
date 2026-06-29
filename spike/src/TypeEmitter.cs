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
            RecordNode r => $"public record {r.Name}({Positional(r.Members)});",
            ClassNode c => $"public class {c.Name}\n{{\n{Members(c.Members)}\n}}",
            StructNode s => $"public struct {s.Name}\n{{\n{Properties(s.Members)}\n}}",
            EnumNode e => $"public enum {e.Name}\n{{\n{string.Join(",\n", e.Members)}\n}}",
            _ => throw new InvalidOperationException($"unknown declaration node {decl.GetType().Name}"),
        };

        return CSharpSyntaxTree.ParseText(source).GetRoot().NormalizeWhitespace().ToFullString() + "\n";
    }

    static string Positional(Field[] members) =>
        string.Join(", ", members.Select(m => $"{m.Type} {m.Name}"));

    static string Properties(Field[] members) =>
        string.Join("\n", members.Select(m => $"public {m.Type} {m.Name} {{ get; set; }}"));

    static string Members(Member[] members) =>
        string.Join("\n", members.Select(Render));

    // A method's signature is rendered here (declaration tier); its body is delegated to the body
    // tier (Generator.RenderBody) — the seam where the two tiers join.
    static string Render(Member member) => member switch
    {
        Field f => $"public {f.Type} {f.Name} {{ get; set; }}",
        MethodNode m => $"public {m.Returns} {m.Name}()\n{{\n{Generator.RenderBody(m.Body)}\n}}",
        _ => throw new InvalidOperationException($"unknown member {member.GetType().Name}"),
    };
}

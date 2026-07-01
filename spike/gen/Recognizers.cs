using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpikeGen;

// A node field discovered in a snippet, tagged with its source position so fields emit in
// source order regardless of which recognizer found them.
record Hole(string FieldType, string FieldName, int Position);

// One per hole kind. Decoupled by design: adding a new kind of hole = add a recognizer to
// the list in Program, and touch nothing else.
interface IHoleRecognizer
{
    IEnumerable<Hole> Recognize(MethodDeclarationSyntax method);
}

// by-value parameter -> value hole (IExpr<T>)
sealed class ValueParamRecognizer : IHoleRecognizer
{
    public IEnumerable<Hole> Recognize(MethodDeclarationSyntax m) =>
        m.ParameterList.Parameters
            .Where(p => p.Modifiers.Count == 0)
            .Select(p => new Hole($"IExpr<{p.Type}>", Naming.Pascal(p.Identifier.ValueText), p.SpanStart));
}

// ref parameter -> existing-name hole (string)
sealed class RefParamRecognizer : IHoleRecognizer
{
    public IEnumerable<Hole> Recognize(MethodDeclarationSyntax m) =>
        m.ParameterList.Parameters
            .Where(p => p.Modifiers.Any(mod => mod.Text == "ref"))
            .Select(p => new Hole("string", Naming.Pascal(p.Identifier.ValueText), p.SpanStart));
}

// `@`-marked identifier declared in the body -> new-name hole (string)
sealed class BodyMarkerRecognizer : IHoleRecognizer
{
    public IEnumerable<Hole> Recognize(MethodDeclarationSyntax m) =>
        (m.Body?.DescendantNodes().OfType<VariableDeclaratorSyntax>() ?? [])
            .Where(v => v.Identifier.Text.StartsWith('@'))
            .Select(v => new Hole("string", Naming.Pascal(v.Identifier.ValueText), v.SpanStart));
}

// Slot.Of<Block>() -> body hole (Block)
sealed class SlotRecognizer : IHoleRecognizer
{
    public IEnumerable<Hole> Recognize(MethodDeclarationSyntax m) =>
        (m.Body?.DescendantNodes().OfType<InvocationExpressionSyntax>() ?? [])
            .Where(IsSlotCall)
            .Select(inv => new Hole("Block", "Body", inv.SpanStart));

    static bool IsSlotCall(InvocationExpressionSyntax inv) =>
        inv.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "Slot" },
            Name: GenericNameSyntax { Identifier.ValueText: "Of" }
        };
}

static class Emitter
{
    public static string Node(MethodDeclarationSyntax m, IEnumerable<IHoleRecognizer> recognizers)
    {
        var fields = recognizers.SelectMany(r => r.Recognize(m))
            .OrderBy(h => h.Position)
            .Select(h => $"{h.FieldType} {h.FieldName}");
        var name = m.Identifier.ValueText + "Node";
        // An expression-bodied snippet (=> a + b) produces a value; a block-bodied one
        // ({ return value; }) produces statements — the body KIND, not the return type.
        var baseType = m.ExpressionBody is not null ? $"IExpr<{m.ReturnType}>" : "IStmt";
        return $"sealed record {name}({string.Join(", ", fields)}) : {baseType};";
    }
}

static class Naming
{
    public static string Pascal(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}

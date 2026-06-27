using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpikeGen;

// A node field discovered in a snippet, tagged with its source position so fields emit in
// source order regardless of which recognizer found them.
record Field(string FieldType, string FieldName, int Position);

// One per fill form. Decoupled by design: adding a new form = add a recognizer to the list
// in Program, and touch nothing else.
interface IFieldRecognizer
{
    IEnumerable<Field> Recognize(MethodDeclarationSyntax method);
}

// by-value parameter -> param fill (IExpr<T>)
sealed class ParamRecognizer : IFieldRecognizer
{
    public IEnumerable<Field> Recognize(MethodDeclarationSyntax m) =>
        m.ParameterList.Parameters
            .Where(p => p.Modifiers.Count == 0)
            .Select(p => new Field($"IExpr<{p.Type}>", Naming.Pascal(p.Identifier.ValueText), p.SpanStart));
}

// ref parameter -> marker fill (existing variable, Var<T> typed from the param)
sealed class RefMarkerRecognizer : IFieldRecognizer
{
    public IEnumerable<Field> Recognize(MethodDeclarationSyntax m) =>
        m.ParameterList.Parameters
            .Where(p => p.Modifiers.Any(mod => mod.Text == "ref"))
            .Select(p => new Field($"Var<{p.Type}>", Naming.Pascal(p.Identifier.ValueText), p.SpanStart));
}

// `@`-marked identifier declared in the body -> marker fill (new variable, Var<T> typed
// from the declaration: `int @i = 0` -> Var<int>)
sealed class BodyMarkerRecognizer : IFieldRecognizer
{
    public IEnumerable<Field> Recognize(MethodDeclarationSyntax m) =>
        (m.Body?.DescendantNodes().OfType<VariableDeclaratorSyntax>() ?? [])
            .Where(v => v.Identifier.Text.StartsWith('@'))
            .Select(v => new Field($"Var<{DeclaredType(v)}>", Naming.Pascal(v.Identifier.ValueText), v.SpanStart));

    static TypeSyntax DeclaredType(VariableDeclaratorSyntax v) =>
        ((VariableDeclarationSyntax)v.Parent!).Type;
}

// Block.Of("id") -> block fill (Block), the field named from the id
sealed class BlockRecognizer : IFieldRecognizer
{
    public IEnumerable<Field> Recognize(MethodDeclarationSyntax m) =>
        (m.Body?.DescendantNodes().OfType<InvocationExpressionSyntax>() ?? [])
            .Where(IsBlockCall)
            .Select(inv => new Field("Block", Naming.Pascal(BlockId(inv)), inv.SpanStart));

    static bool IsBlockCall(InvocationExpressionSyntax inv) =>
        inv.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "Block" },
            Name: IdentifierNameSyntax { Identifier.ValueText: "Of" }
        };

    static string BlockId(InvocationExpressionSyntax inv) =>
        ((LiteralExpressionSyntax)inv.ArgumentList.Arguments[0].Expression).Token.ValueText;
}

static class Emitter
{
    public static string Node(MethodDeclarationSyntax m, IEnumerable<IFieldRecognizer> recognizers)
    {
        var fields = recognizers.SelectMany(r => r.Recognize(m))
            .OrderBy(f => f.Position)
            .Select(f => $"{f.FieldType} {f.FieldName}");
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

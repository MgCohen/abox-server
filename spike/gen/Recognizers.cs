using Microsoft.CodeAnalysis;
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

// by-value parameter -> param fill (Expr<T>)
sealed class ParamRecognizer : IFieldRecognizer
{
    public IEnumerable<Field> Recognize(MethodDeclarationSyntax m) =>
        m.ParameterList.Parameters
            .Where(p => p.Modifiers.Count == 0)
            .Select(p => new Field($"Expr<{p.Type}>", Naming.Pascal(p.Identifier.ValueText), Body.UsePosition(m, p)));
}

// ref parameter -> marker fill (existing variable, Var<T> typed from the param)
sealed class RefMarkerRecognizer : IFieldRecognizer
{
    public IEnumerable<Field> Recognize(MethodDeclarationSyntax m) =>
        m.ParameterList.Parameters
            .Where(p => p.Modifiers.Any(mod => mod.Text == "ref"))
            .Select(p => new Field($"Var<{p.Type}>", Naming.Pascal(p.Identifier.ValueText), Body.UsePosition(m, p)));
}

// A parameter's field sorts by where it's first USED in the body, so fields read in template
// order: `int @var = value` -> var before value, `for (int @i …; @i < count …)` -> i before count.
static class Body
{
    public static int UsePosition(MethodDeclarationSyntax m, ParameterSyntax p)
    {
        SyntaxNode? body = (SyntaxNode?)m.Body ?? m.ExpressionBody;
        var use = body?.DescendantNodes().OfType<IdentifierNameSyntax>()
            .FirstOrDefault(n => n.Identifier.ValueText == p.Identifier.ValueText);
        return use?.SpanStart ?? p.SpanStart;
    }
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
        var fields = Fields(m, recognizers).Select(f => $"{f.FieldType} {f.FieldName}");
        return $"sealed record {m.Identifier.ValueText}Node({string.Join(", ", fields)}) : {BaseType(m)};";
    }

    public static string Factory(MethodDeclarationSyntax m, IEnumerable<IFieldRecognizer> recognizers)
    {
        var fields = Fields(m, recognizers).ToList();
        var ps = string.Join(", ", fields.Select(f => $"{f.FieldType} {Naming.Camel(f.FieldName)}"));
        var args = string.Join(", ", fields.Select(f => Naming.Camel(f.FieldName)));
        var name = m.Identifier.ValueText;
        return $"    public static {BaseType(m)} {name}({ps}) => new {name}Node({args});";
    }

    static IEnumerable<Field> Fields(MethodDeclarationSyntax m, IEnumerable<IFieldRecognizer> recognizers) =>
        recognizers.SelectMany(r => r.Recognize(m)).OrderBy(f => f.Position);

    // An expression-bodied snippet (=> a + b) produces a value; a block-bodied one
    // ({ return value; }) produces statements — the body KIND, not the return type.
    static string BaseType(MethodDeclarationSyntax m) =>
        m.ExpressionBody is not null ? $"Expr<{m.ReturnType}>" : "IStmt";
}

static class Naming
{
    public static string Pascal(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    public static string Camel(string s)
    {
        var name = char.ToLowerInvariant(s[0]) + s[1..];
        return Keywords.Contains(name) ? "@" + name : name;
    }

    static readonly HashSet<string> Keywords = ["var", "else", "ref", "int", "bool", "for", "if", "return"];
}

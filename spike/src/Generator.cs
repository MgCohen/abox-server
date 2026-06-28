using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spike;

// Lowers a recipe (a typed tree of nodes) to a plain ScriptData.cs string.
//
// Parse the real snippet source and substitute its fills at the node level:
//   params  -> a param usage      becomes a rendered child expression
//   markers -> an @-identifier     becomes a name from the recipe
//   blocks  -> a Block.Of("id")    becomes a rendered child block (its statements)
static class Generator
{
    static Dictionary<string, MethodDeclarationSyntax>? _snippets;

    static readonly Regex BlockCall = new(@"Block\s*\.\s*Of\s*\(\s*""(\w+)""\s*\)\s*;");

    public static string Generate(Block recipe)
    {
        LoadSnippets();

        var body = string.Join("\n", recipe.Statements.Select(RenderStmt));

        var full = $$"""
            public static class ScriptData
            {
                public static int Run()
                {
                    {{body}}
                }
            }
            """;

        return CSharpSyntaxTree.ParseText(full).GetRoot().NormalizeWhitespace().ToFullString();
    }

    // --- rendering -----------------------------------------------------------------------

    static string RenderStmt(Stmt node)
    {
        var method = Lookup(node.GetType());
        return SubstituteBlock(method.Body!, BuildFields(node));
    }

    static string RenderExpr(object node) => node switch
    {
        IVar v => v.Name,
        ILit l => FormatLit(l.Value),
        _ => SubstituteExpr(Lookup(node.GetType()).ExpressionBody!.Expression, BuildFields(node)),
    };

    static string FormatLit(object value) => value switch
    {
        bool b => b ? "true" : "false",
        _ => value.ToString()!,
    };

    static string RenderBlock(Block block) => string.Join("\n", block.Statements.Select(RenderStmt));

    // --- fill extraction -----------------------------------------------------------------

    static Fields BuildFields(object node)
    {
        var fields = new Fields();
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(node)!;
            var key = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];

            // Route by the FIELD type, not the value: a Var<T>-typed field is a binding marker,
            // but a Var used at an Expr<T>-typed site is an expression (its bare name).
            if (IsVar(prop.PropertyType))
                fields.Markers[key] = ((IVar)value).Name;
            else if (prop.PropertyType == typeof(Block))
                fields.Blocks[key] = RenderBlock((Block)value);
            else
                fields.Params[key] = RenderExpr(value);
        }
        return fields;
    }

    static bool IsVar(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Var<>);

    // --- substitution --------------------------------------------------------------------

    static string SubstituteBlock(BlockSyntax body, Fields fields)
    {
        var rewritten = (BlockSyntax)new FieldRewriter(fields).Visit(body)!;
        var text = string.Join("\n", rewritten.Statements.Select(s => s.NormalizeWhitespace().ToFullString()));
        return BlockCall.Replace(text, m => fields.Blocks.GetValueOrDefault(m.Groups[1].Value, ""));
    }

    static string SubstituteExpr(ExpressionSyntax expr, Fields fields)
    {
        var rewritten = (ExpressionSyntax)new FieldRewriter(fields).Visit(expr)!;
        return rewritten.NormalizeWhitespace().ToFullString();
    }

    // --- catalog -------------------------------------------------------------------------

    static MethodDeclarationSyntax Lookup(Type nodeType)
    {
        var name = nodeType.Name;
        var key = (name.EndsWith("Node") ? name[..^4] : name).ToLowerInvariant();
        return _snippets!.TryGetValue(key, out var m)
            ? m
            : throw new InvalidOperationException($"no snippet named '{key}' for node {name}");
    }

    static void LoadSnippets()
    {
        if (_snippets is not null) return;

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(Snippets.SourcePath)).GetRoot();
        _snippets = new();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var attr = method.AttributeLists.SelectMany(a => a.Attributes)
                .FirstOrDefault(a => a.Name.ToString() is "Snippet" or "SnippetAttribute");
            if (attr?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax lit)
                _snippets[lit.Token.ValueText] = method;
        }
    }

    sealed class Fields
    {
        public Dictionary<string, string> Params { get; } = new();
        public Dictionary<string, string> Markers { get; } = new();
        public Dictionary<string, string> Blocks { get; } = new();
    }

    sealed class FieldRewriter(Fields fields) : CSharpSyntaxRewriter
    {
        // params: a by-value param usage -> the rendered child expression (whole node)
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var id = node.Identifier;
            if (!id.Text.StartsWith('@') && fields.Params.TryGetValue(id.ValueText, out var expr))
                return SyntaxFactory.ParseExpression(expr).WithTriviaFrom(node);
            return base.VisitIdentifierName(node);
        }

        // markers: any `@`-marked identifier token (declarator OR usage) -> the recipe name
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && token.Text.StartsWith('@')
                && fields.Markers.TryGetValue(token.ValueText, out var name))
                return SyntaxFactory.Identifier(token.LeadingTrivia, name, token.TrailingTrivia);
            return base.VisitToken(token);
        }
    }
}

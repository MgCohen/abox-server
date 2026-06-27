using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spike;

// Lowers a recipe (a typed tree of nodes) to a plain ScriptData.cs string.
//
// Approach (per the design doc): PARSE the real snippet source and SUBSTITUTE holes at the
// node level — never hand-build syntax trees. Value/name holes are swapped via a Roslyn
// rewriter; the body hole (a recognizable `Slot.Of<...>()` call) is swapped post-render.
static class Generator
{
    static Dictionary<string, MethodDeclarationSyntax>? _snippets;

    static readonly Regex SlotCall = new(@"Slot\s*\.\s*Of\s*<[^>]*>\s*\(\s*\)\s*;");

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

    static string RenderStmt(IStmt node)
    {
        var method = Lookup(node.GetType());
        return SubstituteBlock(method.Body!, BuildHoles(node));
    }

    static string RenderExpr(object node) => node switch
    {
        Lit l => l.Value.ToString(),
        Ref r => r.Name,
        _ => SubstituteExpr(Lookup(node.GetType()).ExpressionBody!.Expression, BuildHoles(node)),
    };

    static string RenderBlock(Block block) => string.Join("\n", block.Statements.Select(RenderStmt));

    // --- hole extraction -----------------------------------------------------------------

    static Holes BuildHoles(object node)
    {
        var holes = new Holes();
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(node)!;
            var key = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];

            if (prop.PropertyType == typeof(string))
                holes.Names[key] = (string)value;
            else if (prop.PropertyType == typeof(Block))
                holes.Body = RenderBlock((Block)value);
            else
                holes.Values[key] = RenderExpr(value);
        }
        return holes;
    }

    // --- substitution --------------------------------------------------------------------

    static string SubstituteBlock(BlockSyntax body, Holes holes)
    {
        var rewritten = (BlockSyntax)new HoleRewriter(holes).Visit(body)!;
        var text = string.Join("\n", rewritten.Statements.Select(s => s.NormalizeWhitespace().ToFullString()));
        if (holes.Body is not null)
            text = SlotCall.Replace(text, _ => holes.Body);
        return text;
    }

    static string SubstituteExpr(ExpressionSyntax expr, Holes holes)
    {
        var rewritten = (ExpressionSyntax)new HoleRewriter(holes).Visit(expr)!;
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

    sealed class Holes
    {
        public Dictionary<string, string> Values { get; } = new();
        public Dictionary<string, string> Names { get; } = new();
        public string? Body { get; set; }
    }

    sealed class HoleRewriter(Holes holes) : CSharpSyntaxRewriter
    {
        // value holes: a by-value param usage -> the rendered child expression (whole node)
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var id = node.Identifier;
            if (!id.Text.StartsWith('@') && holes.Values.TryGetValue(id.ValueText, out var expr))
                return SyntaxFactory.ParseExpression(expr).WithTriviaFrom(node);
            return base.VisitIdentifierName(node);
        }

        // name holes: any `@`-marked identifier token (declarator OR usage) -> the recipe name
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && token.Text.StartsWith('@')
                && holes.Names.TryGetValue(token.ValueText, out var name))
                return SyntaxFactory.Identifier(token.LeadingTrivia, name, token.TrailingTrivia);
            return base.VisitToken(token);
        }
    }
}

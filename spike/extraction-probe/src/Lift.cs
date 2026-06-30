using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

// ============================================================================
// THE NEW MECHANISM — lift the author's REAL-CODE glue out of the recipe source and
// into the owned handler, via Roslyn.
//
// The glue is a compiler-checked lambda, never a string. The emitter does NOT execute
// the compiled delegate (you cannot recover clean source from IL); it reads the
// lambda's SYNTAX from the recipe file and splices it into the emitted method. While
// lifting it renames the author's natural parameter names (cart/cmd/ctx) to the
// canonical handler names (agg/command/scope). This is the "merge declaration" /
// semantic-model tech the spike validated (probe D), pointed at lambda bodies instead
// of type declarations.
//
// The rename here is SYNTACTIC (replace identifier tokens matching a parameter name).
// The robust version resolves the lambda's parameter symbols via the semantic model
// so only bound references move; for this probe the parameter names are unique within
// the bodies, so the syntactic pass is exact. Noted in the README limitations.
// ============================================================================
static class Lift
{
    public sealed record Glue(string LoadKeyExpression, IReadOnlyList<string> DivergenceStatements);

    const string CanonicalAggregate = "agg";
    const string CanonicalCommand = "command";
    const string CanonicalScope = "scope";

    public static Glue From(string recipeSource)
    {
        var root = CSharpSyntaxTree
            .ParseText(recipeSource, new CSharpParseOptions(LanguageVersion.Latest))
            .GetRoot();

        var loadBy = LambdaArgumentOf(root, "LoadBy");
        var with = LambdaArgumentOf(root, "With");

        return new Glue(
            LiftSelector((SimpleLambdaExpressionSyntax)loadBy),
            LiftBlock((ParenthesizedLambdaExpressionSyntax)with));
    }

    static LambdaExpressionSyntax LambdaArgumentOf(SyntaxNode root, string method)
    {
        var call = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.ValueText == method)
            ?? throw new InvalidOperationException($"could not find a .{method}(...) call in the recipe source.");
        var arg = call.ArgumentList.Arguments.SingleOrDefault()?.Expression
            ?? throw new InvalidOperationException($".{method}(...) must take exactly one lambda argument.");
        return arg as LambdaExpressionSyntax
            ?? throw new InvalidOperationException($".{method}(...)'s argument must be a lambda (was {arg.Kind()}).");
    }

    static string LiftSelector(SimpleLambdaExpressionSyntax lambda)
    {
        var rename = new Dictionary<string, string>
        {
            [lambda.Parameter.Identifier.ValueText] = CanonicalCommand,
        };
        var body = lambda.Body as ExpressionSyntax
            ?? throw new InvalidOperationException("LoadBy must be an expression lambda (cmd => cmd.Key).");
        return Rename(body, rename).ToString();
    }

    static IReadOnlyList<string> LiftBlock(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var ps = lambda.ParameterList.Parameters;
        if (ps.Count != 3)
            throw new InvalidOperationException("With must take (aggregate, command, scope).");
        var rename = new Dictionary<string, string>
        {
            [ps[0].Identifier.ValueText] = CanonicalAggregate,
            [ps[1].Identifier.ValueText] = CanonicalCommand,
            [ps[2].Identifier.ValueText] = CanonicalScope,
        };
        var block = lambda.Body as BlockSyntax
            ?? throw new InvalidOperationException("With must be a block lambda { ... }.");
        return block.Statements
            .Select(s => Rename(s, rename).ToString())
            .ToList();
    }

    static SyntaxNode Rename(SyntaxNode node, IReadOnlyDictionary<string, string> map) =>
        new ParameterRenamer(map).Visit(node)!;

    sealed class ParameterRenamer(IReadOnlyDictionary<string, string> map) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            map.TryGetValue(node.Identifier.ValueText, out var to)
                ? node.WithIdentifier(SyntaxFactory.Identifier(to)).WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

// Lift the real-code glue (the `key` selector + the `body` divergence) out of a recipe
// method's source, renaming the author's lambda params to the canonical handler names.
// Same Roslyn lift as the extraction probe; here it pulls the named `key:` / `body:`
// arguments of the Mutate(...) call.
static class Lift
{
    public sealed record Glue(string KeyExpression, IReadOnlyList<string> BodyStatements);

    const string CanonicalAggregate = "agg";
    const string CanonicalCommand = "command";

    public static Glue From(string recipeSource, string methodName)
    {
        var root = CSharpSyntaxTree
            .ParseText(recipeSource, new CSharpParseOptions(LanguageVersion.Latest))
            .GetRoot();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == methodName)
            ?? throw new InvalidOperationException($"no method '{methodName}' in the recipe source.");

        var mutate = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.ValueText == "Mutate")
            ?? throw new InvalidOperationException("no .Mutate(...) call in the recipe.");

        var key = (SimpleLambdaExpressionSyntax)LambdaArg(mutate, "key");
        var body = (ParenthesizedLambdaExpressionSyntax)LambdaArg(mutate, "body");

        return new Glue(LiftKey(key), LiftBody(body));
    }

    static LambdaExpressionSyntax LambdaArg(InvocationExpressionSyntax call, string name)
    {
        var arg = call.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name)
            ?? throw new InvalidOperationException($"Mutate(...) has no '{name}:' argument.");
        return arg.Expression as LambdaExpressionSyntax
            ?? throw new InvalidOperationException($"'{name}:' must be a lambda.");
    }

    static string LiftKey(SimpleLambdaExpressionSyntax lambda)
    {
        var rename = new Dictionary<string, string> { [lambda.Parameter.Identifier.ValueText] = CanonicalCommand };
        var body = lambda.Body as ExpressionSyntax
            ?? throw new InvalidOperationException("key must be an expression lambda (c => …).");
        return Rename(body, rename).ToString();
    }

    static IReadOnlyList<string> LiftBody(ParenthesizedLambdaExpressionSyntax lambda)
    {
        var ps = lambda.ParameterList.Parameters;
        if (ps.Count != 2)
            throw new InvalidOperationException("body must take (aggregate, command).");
        var rename = new Dictionary<string, string>
        {
            [ps[0].Identifier.ValueText] = CanonicalAggregate,
            [ps[1].Identifier.ValueText] = CanonicalCommand,
        };
        return lambda.Body switch
        {
            BlockSyntax block => block.Statements.Select(s => Rename(s, rename).ToString()).ToList(),
            ExpressionSyntax expr => new[] { Rename(expr, rename) + ";" },
            _ => throw new InvalidOperationException("body must be a block or expression lambda."),
        };
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

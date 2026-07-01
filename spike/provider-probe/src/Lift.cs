using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

// Lift the recipe's shape out of its source: the command type (from Feature<TCmd>), the
// provider (aggregate + factory, from the `via:` argument), and the real-code glue (the
// `key` selector + the `body` divergence), renaming author lambda params to canonical names.
static class Lift
{
    public sealed record Recipe(
        string Command,
        string Aggregate,
        string StoreFactory,
        string KeyExpression,
        IReadOnlyList<string> BodyStatements)
    {
        public string FeatureName => Command.EndsWith("Command") ? Command[..^"Command".Length] : Command;
    }

    const string CanonicalAggregate = "agg";
    const string CanonicalCommand = "command";

    public static Recipe From(string recipeSource, string methodName)
    {
        var root = CSharpSyntaxTree
            .ParseText(recipeSource, new CSharpParseOptions(LanguageVersion.Latest))
            .GetRoot();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == methodName)
            ?? throw new InvalidOperationException($"no method '{methodName}' in the recipe source.");

        var command = FeatureCommandType(method);
        var mutate = MutateCall(method);
        var (aggregate, factory) = ProviderFromVia(mutate);

        var key = (SimpleLambdaExpressionSyntax)LambdaArg(mutate, "key");
        var body = (ParenthesizedLambdaExpressionSyntax)LambdaArg(mutate, "body");

        return new Recipe(command, aggregate, factory, LiftKey(key), LiftBody(body));
    }

    // `new Feature<AddPointsCommand>(scope => …)` — the builder fixes TCmd; we read it here.
    static string FeatureCommandType(MethodDeclarationSyntax method)
    {
        var feature = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault(o => o.Type is GenericNameSyntax g && g.Identifier.ValueText == "Feature")
            ?? throw new InvalidOperationException("no `new Feature<…>(…)` in the recipe.");
        return ((GenericNameSyntax)feature.Type).TypeArgumentList.Arguments[0].ToString();
    }

    // The canonical action form is the free function `Mutate(scope, via:…, key:…, body:…)`.
    static InvocationExpressionSyntax MutateCall(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "Mutate")
        ?? throw new InvalidOperationException("no `Mutate(scope, …)` call in the recipe.");

    // `via: Stores.Repository<User>()` -> aggregate "User", factory "Repository".
    static (string Aggregate, string Factory) ProviderFromVia(InvocationExpressionSyntax mutate)
    {
        var via = mutate.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "via")?.Expression
            ?? throw new InvalidOperationException("Mutate(...) has no 'via:' argument.");

        if (via is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax g } })
            throw new InvalidOperationException("'via:' must be a Stores.<Factory><T>() call.");

        return (g.TypeArgumentList.Arguments[0].ToString(), g.Identifier.ValueText);
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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

// Lift the recipe's shape out of its source: the command type (from Feature<TCmd>), the
// aggregate (from the store's type argument), and the named-arg fills of the Mutate(...)
// call. Expression args (store, key) become expression fills; a lambda arg (body) becomes a
// block fill. Author names are renamed to canonical handler names: scope.Command -> command,
// the body lambda's parameter -> agg.
static class Lift
{
    public sealed record Recipe(
        string Command,
        string Aggregate,
        string StoreName,
        IReadOnlyDictionary<string, string> ExprFills,
        IReadOnlyDictionary<string, string> BlockFills)
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

        var exprFills = new Dictionary<string, string>();
        var blockFills = new Dictionary<string, string>();
        (string Aggregate, string StoreName)? store = null;

        foreach (var arg in mutate.ArgumentList.Arguments)
        {
            var name = arg.NameColon?.Name.Identifier.ValueText
                ?? throw new InvalidOperationException("Mutate(...) args must be named (store:, key:, body:).");

            if (name == "store")
                store = StoreOf(arg.Expression);

            if (arg.Expression is LambdaExpressionSyntax lambda)
                blockFills[name] = string.Join("\n", LiftBody(lambda));
            else
                exprFills[name] = Canonicalize(arg.Expression).ToString();
        }

        if (store is null)
            throw new InvalidOperationException("Mutate(...) has no 'store:' argument.");

        return new Recipe(command, store.Value.Aggregate, store.Value.StoreName, exprFills, blockFills);
    }

    static string FeatureCommandType(MethodDeclarationSyntax method)
    {
        var feature = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault(o => o.Type is GenericNameSyntax g && g.Identifier.ValueText == "Feature")
            ?? throw new InvalidOperationException("no `new Feature<…>(…)` in the recipe.");
        return ((GenericNameSyntax)feature.Type).TypeArgumentList.Arguments[0].ToString();
    }

    static InvocationExpressionSyntax MutateCall(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "Mutate")
        ?? throw new InvalidOperationException("no `Mutate(store:, key:, body:)` call in the recipe.");

    // `store: Stores.Repository<User>()` -> aggregate "User", store name "Repository".
    static (string Aggregate, string StoreName) StoreOf(ExpressionSyntax via)
    {
        if (via is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax g } })
            return (g.TypeArgumentList.Arguments[0].ToString(), g.Identifier.ValueText);
        throw new InvalidOperationException("'store:' must be a Stores.<Factory><T>() call.");
    }

    static IReadOnlyList<string> LiftBody(LambdaExpressionSyntax lambda)
    {
        var param = lambda switch
        {
            SimpleLambdaExpressionSyntax s => s.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax p when p.ParameterList.Parameters.Count == 1
                => p.ParameterList.Parameters[0].Identifier.ValueText,
            _ => throw new InvalidOperationException("body must be a single-parameter lambda (agg => …)."),
        };
        var rename = new Dictionary<string, string> { [param] = CanonicalAggregate };
        return lambda.Body switch
        {
            BlockSyntax block => block.Statements.Select(s => Canonicalize(Rename(s, rename)).ToString()).ToList(),
            ExpressionSyntax expr => new[] { Canonicalize(Rename(expr, rename)) + ";" },
            _ => throw new InvalidOperationException("body must be a block or expression lambda."),
        };
    }

    // scope.Command -> command, everywhere (safe: store expressions carry no scope.Command).
    static SyntaxNode Canonicalize(SyntaxNode node) => new ScopeCommandRewriter().Visit(node)!;

    static SyntaxNode Rename(SyntaxNode node, IReadOnlyDictionary<string, string> map) =>
        new ParameterRenamer(map).Visit(node)!;

    sealed class ScopeCommandRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node is { Expression: IdentifierNameSyntax { Identifier.ValueText: "scope" }, Name.Identifier.ValueText: "Command" })
                return SyntaxFactory.IdentifierName(CanonicalCommand).WithTriviaFrom(node);
            return base.VisitMemberAccessExpression(node);
        }
    }

    sealed class ParameterRenamer(IReadOnlyDictionary<string, string> map) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            map.TryGetValue(node.Identifier.ValueText, out var to)
                ? node.WithIdentifier(SyntaxFactory.Identifier(to)).WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
    }
}

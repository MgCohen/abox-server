using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Probe;

// PROBE B at emit time (reused infra). The handler body is assembled from the typed
// scaffold (scope.Get<Repo<TAgg>>) plus the LIFTED glue (which may scope.Ask<T>), so
// this scans the assembled body text for scope.Get<T>() / scope.Ask<T>() additively —
// no body rewrite — and resolves each T through the shared ResolutionModel. The output
// drives the emitted RegisterDiscovered(container).
static class WiringScan
{
    public enum DepKind { Service, Query }

    public readonly record struct Dependency(DepKind Kind, string RenderedType);

    public static IReadOnlyList<Dependency> Scan(string handlerBody, string scopeParam, ResolutionModel model)
    {
        var wrapped = $"class __W__ {{ object __m__(object {scopeParam}) {{ {handlerBody} return null!; }} }}";
        var tree = CSharpSyntaxTree.ParseText(wrapped, new CSharpParseOptions(LanguageVersion.Latest));

        var found = new List<Dependency>();
        foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax ma)
                continue;
            if (ma.Expression is not IdentifierNameSyntax recv || recv.Identifier.ValueText != scopeParam)
                continue;
            if (ma.Name is not GenericNameSyntax verb || verb.TypeArgumentList.Arguments.Count != 1)
                continue;

            var kind = verb.Identifier.ValueText switch
            {
                "Get" => DepKind.Service,
                "Ask" => DepKind.Query,
                _ => (DepKind?)null,
            };
            if (kind is null)
                continue;

            var typeText = verb.TypeArgumentList.Arguments[0].ToString();
            var rendered = ResolutionModel.Render(model.Resolve(typeText));
            found.Add(new Dependency(kind.Value, rendered));
        }

        return found
            .GroupBy(d => (d.Kind, d.RenderedType))
            .Select(g => g.First())
            .ToList();
    }
}

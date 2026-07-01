using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Slice;

// PROBE B at emit time. The isolated probe scanned the CONSUMER's compilation as
// an in-build IIncrementalGenerator. Here the markers live in the recipe's glue
// TEXT, so the emit tool parses that text and scans it for scope.Get<T>() /
// scope.Ask<T>() — additively, no body rewrite. T is resolved through the shared
// ResolutionModel, so a Get<Repo<User>> renders idiomatically AND a marker over a
// minted model would resolve too.
//
// The output drives the emitted RegisterDiscovered(container) method.
static class WiringScan
{
    public enum DepKind { Service, Query }

    public readonly record struct Dependency(DepKind Kind, string RenderedType);

    public static IReadOnlyList<Dependency> Scan(string glueBody, string scopeParam, ResolutionModel model)
    {
        // Parse the glue body as the body of a throwaway method so the call sites
        // are real syntax we can walk (the text alone is not a parseable unit).
        var wrapped = $"class __W__ {{ object __m__(object {scopeParam}) {{ {glueBody} return null!; }} }}";
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

            // Resolve T through the shared model so the rendered type is idiomatic
            // and identical to how the same type renders elsewhere in the feature.
            var typeText = verb.TypeArgumentList.Arguments[0].ToString();
            var rendered = ResolutionModel.Render(model.Resolve(typeText));
            found.Add(new Dependency(kind.Value, rendered));
        }

        // Distinct (kind, type): a looped/repeated marker is one dependency.
        return found
            .GroupBy(d => (d.Kind, d.RenderedType))
            .Select(g => g.First())
            .ToList();
    }
}
